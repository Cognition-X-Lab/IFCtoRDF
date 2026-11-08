using g3;
using Microsoft.Isam.Esent.Interop;
using System;
using System.CodeDom;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xbim.Common.Geometry;
using Xbim.Common.Step21;
using Xbim.Common.XbimExtensions;
using Xbim.Ifc;
using Xbim.Ifc.Extensions;
using Xbim.IO;
using Xbim.ModelGeometry.Scene;

namespace IFCtoRDF
{
    public static class IfcConverter
    {
        public static void SaveXBIMFile(string inputPath, string outputPath)
        {
            IfcStore model = IfcStore.Open(inputPath);
            IfcConverter.SaveModelAsXBIM(model, outputPath);
        }

        public static BIMModel GetModelFromIFC(string modelPath, bool flipTriangles = false)
        {
            var modelIfc = IfcStore.Open(modelPath);
            double scale = modelIfc.ModelFactors.LengthToMetresConversionFactor;
            bool createContext = false;
            if (Path.GetExtension(modelPath) == ".xbim")
            {
                createContext = false;
            }
            if (Path.GetExtension(modelPath) == ".ifc")
            {
                createContext = true;
            }
            BIMModel model = GetModelFromIFC(modelIfc, createContext, scale, flipTriangles);
            model.Properties.Add("Original File Type", ConverterGeneral.Datasets.IFC.ToString());
            MakeAllIDsUnique(model);
            return model;
        }

        private static BIMModel GetModelFromIFC(IfcStore model, bool createContext, double scale, bool flipTriangles)
        {
            // Get the objects:
            ConcurrentDictionary<string, ccBIMObject> objectDict = GetBIMObjects(model, createContext, scale, flipTriangles);

            Dictionary<string, BIMObject> objectDictionaryFixed = new Dictionary<string, BIMObject>();
            foreach (var obj in objectDict.Values)
            {
                BIMObject fixedObj = obj.MakeRegularBIMObject();
                objectDictionaryFixed.Add(fixedObj.ID.ToString(), fixedObj);
            }

            ConcurrentBag<BIMRelation3> relationDict = new ConcurrentBag<BIMRelation3>();
            Dictionary<string, List<string>> materialsDict = new Dictionary<string, List<string>>();
            switch (model.SchemaVersion)
            {
                case XbimSchemaVersion.Ifc4:
                    relationDict = GetRelationInModel4(model, out materialsDict);
                    break;
                case XbimSchemaVersion.Ifc2X3:
                    relationDict = GetRelationInModel2x3(model, out materialsDict);
                    break;
                case XbimSchemaVersion.Ifc4x3:
                    relationDict = GetRelationInModel4x3(model, out materialsDict);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported IFC schema version: {model.SchemaVersion}");
            }

            List<BIMRelation3> relationDictionaryFixed = new List<BIMRelation3>();
            foreach (var rel in relationDict)
            {
                relationDictionaryFixed.Add(rel);
            }

            BIMModel finalModel = new BIMModel()
            {
                ID = model.UserDefinedId.ToString(),
                Objects = objectDictionaryFixed,
                Relations = relationDictionaryFixed
            };

            foreach (var materialAssign in materialsDict)
            {
                if (finalModel.Objects.ContainsKey(materialAssign.Key))
                {
                    finalModel.Objects[materialAssign.Key].Properties.Add("Material", string.Join(",", materialAssign.Value));
                }
                else
                {
                    var compsMatchMats = finalModel.Objects.SelectMany(o => o.Value.Components).Where(c => c.Key == materialAssign.Key);
                    foreach (var compsMatchMat in compsMatchMats)
                    {
                        compsMatchMat.Value.Properties.Add("Material", string.Join(",", materialAssign.Value));
                    }

                    if (compsMatchMats.Count() == 0)
                    {
                        // So far I noticed this related to IFCWINDOWSTYLE and IFCFURNISHINGTYPE
                        // The materials part could be moved to within the object itself...
                    }
                }
            }

            finalModel.Properties.Add("Name", Path.GetFileNameWithoutExtension(model.FileName));

            return finalModel;
        }

        private static void MakeAllIDsUnique(BIMModel model)
        {
            foreach (string label in model.Objects.Keys.ToList())
            {
                BIMObject obj = model.Objects[label];
                model.Objects.Remove(label);
                obj.ID = Guid.NewGuid().ToString();
                model.Objects.Add(obj.ID, obj);

                BIMModel.SwapIDsInRelations(ref model, label, obj.ID);
            }
            foreach (string label in model.Objects.Keys.ToList())
            {
                BIMObject obj = model.Objects[label];
                foreach (string complabel in model.Objects[obj.ID].Components.Keys.ToList())
                {
                    BIMComponent comp = model.Objects[obj.ID].Components[complabel];
                    model.Objects[obj.ID].Components.Remove(complabel);
                    comp.ID = Guid.NewGuid().ToString();
                    model.Objects[obj.ID].Components.Add(comp.ID, comp);

                    // In case there are relations to components (not implemented yet, be careful since component original labels are not really unique)
                    //BIMModel.SwapIDsInRelations(ref model, complabel, comp.ID);
                }
            }
        }

        private static ConcurrentDictionary<string, ccBIMObject> GetBIMObjects(IfcStore model, bool createContext, double scale, bool flipTriangles)
        {
            ConcurrentDictionary<string, ccBIMObject> objectDict = new ConcurrentDictionary<string, ccBIMObject>();
            switch (model.SchemaVersion)
            {
                case XbimSchemaVersion.Ifc4:
                    objectDict = GetObjectInModel4(model);
                    break;
                case XbimSchemaVersion.Ifc2X3:
                    objectDict = GetObjectInModel2x3(model);
                    break;
                case XbimSchemaVersion.Ifc4x3:
                    objectDict = GetObjectInModel4x3(model);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported IFC schema version: {model.SchemaVersion}");
            }

            Xbim3DModelContext context = new Xbim3DModelContext(model);
            if (createContext)
            {
                context.CreateContext();
            }

            //List<XbimShapeGeometry> geometrys = context.ShapeGeometries().ToList();
            List<XbimShapeInstance> shapeInstances = context.ShapeInstances().ToList();

            //Check all the instances
            Parallel.ForEach(shapeInstances, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, shapeInstance =>
            {
                if (shapeInstance.RepresentationType != XbimGeometryRepresentationType.OpeningsAndAdditionsIncluded)
                {
                    return;
                }

                GetComponentsAndMapToObject(flipTriangles, shapeInstance, objectDict, context);
            });

            // Next bit is for making the local center of all objects the origin:
            Parallel.ForEach(objectDict.Values, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, obj =>
            {
                obj.CenterLocation = CenterObject(obj.Components.Values.ToList(), scale, obj.Orientation);
            });

            return objectDict;
        }

        private static void GetComponentsAndMapToObject(bool flipTriangles, XbimShapeInstance shapeInstance, ConcurrentDictionary<string, ccBIMObject> objectDict, Xbim3DModelContext context)
        {
            var transfor = shapeInstance.Transformation; //Transformation matrix (location point inside)

            XbimShapeGeometry geometry = context.ShapeGeometry(shapeInstance);   //Instance's geometry
                                                                                 //XbimRect3D box = geometry.BoundingBox; //bounding box

            List<int[]> triangles = new List<int[]>();
            //List<XbimPoint3D> localverts = new List<XbimPoint3D>();
            List<XbimPoint3D> globalverts = new List<XbimPoint3D>();

            //If you want to get all the faces and trinagulation use this
            using (var stream = new MemoryStream(((IXbimShapeGeometryData)geometry).ShapeData))
            {
                using (var reader = new BinaryReader(stream))
                {
                    var mesh = reader.ReadShapeTriangulation();
                    foreach (var face in mesh.Faces)
                    {
                        for (int i = 0; i < face.TriangleCount; i++)
                        {
                            int index = i * 3;
                            triangles.Add(new int[3] { face.Indices[index], face.Indices[index + 1], face.Indices[index + 2] });
                        }
                    }
                    foreach (XbimPoint3D p in mesh.Vertices)
                    {
                        //localverts.Add(p);
                        var tp = transfor.Transform(p);
                        globalverts.Add(tp);
                    }
                }
            }

            BIMComponent objectComponent = new BIMComponent()
            {
                Properties = new Dictionary<string, object>
                                {
                                    { "IfcTypeId", shapeInstance.IfcTypeId.ToString() },
                                    { "IfcProductLabel", shapeInstance.IfcProductLabel.ToString() },
                                    { "InstanceLabel", shapeInstance.InstanceLabel.ToString() },
                                    { "RepresentationContext", shapeInstance.RepresentationContext.ToString() },
                                    { "StyleLabel", shapeInstance.StyleLabel.ToString() },
                                    { "ShapeGeometryLabel", shapeInstance.ShapeGeometryLabel.ToString() }
                                },
                ID = shapeInstance.ShapeGeometryLabel.ToString(),
                Mesh = DMesh3Builder.Build<Vector3d, Index3i, Vector3d>(
                                        globalverts.Select(v => ConverterGeneral.VectorConverter(v)),
                                        triangles.Select(t => (new Index3i(flipTriangles ? t : t.Reverse().ToArray()))))
            };

            objectDict[shapeInstance.IfcProductLabel.ToString()].Components.TryAdd(objectComponent.ID.ToString(), objectComponent);
        }

        public static Vector3d CenterObject(List<BIMComponent> globalComponents, double scale, Vector4d orientation)
        {
            List<Vector3d> allVects = globalComponents.SelectMany(vl => vl.Mesh.Vertices()).ToList();
            GeomUtils.GetXYZDimentions(allVects, out Vector3d objCenter, out Vector3d objDims);

            // Move all verticies to the center and the reverse orinetation (so they face forward):
            Matrix4 objTranslation = GeomUtils.GetTranslationMatrixFromLocationOrientation(objCenter, orientation);
            Matrix4 objTranslationInverse = objTranslation.GetInverse(out bool invertable);
            if (invertable)
            {
                foreach (BIMComponent component in globalComponents)
                {
                    List<Vector3d> newVerts = new List<Vector3d>();
                    foreach (Vector3d oldVect in component.Mesh.Vertices())
                    {
                        Vector4d oldVertVect = new Vector4d(oldVect.x, oldVect.y, oldVect.z, 1.0);
                        Vector4d oldVertTranslated = Matrix4.Multiply(objTranslationInverse, oldVertVect);
                        oldVertTranslated = oldVertTranslated * scale;
                        newVerts.Add(new Vector3d(oldVertTranslated.x, oldVertTranslated.y, oldVertTranslated.z));
                    }
                    component.Mesh = DMesh3Builder.Build<Vector3d, Index3i, Vector3d>(newVerts, component.Mesh.Triangles());
                }
            }

            objCenter = objCenter * scale;
            return objCenter;
        }

        public static ConcurrentDictionary<string, ccBIMObject> GetObjectInModel2x3(IfcStore model)
        {
            ConcurrentDictionary<string, ccBIMObject> objectList = new ConcurrentDictionary<string, ccBIMObject>();
            var modelInstances = model.Instances.OfType<Xbim.Ifc2x3.Kernel.IfcProduct>().ToList();
            Parallel.ForEach(modelInstances, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, modelInstance =>
            {
                //modelInstances.ForEach(modelInstance =>            {
                ccBIMObject instanceObject = new ccBIMObject
                {
                    ID = modelInstance.EntityLabel.ToString(),
                    Orientation = ConverterGeneral.VectorConverter(modelInstance.ObjectPlacement.ToMatrix3D().GetRotationQuaternion())
                };
                instanceObject.Properties.Add("ID", modelInstance.GlobalId);
                instanceObject.Properties.Add("EntityLabel", modelInstance.EntityLabel.ToString());
                instanceObject.Properties.Add("Name", modelInstance.Name);
                instanceObject.Properties.Add("IfcType", modelInstance.GetType().ToString());
                instanceObject.Properties.Add("IfcObjectType", modelInstance.ObjectType);
                instanceObject.Properties.Add("Description", modelInstance.Description);
                instanceObject.Properties.Add("BuildingName", Path.GetFileNameWithoutExtension(model.FileName));

                try
                {
                    System.Collections.Generic.List<Xbim.Ifc2x3.MaterialResource.IfcMaterial> materials = new System.Collections.Generic.List<Xbim.Ifc2x3.MaterialResource.IfcMaterial>();

                    // ---------------------------------------------------------
                    // 1. Determine the material-assignment type
                    // ---------------------------------------------------------

                    if (modelInstance.Material is Xbim.Ifc2x3.MaterialResource.IfcMaterial singleMaterial)
                    {
                        materials.Add(singleMaterial);
                    }
                    else if (modelInstance.Material is Xbim.Ifc2x3.MaterialResource.IfcMaterialList materialList)
                    {
                        materials.AddRange(materialList.Materials.Where(material => material != null));
                    }
                    else if (modelInstance.Material is Xbim.Ifc2x3.MaterialResource.IfcMaterialLayerSet layerSet)
                    {
                        materials.AddRange(layerSet.MaterialLayers.Where(layer => layer.Material != null).Select(layer => layer.Material));
                    }
                    else if (modelInstance.Material is Xbim.Ifc2x3.MaterialResource.IfcMaterialLayerSetUsage layerSetUsage)
                    {
                        materials.AddRange(layerSetUsage.ForLayerSet.MaterialLayers.Where(layer => layer.Material != null).Select(layer => layer.Material));
                    }
                    else
                    {
                        string actualType = modelInstance.Material == null ? "null" : modelInstance.Material.GetType().FullName;
                        if (modelInstance.Material != null)
                        {
                            Debug.Print("Unsupported material type for " + modelInstance.EntityLabel + ": " + actualType);
                        }
                    }

                    // ---------------------------------------------------------
                    // 2. Store the material names
                    // ---------------------------------------------------------

                    if (materials.Count > 0)
                    {
                        string materialNames = string.Join(";", materials.Select(material => material.Name.ToString()).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct());

                        if (!string.IsNullOrWhiteSpace(materialNames))
                        {
                            instanceObject.Properties["InstanceMaterial"] = materialNames;
                        }
                        // -----------------------------------------------------
                        // 3. Find all available colours in priority order
                        // -----------------------------------------------------

                        List<Xbim.Ifc2x3.RepresentationResource.IfcRepresentation> productRepresentations = new List<Xbim.Ifc2x3.RepresentationResource.IfcRepresentation>();
                        List<Xbim.Ifc2x3.GeometryResource.IfcRepresentationItem> productRepresentationItems = new List<Xbim.Ifc2x3.GeometryResource.IfcRepresentationItem>();
                        HashSet<int> visitedRepresentations = new HashSet<int>();

                        void CollectRepresentationItems(Xbim.Ifc2x3.RepresentationResource.IfcRepresentation representation)
                        {
                            if (representation == null || !visitedRepresentations.Add(representation.EntityLabel))
                            {
                                return;
                            }

                            productRepresentations.Add(representation);

                            foreach (Xbim.Ifc2x3.GeometryResource.IfcRepresentationItem representationItem in representation.Items)
                            {
                                productRepresentationItems.Add(representationItem);

                                if (representationItem is Xbim.Ifc2x3.GeometryResource.IfcMappedItem mappedItem && mappedItem.MappingSource != null && mappedItem.MappingSource.MappedRepresentation != null)
                                {
                                    CollectRepresentationItems(mappedItem.MappingSource.MappedRepresentation);
                                }
                            }
                        }

                        // -----------------------------------------------------
                        // 3.1 Collect product geometry
                        // -----------------------------------------------------

                        if (modelInstance is Xbim.Ifc2x3.Kernel.IfcProduct product && product.Representation != null)
                        {
                            foreach (Xbim.Ifc2x3.RepresentationResource.IfcRepresentation representation in product.Representation.Representations)
                            {
                                CollectRepresentationItems(representation);
                            }
                        }

                        List<Xbim.Ifc2x3.PresentationAppearanceResource.IfcStyledItem> geometryStyledItems = productRepresentationItems
                                .SelectMany((Xbim.Ifc2x3.GeometryResource.IfcRepresentationItem representationItem) => representationItem.StyledByItem)
                                .Concat(productRepresentationItems.OfType<Xbim.Ifc2x3.PresentationAppearanceResource.IfcStyledItem>())
                                .Distinct()
                                .ToList();

                        List<Xbim.Ifc2x3.PresentationAppearanceResource.IfcSurfaceStyle> geometrySurfaceStyles = geometryStyledItems
                                .SelectMany((Xbim.Ifc2x3.PresentationAppearanceResource.IfcStyledItem styledItem) => styledItem.Styles)
                                .SelectMany((Xbim.Ifc2x3.PresentationAppearanceResource.IfcPresentationStyleAssignment styleAssignment) => styleAssignment.Styles)
                                .OfType<Xbim.Ifc2x3.PresentationAppearanceResource.IfcSurfaceStyle>()
                                .ToList();

                        // Priority 1: Geometry diffuse colours

                        List<Xbim.Ifc2x3.PresentationResource.IfcColourRgb> geometryDiffuseColours = geometrySurfaceStyles
                                .SelectMany((Xbim.Ifc2x3.PresentationAppearanceResource.IfcSurfaceStyle surfaceStyle) => surfaceStyle.Styles)
                                .OfType<Xbim.Ifc2x3.PresentationAppearanceResource.IfcSurfaceStyleRendering>()
                                .Select((Xbim.Ifc2x3.PresentationAppearanceResource.IfcSurfaceStyleRendering rendering) => rendering.DiffuseColour)
                                .OfType<Xbim.Ifc2x3.PresentationResource.IfcColourRgb>()
                                .ToList();

                        // Priority 2: Geometry surface colours

                        List<Xbim.Ifc2x3.PresentationResource.IfcColourRgb> geometrySurfaceColours = geometrySurfaceStyles
                                .SelectMany((Xbim.Ifc2x3.PresentationAppearanceResource.IfcSurfaceStyle surfaceStyle) => surfaceStyle.Styles)
                                .OfType<Xbim.Ifc2x3.PresentationAppearanceResource.IfcSurfaceStyleShading>()
                                .Select((Xbim.Ifc2x3.PresentationAppearanceResource.IfcSurfaceStyleShading shading) => shading.SurfaceColour)
                                .Where(colourValue => colourValue != null)
                                .ToList();

                        // Priority 3: Geometry curve colours

                        List<Xbim.Ifc2x3.PresentationResource.IfcColourRgb> geometryCurveColours = geometryStyledItems
                                .SelectMany((Xbim.Ifc2x3.PresentationAppearanceResource.IfcStyledItem styledItem) => styledItem.Styles)
                                .SelectMany((Xbim.Ifc2x3.PresentationAppearanceResource.IfcPresentationStyleAssignment styleAssignment) => styleAssignment.Styles)
                                .OfType<Xbim.Ifc2x3.PresentationAppearanceResource.IfcCurveStyle>()
                                .Select((Xbim.Ifc2x3.PresentationAppearanceResource.IfcCurveStyle curveStyle) => curveStyle.CurveColour)
                                .OfType<Xbim.Ifc2x3.PresentationResource.IfcColourRgb>()
                                .ToList();

                        // -----------------------------------------------------
                        // 3.2 Collect material colours
                        // -----------------------------------------------------

                        List<Xbim.Ifc2x3.PresentationAppearanceResource.IfcStyledItem> materialStyledItems = materials
                                .Where(material => material != null)
                                .SelectMany(material => material.HasRepresentation)
                                .SelectMany((Xbim.Ifc2x3.RepresentationResource.IfcMaterialDefinitionRepresentation materialDefinition) => materialDefinition.Representations)
                                .OfType<Xbim.Ifc2x3.RepresentationResource.IfcStyledRepresentation>()
                                .SelectMany((Xbim.Ifc2x3.RepresentationResource.IfcStyledRepresentation styledRepresentation) => styledRepresentation.Items)
                                .OfType<Xbim.Ifc2x3.PresentationAppearanceResource.IfcStyledItem>()
                                .ToList();

                        List<Xbim.Ifc2x3.PresentationAppearanceResource.IfcSurfaceStyle> materialSurfaceStyles = materialStyledItems
                                .SelectMany((Xbim.Ifc2x3.PresentationAppearanceResource.IfcStyledItem styledItem) => styledItem.Styles)
                                .SelectMany((Xbim.Ifc2x3.PresentationAppearanceResource.IfcPresentationStyleAssignment styleAssignment) => styleAssignment.Styles)
                                .OfType<Xbim.Ifc2x3.PresentationAppearanceResource.IfcSurfaceStyle>()
                                .ToList();

                        // Priority 4: Material diffuse colours

                        List<Xbim.Ifc2x3.PresentationResource.IfcColourRgb> materialDiffuseColours = materialSurfaceStyles
                                .SelectMany((Xbim.Ifc2x3.PresentationAppearanceResource.IfcSurfaceStyle surfaceStyle) => surfaceStyle.Styles)
                                .OfType<Xbim.Ifc2x3.PresentationAppearanceResource.IfcSurfaceStyleRendering>()
                                .Select((Xbim.Ifc2x3.PresentationAppearanceResource.IfcSurfaceStyleRendering rendering) => rendering.DiffuseColour)
                                .OfType<Xbim.Ifc2x3.PresentationResource.IfcColourRgb>()
                                .ToList();

                        // Priority 5: Material surface colours

                        List<Xbim.Ifc2x3.PresentationResource.IfcColourRgb> materialSurfaceColours = materialSurfaceStyles
                                .SelectMany((Xbim.Ifc2x3.PresentationAppearanceResource.IfcSurfaceStyle surfaceStyle) => surfaceStyle.Styles)
                                .OfType<Xbim.Ifc2x3.PresentationAppearanceResource.IfcSurfaceStyleShading>()
                                .Select((Xbim.Ifc2x3.PresentationAppearanceResource.IfcSurfaceStyleShading shading) => shading.SurfaceColour)
                                .Where(colourValue => colourValue != null)
                                .ToList();

                        // -----------------------------------------------------
                        // 3.3 Collect presentation-layer colours
                        // -----------------------------------------------------

                        List<Xbim.Ifc2x3.PresentationOrganizationResource.IfcPresentationLayerWithStyle> presentationLayers = productRepresentations
                                .SelectMany((Xbim.Ifc2x3.RepresentationResource.IfcRepresentation representation) => representation.LayerAssignments)
                                .Concat(productRepresentationItems.SelectMany((Xbim.Ifc2x3.GeometryResource.IfcRepresentationItem representationItem) => representationItem.LayerAssignments))
                                .OfType<Xbim.Ifc2x3.PresentationOrganizationResource.IfcPresentationLayerWithStyle>()
                                .Distinct()
                                .ToList();

                        // Priority 6: Presentation-layer curve colours

                        List<Xbim.Ifc2x3.PresentationResource.IfcColourRgb> presentationLayerColours = presentationLayers
                                .SelectMany((Xbim.Ifc2x3.PresentationOrganizationResource.IfcPresentationLayerWithStyle presentationLayer) => presentationLayer.LayerStyles)
                                .OfType<Xbim.Ifc2x3.PresentationAppearanceResource.IfcCurveStyle>()
                                .Select((Xbim.Ifc2x3.PresentationAppearanceResource.IfcCurveStyle curveStyle) => curveStyle.CurveColour)
                                .OfType<Xbim.Ifc2x3.PresentationResource.IfcColourRgb>()
                                .ToList();

                        // -----------------------------------------------------
                        // 3.4 Combine colours in priority order
                        // -----------------------------------------------------

                        List<Xbim.Ifc2x3.PresentationResource.IfcColourRgb> colours = geometryDiffuseColours
                                .Concat(geometrySurfaceColours)
                                .Concat(geometryCurveColours)
                                .Concat(materialDiffuseColours)
                                .Concat(materialSurfaceColours)
                                .Concat(presentationLayerColours)
                                .Where(colourValue => colourValue != null)
                                .GroupBy(colourValue => new { Red = colourValue.Red.Value, Green = colourValue.Green.Value, Blue = colourValue.Blue.Value })
                                .Select(colourGroup => colourGroup.First())
                                .ToList();

                        if (colours.Count > 1)
                        {
                            //Debug.Print(colours.Count + " colours found for " + modelInstance.EntityLabel + ", materials: " + materialNames);
                        }

                        Xbim.Ifc2x3.PresentationResource.IfcColourRgb colour = colours.FirstOrDefault();

                        // -----------------------------------------------------
                        // 4. Store the highest-priority colour
                        // -----------------------------------------------------

                        if (colour != null)
                        {
                            string colourString = string.Join(",", colour.Red.Value.ToString(), colour.Green.Value.ToString(), colour.Blue.Value.ToString());
                            instanceObject.Properties["Colour"] = colourString;
                        }
                        else
                        {
                            Debug.Print("No colour found for " + modelInstance.EntityLabel + ", materials: " + materialNames);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.Print("Issue finding material for " + modelInstance.EntityLabel + ": " + ex.GetType().Name + ": " + ex.Message);
                }

                foreach (var propSet in modelInstance.PropertySets)
                {
                    foreach (var prop in propSet.HasProperties)
                    {
                        string propName = propSet.Name + "--" + prop.Name;
                        if (prop.GetType() == typeof(Xbim.Ifc2x3.PropertyResource.IfcPropertyListValue))
                        {
                            Xbim.Ifc2x3.PropertyResource.IfcPropertyListValue propLV = prop as Xbim.Ifc2x3.PropertyResource.IfcPropertyListValue;
                            instanceObject.Properties.Add(propName, string.Join(",", propLV.ListValues.Select(v => v.Value.ToString())));
                            continue;
                        }
                        if (prop.GetType() == typeof(Xbim.Ifc2x3.PropertyResource.IfcPropertySingleValue))
                        {
                            Xbim.Ifc2x3.PropertyResource.IfcPropertySingleValue propSV = prop as Xbim.Ifc2x3.PropertyResource.IfcPropertySingleValue;
                            string propVal = propSV.NominalValue != null && propSV.NominalValue.Value != null ? (string)propSV.NominalValue.Value.ToString() : "NULL";
                            propVal = Regex.Replace(propVal, @"\\[A-Za-z0-9]+", "");
                            propVal = propVal.Replace("\r", "").Replace("\n", "");
                            propVal = string.IsNullOrWhiteSpace(propVal) ? "NULL" : propVal;
                            if (!instanceObject.Properties.ContainsKey(propName))
                            {
                                instanceObject.Properties.Add(propName, propVal);
                            }
                            else
                            {
                                if ((string)instanceObject.Properties[propName] != propVal)
                                {
                                    // Basically if the value is different then add it. If its the same then leave it as is.
                                    instanceObject.Properties[propName] += "," + propVal;
                                }
                            }
                            continue;
                        }

                        // TODO: Potentially unsupported IfcProperties...
                        Debug.Print("Unsupported property type: " + prop.GetType().ToString() + " for property: " + propName);
                    }
                }

                objectList.TryAdd(modelInstance.EntityLabel.ToString(), instanceObject);
            });

            return objectList;
        }

        public static ConcurrentDictionary<string, ccBIMObject> GetObjectInModel4(IfcStore model)
        {
            ConcurrentDictionary<string, ccBIMObject> objectList = new ConcurrentDictionary<string, ccBIMObject>();
            var modelInstances = model.Instances.OfType<Xbim.Ifc4.Kernel.IfcProduct>().ToList();
            Parallel.ForEach(modelInstances, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, modelInstance =>
            {
                //modelInstances.ForEach(modelInstance =>            {
                ccBIMObject instanceObject = new ccBIMObject
                {
                    ID = modelInstance.EntityLabel.ToString(),
                    Orientation = ConverterGeneral.VectorConverter(modelInstance.ObjectPlacement.ToMatrix3D().GetRotationQuaternion())
                };
                instanceObject.Properties.Add("ID", modelInstance.GlobalId);
                instanceObject.Properties.Add("EntityLabel", modelInstance.EntityLabel.ToString());
                instanceObject.Properties.Add("Name", modelInstance.Name);
                instanceObject.Properties.Add("IfcType", modelInstance.GetType().ToString());
                instanceObject.Properties.Add("IfcObjectType", modelInstance.ObjectType);
                instanceObject.Properties.Add("Description", modelInstance.Description);
                instanceObject.Properties.Add("BuildingName", Path.GetFileNameWithoutExtension(model.FileName));

                try
                {
                    System.Collections.Generic.List<Xbim.Ifc4.MaterialResource.IfcMaterial> materials = new System.Collections.Generic.List<Xbim.Ifc4.MaterialResource.IfcMaterial>();

                    // ---------------------------------------------------------
                    // 1. Determine the material-assignment type
                    // ---------------------------------------------------------

                    if (modelInstance.Material is Xbim.Ifc4.MaterialResource.IfcMaterial singleMaterial)
                    {
                        materials.Add(singleMaterial);
                    }
                    else if (modelInstance.Material is Xbim.Ifc4.MaterialResource.IfcMaterialConstituentSet constituentSet)
                    {
                        materials.AddRange(constituentSet.MaterialConstituents.Where(constituent => constituent.Material != null).Select(constituent => constituent.Material));
                    }
                    else if (modelInstance.Material is Xbim.Ifc4.MaterialResource.IfcMaterialLayerSet layerSet)
                    {
                        materials.AddRange(layerSet.MaterialLayers.Where(layer => layer.Material != null).Select(layer => layer.Material));
                    }
                    else if (modelInstance.Material is Xbim.Ifc4.MaterialResource.IfcMaterialLayerSetUsage layerSetUsage)
                    {
                        materials.AddRange(layerSetUsage.ForLayerSet.MaterialLayers.Where(layer => layer.Material != null).Select(layer => layer.Material));
                    }
                    else
                    {
                        string actualType = modelInstance.Material == null ? "null" : modelInstance.Material.GetType().FullName;
                        if (modelInstance.Material != null)
                        {
                            Debug.Print("Unsupported material type for " + modelInstance.EntityLabel + ": " + actualType);
                        }
                    }

                    // ---------------------------------------------------------
                    // 2. Store the material names
                    // ---------------------------------------------------------

                    if (materials.Count > 0)
                    {
                        string materialNames = string.Join(";", materials.Select(material => material.Name.ToString()).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct());

                        if (!string.IsNullOrWhiteSpace(materialNames))
                        {
                            instanceObject.Properties["InstanceMaterial"] = materialNames;
                        }
                        // -----------------------------------------------------
                        // 3. Find all available colours in priority order
                        // -----------------------------------------------------

                        List<Xbim.Ifc4.RepresentationResource.IfcRepresentation> productRepresentations = new List<Xbim.Ifc4.RepresentationResource.IfcRepresentation>();
                        List<Xbim.Ifc4.GeometryResource.IfcRepresentationItem> productRepresentationItems = new List<Xbim.Ifc4.GeometryResource.IfcRepresentationItem>();
                        HashSet<int> visitedRepresentations = new HashSet<int>();

                        void CollectRepresentationItems(Xbim.Ifc4.RepresentationResource.IfcRepresentation representation)
                        {
                            if (representation == null || !visitedRepresentations.Add(representation.EntityLabel))
                            {
                                return;
                            }

                            productRepresentations.Add(representation);

                            foreach (Xbim.Ifc4.GeometryResource.IfcRepresentationItem representationItem in representation.Items)
                            {
                                productRepresentationItems.Add(representationItem);

                                if (representationItem is Xbim.Ifc4.GeometryResource.IfcMappedItem mappedItem && mappedItem.MappingSource != null && mappedItem.MappingSource.MappedRepresentation != null)
                                {
                                    CollectRepresentationItems(mappedItem.MappingSource.MappedRepresentation);
                                }
                            }
                        }

                        // -----------------------------------------------------
                        // 3.1 Collect product geometry
                        // -----------------------------------------------------

                        if (modelInstance is Xbim.Ifc4.Kernel.IfcProduct product && product.Representation != null)
                        {
                            foreach (Xbim.Ifc4.RepresentationResource.IfcRepresentation representation in product.Representation.Representations)
                            {
                                CollectRepresentationItems(representation);
                            }
                        }

                        List<Xbim.Ifc4.PresentationAppearanceResource.IfcStyledItem> geometryStyledItems = productRepresentationItems
                                .SelectMany((Xbim.Ifc4.GeometryResource.IfcRepresentationItem representationItem) => representationItem.StyledByItem)
                                .Concat(productRepresentationItems.OfType<Xbim.Ifc4.PresentationAppearanceResource.IfcStyledItem>())
                                .Distinct()
                                .ToList();

                        // IFC4 normally assigns styles directly, but this also handles the deprecated IfcPresentationStyleAssignment form.

                        List<Xbim.Ifc4.PresentationAppearanceResource.IfcSurfaceStyle> geometrySurfaceStyles = geometryStyledItems
                                .SelectMany((Xbim.Ifc4.PresentationAppearanceResource.IfcStyledItem styledItem) => styledItem.Styles)
                                .OfType<Xbim.Ifc4.PresentationAppearanceResource.IfcSurfaceStyle>()
                                .Concat(geometryStyledItems
                                        .SelectMany((Xbim.Ifc4.PresentationAppearanceResource.IfcStyledItem styledItem) => styledItem.Styles)
                                        .OfType<Xbim.Ifc4.PresentationAppearanceResource.IfcPresentationStyleAssignment>()
                                        .SelectMany((Xbim.Ifc4.PresentationAppearanceResource.IfcPresentationStyleAssignment styleAssignment) => styleAssignment.Styles)
                                        .OfType<Xbim.Ifc4.PresentationAppearanceResource.IfcSurfaceStyle>())
                                .Distinct()
                                .ToList();

                        List<Xbim.Ifc4.PresentationAppearanceResource.IfcCurveStyle> geometryCurveStyles = geometryStyledItems
                                .SelectMany((Xbim.Ifc4.PresentationAppearanceResource.IfcStyledItem styledItem) => styledItem.Styles)
                                .OfType<Xbim.Ifc4.PresentationAppearanceResource.IfcCurveStyle>()
                                .Concat(geometryStyledItems
                                        .SelectMany((Xbim.Ifc4.PresentationAppearanceResource.IfcStyledItem styledItem) => styledItem.Styles)
                                        .OfType<Xbim.Ifc4.PresentationAppearanceResource.IfcPresentationStyleAssignment>()
                                        .SelectMany((Xbim.Ifc4.PresentationAppearanceResource.IfcPresentationStyleAssignment styleAssignment) => styleAssignment.Styles)
                                        .OfType<Xbim.Ifc4.PresentationAppearanceResource.IfcCurveStyle>())
                                .Distinct()
                                .ToList();

                        // Priority 1: Geometry diffuse colours

                        List<Xbim.Ifc4.PresentationAppearanceResource.IfcColourRgb> geometryDiffuseColours = geometrySurfaceStyles
                                .SelectMany((Xbim.Ifc4.PresentationAppearanceResource.IfcSurfaceStyle surfaceStyle) => surfaceStyle.Styles)
                                .OfType<Xbim.Ifc4.PresentationAppearanceResource.IfcSurfaceStyleRendering>()
                                .Select((Xbim.Ifc4.PresentationAppearanceResource.IfcSurfaceStyleRendering rendering) => rendering.DiffuseColour)
                                .OfType<Xbim.Ifc4.PresentationAppearanceResource.IfcColourRgb>()
                                .ToList();

                        // Priority 2: Geometry surface colours

                        List<Xbim.Ifc4.PresentationAppearanceResource.IfcColourRgb> geometrySurfaceColours = geometrySurfaceStyles
                                .SelectMany((Xbim.Ifc4.PresentationAppearanceResource.IfcSurfaceStyle surfaceStyle) => surfaceStyle.Styles)
                                .OfType<Xbim.Ifc4.PresentationAppearanceResource.IfcSurfaceStyleShading>()
                                .Select((Xbim.Ifc4.PresentationAppearanceResource.IfcSurfaceStyleShading shading) => shading.SurfaceColour)
                                .Where(colourValue => colourValue != null)
                                .ToList();

                        // Priority 3: Geometry curve colours

                        List<Xbim.Ifc4.PresentationAppearanceResource.IfcColourRgb> geometryCurveColours = geometryCurveStyles
                                .Select((Xbim.Ifc4.PresentationAppearanceResource.IfcCurveStyle curveStyle) => curveStyle.CurveColour)
                                .OfType<Xbim.Ifc4.PresentationAppearanceResource.IfcColourRgb>()
                                .ToList();

                        // -----------------------------------------------------
                        // 3.2 Collect material colours
                        // -----------------------------------------------------

                        List<Xbim.Ifc4.PresentationAppearanceResource.IfcStyledItem> materialStyledItems = materials
                                .Where(material => material != null)
                                .SelectMany(material => material.HasRepresentation)
                                .SelectMany((Xbim.Ifc4.RepresentationResource.IfcMaterialDefinitionRepresentation materialDefinition) => materialDefinition.Representations)
                                .OfType<Xbim.Ifc4.RepresentationResource.IfcStyledRepresentation>()
                                .SelectMany((Xbim.Ifc4.RepresentationResource.IfcStyledRepresentation styledRepresentation) => styledRepresentation.Items)
                                .OfType<Xbim.Ifc4.PresentationAppearanceResource.IfcStyledItem>()
                                .ToList();

                        List<Xbim.Ifc4.PresentationAppearanceResource.IfcSurfaceStyle> materialSurfaceStyles = materialStyledItems
                                .SelectMany((Xbim.Ifc4.PresentationAppearanceResource.IfcStyledItem styledItem) => styledItem.Styles)
                                .OfType<Xbim.Ifc4.PresentationAppearanceResource.IfcSurfaceStyle>()
                                .Concat(materialStyledItems
                                        .SelectMany((Xbim.Ifc4.PresentationAppearanceResource.IfcStyledItem styledItem) => styledItem.Styles)
                                        .OfType<Xbim.Ifc4.PresentationAppearanceResource.IfcPresentationStyleAssignment>()
                                        .SelectMany((Xbim.Ifc4.PresentationAppearanceResource.IfcPresentationStyleAssignment styleAssignment) => styleAssignment.Styles)
                                        .OfType<Xbim.Ifc4.PresentationAppearanceResource.IfcSurfaceStyle>())
                                .Distinct()
                                .ToList();

                        // Priority 4: Material diffuse colours

                        List<Xbim.Ifc4.PresentationAppearanceResource.IfcColourRgb> materialDiffuseColours = materialSurfaceStyles
                                .SelectMany((Xbim.Ifc4.PresentationAppearanceResource.IfcSurfaceStyle surfaceStyle) => surfaceStyle.Styles)
                                .OfType<Xbim.Ifc4.PresentationAppearanceResource.IfcSurfaceStyleRendering>()
                                .Select((Xbim.Ifc4.PresentationAppearanceResource.IfcSurfaceStyleRendering rendering) => rendering.DiffuseColour)
                                .OfType<Xbim.Ifc4.PresentationAppearanceResource.IfcColourRgb>()
                                .ToList();

                        // Priority 5: Material surface colours

                        List<Xbim.Ifc4.PresentationAppearanceResource.IfcColourRgb> materialSurfaceColours = materialSurfaceStyles
                                .SelectMany((Xbim.Ifc4.PresentationAppearanceResource.IfcSurfaceStyle surfaceStyle) => surfaceStyle.Styles)
                                .OfType<Xbim.Ifc4.PresentationAppearanceResource.IfcSurfaceStyleShading>()
                                .Select((Xbim.Ifc4.PresentationAppearanceResource.IfcSurfaceStyleShading shading) => shading.SurfaceColour)
                                .Where(colourValue => colourValue != null)
                                .ToList();

                        // -----------------------------------------------------
                        // 3.3 Collect presentation-layer colours
                        // -----------------------------------------------------

                        List<Xbim.Ifc4.PresentationOrganizationResource.IfcPresentationLayerWithStyle> presentationLayers = productRepresentations
                                .SelectMany((Xbim.Ifc4.RepresentationResource.IfcRepresentation representation) => representation.LayerAssignments)
                                .Concat(productRepresentationItems.SelectMany((Xbim.Ifc4.GeometryResource.IfcRepresentationItem representationItem) => representationItem.LayerAssignment))
                                .OfType<Xbim.Ifc4.PresentationOrganizationResource.IfcPresentationLayerWithStyle>()
                                .Distinct()
                                .ToList();

                        // Priority 6: Presentation-layer curve colours

                        List<Xbim.Ifc4.PresentationAppearanceResource.IfcColourRgb> presentationLayerColours = presentationLayers
                                .SelectMany((Xbim.Ifc4.PresentationOrganizationResource.IfcPresentationLayerWithStyle presentationLayer) => presentationLayer.LayerStyles)
                                .OfType<Xbim.Ifc4.PresentationAppearanceResource.IfcCurveStyle>()
                                .Select((Xbim.Ifc4.PresentationAppearanceResource.IfcCurveStyle curveStyle) => curveStyle.CurveColour)
                                .OfType<Xbim.Ifc4.PresentationAppearanceResource.IfcColourRgb>()
                                .ToList();

                        // -----------------------------------------------------
                        // 3.4 Combine colours in priority order
                        // -----------------------------------------------------

                        List<Xbim.Ifc4.PresentationAppearanceResource.IfcColourRgb> colours = geometryDiffuseColours
                                .Concat(geometrySurfaceColours)
                                .Concat(geometryCurveColours)
                                .Concat(materialDiffuseColours)
                                .Concat(materialSurfaceColours)
                                .Concat(presentationLayerColours)
                                .Where(colourValue => colourValue != null)
                                .GroupBy(colourValue => new { Red = colourValue.Red.Value, Green = colourValue.Green.Value, Blue = colourValue.Blue.Value })
                                .Select(colourGroup => colourGroup.First())
                                .ToList();

                        if (colours.Count > 1)
                        {
                            //Debug.Print(colours.Count + " colours found for " + modelInstance.EntityLabel + ", materials: " + materialNames);
                        }

                        Xbim.Ifc4.PresentationAppearanceResource.IfcColourRgb colour = colours.FirstOrDefault();

                        // -----------------------------------------------------
                        // 4. Store the highest-priority colour
                        // -----------------------------------------------------

                        if (colour != null)
                        {
                            string colourString = string.Join(",", colour.Red.Value.ToString(), colour.Green.Value.ToString(), colour.Blue.Value.ToString());
                            instanceObject.Properties["Colour"] = colourString;
                        }
                        else
                        {
                            Debug.Print("No colour found for " + modelInstance.EntityLabel + ", materials: " + materialNames);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.Print("Issue finding material for " + modelInstance.EntityLabel + ": " + ex.GetType().Name + ": " + ex.Message);
                }

                foreach (var propSet in modelInstance.PropertySets)
                {
                    foreach (var prop in propSet.HasProperties)
                    {
                        string propName = propSet.Name + "--" + prop.Name;
                        if (prop.GetType() == typeof(Xbim.Ifc4.PropertyResource.IfcPropertyListValue))
                        {
                            Xbim.Ifc4.PropertyResource.IfcPropertyListValue propLV = prop as Xbim.Ifc4.PropertyResource.IfcPropertyListValue;
                            instanceObject.Properties.Add(propName, string.Join(",", propLV.ListValues.Select(v => v.Value.ToString())));
                            continue;
                        }
                        if (prop.GetType() == typeof(Xbim.Ifc4.PropertyResource.IfcPropertySingleValue))
                        {
                            Xbim.Ifc4.PropertyResource.IfcPropertySingleValue propSV = prop as Xbim.Ifc4.PropertyResource.IfcPropertySingleValue;
                            string propVal = propSV.NominalValue != null && propSV.NominalValue.Value != null ? (string)propSV.NominalValue.Value.ToString() : "NULL";
                            propVal = Regex.Replace(propVal, @"\\[A-Za-z0-9]+", "");
                            propVal = propVal.Replace("\r", "").Replace("\n", "");
                            propVal = string.IsNullOrWhiteSpace(propVal) ? "NULL" : propVal;
                            if (!instanceObject.Properties.ContainsKey(propName))
                            {
                                instanceObject.Properties.Add(propName, propVal);
                            }
                            else
                            {
                                if ((string)instanceObject.Properties[propName] != propVal)
                                {
                                    // Basically if the value is different then add it. If its the same then leave it as is.
                                    instanceObject.Properties[propName] += "," + propVal;
                                }
                            }
                            continue;
                        }

                        // TODO: Potentially unsupported IfcProperties...
                        Debug.Print("Unsupported property type: " + prop.GetType().ToString() + " for property: " + propName);
                    }
                }

                objectList.TryAdd(modelInstance.EntityLabel.ToString(), instanceObject);
            });

            return objectList;
        }

        public static ConcurrentDictionary<string, ccBIMObject> GetObjectInModel4x3(IfcStore model)
        {
            ConcurrentDictionary<string, ccBIMObject> objectList = new ConcurrentDictionary<string, ccBIMObject>();
            var modelInstances = model.Instances.OfType<Xbim.Ifc4x3.Kernel.IfcProduct>().ToList();
            Parallel.ForEach(modelInstances, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, modelInstance =>
            {
                //modelInstances.ForEach(modelInstance =>            {
                ccBIMObject instanceObject = new ccBIMObject
                {
                    ID = modelInstance.EntityLabel.ToString(),
                    Orientation = ConverterGeneral.VectorConverter(modelInstance.ObjectPlacement.ToMatrix3D().GetRotationQuaternion())
                };
                instanceObject.Properties.Add("ID", modelInstance.GlobalId);
                instanceObject.Properties.Add("EntityLabel", modelInstance.EntityLabel.ToString());
                instanceObject.Properties.Add("Name", modelInstance.Name);
                instanceObject.Properties.Add("IfcType", modelInstance.GetType().ToString());
                instanceObject.Properties.Add("IfcObjectType", modelInstance.ObjectType);
                instanceObject.Properties.Add("Description", modelInstance.Description);
                instanceObject.Properties.Add("BuildingName", Path.GetFileNameWithoutExtension(model.FileName));

                try
                {
                    System.Collections.Generic.List<Xbim.Ifc4x3.MaterialResource.IfcMaterial> materials = new System.Collections.Generic.List<Xbim.Ifc4x3.MaterialResource.IfcMaterial>();

                    // ---------------------------------------------------------
                    // 1. Determine the material-assignment type
                    // ---------------------------------------------------------

                    if (modelInstance.Material is Xbim.Ifc4x3.MaterialResource.IfcMaterial singleMaterial)
                    {
                        materials.Add(singleMaterial);
                    }
                    else if (modelInstance.Material is Xbim.Ifc4x3.MaterialResource.IfcMaterialConstituentSet constituentSet)
                    {
                        materials.AddRange(constituentSet.MaterialConstituents.Where(constituent => constituent.Material != null).Select(constituent => constituent.Material));
                    }
                    else if (modelInstance.Material is Xbim.Ifc4x3.MaterialResource.IfcMaterialLayerSet layerSet)
                    {
                        materials.AddRange(layerSet.MaterialLayers.Where(layer => layer.Material != null).Select(layer => layer.Material));
                    }
                    else if (modelInstance.Material is Xbim.Ifc4x3.MaterialResource.IfcMaterialLayerSetUsage layerSetUsage)
                    {
                        materials.AddRange(layerSetUsage.ForLayerSet.MaterialLayers.Where(layer => layer.Material != null).Select(layer => layer.Material));
                    }
                    else
                    {
                        string actualType = modelInstance.Material == null ? "null" : modelInstance.Material.GetType().FullName;
                        if (modelInstance.Material != null)
                        {
                            Debug.Print("Unsupported material type for " + modelInstance.EntityLabel + ": " + actualType);
                        }
                    }

                    // ---------------------------------------------------------
                    // 2. Store the material names
                    // ---------------------------------------------------------

                    if (materials.Count > 0)
                    {
                        string materialNames = string.Join(";", materials.Select(material => material.Name.ToString()).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct());

                        if (!string.IsNullOrWhiteSpace(materialNames))
                        {
                            instanceObject.Properties["InstanceMaterial"] = materialNames;
                        }

                        // -----------------------------------------------------
                        // 3. Find all available colours in priority order
                        // -----------------------------------------------------

                        List<Xbim.Ifc4x3.RepresentationResource.IfcRepresentation> productRepresentations = new List<Xbim.Ifc4x3.RepresentationResource.IfcRepresentation>();
                        List<Xbim.Ifc4x3.GeometryResource.IfcRepresentationItem> productRepresentationItems = new List<Xbim.Ifc4x3.GeometryResource.IfcRepresentationItem>();
                        HashSet<int> visitedRepresentations = new HashSet<int>();

                        void CollectRepresentationItems(Xbim.Ifc4x3.RepresentationResource.IfcRepresentation representation)
                        {
                            if (representation == null || !visitedRepresentations.Add(representation.EntityLabel))
                            {
                                return;
                            }

                            productRepresentations.Add(representation);

                            foreach (Xbim.Ifc4x3.GeometryResource.IfcRepresentationItem representationItem in representation.Items)
                            {
                                productRepresentationItems.Add(representationItem);

                                if (representationItem is Xbim.Ifc4x3.GeometryResource.IfcMappedItem mappedItem && mappedItem.MappingSource != null && mappedItem.MappingSource.MappedRepresentation != null)
                                {
                                    CollectRepresentationItems(mappedItem.MappingSource.MappedRepresentation);
                                }
                            }
                        }

                        // -----------------------------------------------------
                        // 3.1 Collect product geometry
                        // -----------------------------------------------------

                        if (modelInstance is Xbim.Ifc4x3.Kernel.IfcProduct product && product.Representation != null)
                        {
                            foreach (Xbim.Ifc4x3.RepresentationResource.IfcRepresentation representation in product.Representation.Representations)
                            {
                                CollectRepresentationItems(representation);
                            }
                        }

                        List<Xbim.Ifc4x3.PresentationAppearanceResource.IfcStyledItem> geometryStyledItems = productRepresentationItems
                                .SelectMany((Xbim.Ifc4x3.GeometryResource.IfcRepresentationItem representationItem) => representationItem.StyledByItem)
                                .Concat(productRepresentationItems.OfType<Xbim.Ifc4x3.PresentationAppearanceResource.IfcStyledItem>())
                                .Distinct()
                                .ToList();

                        List<Xbim.Ifc4x3.PresentationAppearanceResource.IfcSurfaceStyle> geometrySurfaceStyles = geometryStyledItems
                                .SelectMany((Xbim.Ifc4x3.PresentationAppearanceResource.IfcStyledItem styledItem) => styledItem.Styles)
                                .OfType<Xbim.Ifc4x3.PresentationAppearanceResource.IfcSurfaceStyle>()
                                .Distinct()
                                .ToList();

                        List<Xbim.Ifc4x3.PresentationAppearanceResource.IfcCurveStyle> geometryCurveStyles = geometryStyledItems
                                .SelectMany((Xbim.Ifc4x3.PresentationAppearanceResource.IfcStyledItem styledItem) => styledItem.Styles)
                                .OfType<Xbim.Ifc4x3.PresentationAppearanceResource.IfcCurveStyle>()
                                .Distinct()
                                .ToList();

                        // Priority 1: Geometry diffuse colours

                        List<Xbim.Ifc4x3.PresentationAppearanceResource.IfcColourRgb> geometryDiffuseColours = geometrySurfaceStyles
                                .SelectMany((Xbim.Ifc4x3.PresentationAppearanceResource.IfcSurfaceStyle surfaceStyle) => surfaceStyle.Styles)
                                .OfType<Xbim.Ifc4x3.PresentationAppearanceResource.IfcSurfaceStyleRendering>()
                                .Select((Xbim.Ifc4x3.PresentationAppearanceResource.IfcSurfaceStyleRendering rendering) => rendering.DiffuseColour)
                                .OfType<Xbim.Ifc4x3.PresentationAppearanceResource.IfcColourRgb>()
                                .ToList();

                        // Priority 2: Geometry surface colours

                        List<Xbim.Ifc4x3.PresentationAppearanceResource.IfcColourRgb> geometrySurfaceColours = geometrySurfaceStyles
                                .SelectMany((Xbim.Ifc4x3.PresentationAppearanceResource.IfcSurfaceStyle surfaceStyle) => surfaceStyle.Styles)
                                .OfType<Xbim.Ifc4x3.PresentationAppearanceResource.IfcSurfaceStyleShading>()
                                .Select((Xbim.Ifc4x3.PresentationAppearanceResource.IfcSurfaceStyleShading shading) => shading.SurfaceColour)
                                .Where(colourValue => colourValue != null)
                                .ToList();

                        // Priority 3: Geometry curve colours

                        List<Xbim.Ifc4x3.PresentationAppearanceResource.IfcColourRgb> geometryCurveColours = geometryCurveStyles
                                .Select((Xbim.Ifc4x3.PresentationAppearanceResource.IfcCurveStyle curveStyle) => curveStyle.CurveColour)
                                .OfType<Xbim.Ifc4x3.PresentationAppearanceResource.IfcColourRgb>()
                                .ToList();

                        // -----------------------------------------------------
                        // 3.2 Collect material colours
                        // -----------------------------------------------------

                        List<Xbim.Ifc4x3.PresentationAppearanceResource.IfcStyledItem> materialStyledItems = materials
                                .Where(material => material != null)
                                .SelectMany(material => material.HasRepresentation)
                                .SelectMany((Xbim.Ifc4x3.RepresentationResource.IfcMaterialDefinitionRepresentation materialDefinition) => materialDefinition.Representations)
                                .OfType<Xbim.Ifc4x3.RepresentationResource.IfcStyledRepresentation>()
                                .SelectMany((Xbim.Ifc4x3.RepresentationResource.IfcStyledRepresentation styledRepresentation) => styledRepresentation.Items)
                                .OfType<Xbim.Ifc4x3.PresentationAppearanceResource.IfcStyledItem>()
                                .ToList();

                        List<Xbim.Ifc4x3.PresentationAppearanceResource.IfcSurfaceStyle> materialSurfaceStyles = materialStyledItems
                                .SelectMany((Xbim.Ifc4x3.PresentationAppearanceResource.IfcStyledItem styledItem) => styledItem.Styles)
                                .OfType<Xbim.Ifc4x3.PresentationAppearanceResource.IfcSurfaceStyle>()
                                .Distinct()
                                .ToList();

                        // Priority 4: Material diffuse colours

                        List<Xbim.Ifc4x3.PresentationAppearanceResource.IfcColourRgb> materialDiffuseColours = materialSurfaceStyles
                                .SelectMany((Xbim.Ifc4x3.PresentationAppearanceResource.IfcSurfaceStyle surfaceStyle) => surfaceStyle.Styles)
                                .OfType<Xbim.Ifc4x3.PresentationAppearanceResource.IfcSurfaceStyleRendering>()
                                .Select((Xbim.Ifc4x3.PresentationAppearanceResource.IfcSurfaceStyleRendering rendering) => rendering.DiffuseColour)
                                .OfType<Xbim.Ifc4x3.PresentationAppearanceResource.IfcColourRgb>()
                                .ToList();

                        // Priority 5: Material surface colours

                        List<Xbim.Ifc4x3.PresentationAppearanceResource.IfcColourRgb> materialSurfaceColours = materialSurfaceStyles
                                .SelectMany((Xbim.Ifc4x3.PresentationAppearanceResource.IfcSurfaceStyle surfaceStyle) => surfaceStyle.Styles)
                                .OfType<Xbim.Ifc4x3.PresentationAppearanceResource.IfcSurfaceStyleShading>()
                                .Select((Xbim.Ifc4x3.PresentationAppearanceResource.IfcSurfaceStyleShading shading) => shading.SurfaceColour)
                                .Where(colourValue => colourValue != null)
                                .ToList();

                        // -----------------------------------------------------
                        // 3.3 Collect presentation-layer colours
                        // -----------------------------------------------------

                        List<Xbim.Ifc4x3.PresentationOrganizationResource.IfcPresentationLayerWithStyle> presentationLayers = productRepresentations
                                .SelectMany((Xbim.Ifc4x3.RepresentationResource.IfcRepresentation representation) => representation.LayerAssignments)
                                .Concat(productRepresentationItems.SelectMany((Xbim.Ifc4x3.GeometryResource.IfcRepresentationItem representationItem) => representationItem.LayerAssignment))
                                .OfType<Xbim.Ifc4x3.PresentationOrganizationResource.IfcPresentationLayerWithStyle>()
                                .Distinct()
                                .ToList();

                        // Priority 6: Presentation-layer curve colours

                        List<Xbim.Ifc4x3.PresentationAppearanceResource.IfcColourRgb> presentationLayerColours = presentationLayers
                                .SelectMany((Xbim.Ifc4x3.PresentationOrganizationResource.IfcPresentationLayerWithStyle presentationLayer) => presentationLayer.LayerStyles)
                                .OfType<Xbim.Ifc4x3.PresentationAppearanceResource.IfcCurveStyle>()
                                .Select((Xbim.Ifc4x3.PresentationAppearanceResource.IfcCurveStyle curveStyle) => curveStyle.CurveColour)
                                .OfType<Xbim.Ifc4x3.PresentationAppearanceResource.IfcColourRgb>()
                                .ToList();

                        // -----------------------------------------------------
                        // 3.4 Combine colours in priority order
                        // -----------------------------------------------------

                        List<Xbim.Ifc4x3.PresentationAppearanceResource.IfcColourRgb> colours = geometryDiffuseColours
                                .Concat(geometrySurfaceColours)
                                .Concat(geometryCurveColours)
                                .Concat(materialDiffuseColours)
                                .Concat(materialSurfaceColours)
                                .Concat(presentationLayerColours)
                                .Where(colourValue => colourValue != null)
                                .GroupBy(colourValue => new { Red = colourValue.Red.Value, Green = colourValue.Green.Value, Blue = colourValue.Blue.Value })
                                .Select(colourGroup => colourGroup.First())
                                .ToList();

                        if (colours.Count > 1)
                        {
                            //Debug.Print(colours.Count + " colours found for " + modelInstance.EntityLabel + ", materials: " + materialNames);
                        }

                        Xbim.Ifc4x3.PresentationAppearanceResource.IfcColourRgb colour = colours.FirstOrDefault();

                        // -----------------------------------------------------
                        // 4. Store the highest-priority colour
                        // -----------------------------------------------------

                        if (colour != null)
                        {
                            string colourString = string.Join(",", colour.Red.Value.ToString(), colour.Green.Value.ToString(), colour.Blue.Value.ToString());
                            instanceObject.Properties["Colour"] = colourString;
                        }
                        else
                        {
                            Debug.Print("No colour found for " + modelInstance.EntityLabel + ", materials: " + materialNames);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.Print("Issue finding material for " + modelInstance.EntityLabel + ": " + ex.GetType().Name + ": " + ex.Message);
                }

                foreach (var propSet in modelInstance.PropertySets)
                {
                    foreach (var prop in propSet.HasProperties)
                    {
                        string propName = propSet.Name + "--" + prop.Name;
                        if (prop.GetType() == typeof(Xbim.Ifc4x3.PropertyResource.IfcPropertyListValue))
                        {
                            Xbim.Ifc4x3.PropertyResource.IfcPropertyListValue propLV = prop as Xbim.Ifc4x3.PropertyResource.IfcPropertyListValue;
                            instanceObject.Properties.Add(propName, string.Join(",", propLV.ListValues.Select(v => v.Value.ToString())));
                            continue;
                        }
                        if (prop.GetType() == typeof(Xbim.Ifc4x3.PropertyResource.IfcPropertySingleValue))
                        {
                            Xbim.Ifc4x3.PropertyResource.IfcPropertySingleValue propSV = prop as Xbim.Ifc4x3.PropertyResource.IfcPropertySingleValue;
                            string propVal = propSV.NominalValue != null && propSV.NominalValue.Value != null ? (string)propSV.NominalValue.Value.ToString() : "NULL";
                            propVal = Regex.Replace(propVal, @"\\[A-Za-z0-9]+", "");
                            propVal = propVal.Replace("\r", "").Replace("\n", "");
                            propVal = string.IsNullOrWhiteSpace(propVal) ? "NULL" : propVal;
                            if (!instanceObject.Properties.ContainsKey(propName))
                            {
                                instanceObject.Properties.Add(propName, propVal);
                            }
                            else
                            {
                                if ((string)instanceObject.Properties[propName] != propVal)
                                {
                                    // Basically if the value is different then add it. If its the same then leave it as is.
                                    instanceObject.Properties[propName] += "," + propVal;
                                }
                            }
                            continue;
                        }

                        // TODO: Potentially unsupported IfcProperties...
                        Debug.Print("Unsupported property type: " + prop.GetType().ToString() + " for property: " + propName);
                    }
                }

                objectList.TryAdd(modelInstance.EntityLabel.ToString(), instanceObject);
            });

            return objectList;
        }

        public static ConcurrentBag<BIMRelation3> GetRelationInModel2x3(IfcStore model, out Dictionary<string, List<string>> materialsDict)
        {
            ConcurrentBag<BIMRelation3> relList = new ConcurrentBag<BIMRelation3>();
            materialsDict = new Dictionary<string, List<string>>();
            var modelRels = model.Instances.OfType<Xbim.Ifc2x3.Kernel.IfcRelationship>().ToList();
            foreach (var rel in modelRels)
            {
                if (rel is Xbim.Ifc2x3.Kernel.IfcRelAggregates)
                {
                    Xbim.Ifc2x3.Kernel.IfcRelAggregates relAgg = (Xbim.Ifc2x3.Kernel.IfcRelAggregates)rel;
                    var superElem = relAgg.RelatingObject;
                    foreach (var elem in relAgg.RelatedObjects)
                    {
                        BIMRelation3 newRel = new BIMRelation3() { ID1 = elem.EntityLabel.ToString(), ID2 = superElem.EntityLabel.ToString(), Relationship = "IfcRelAggregates" };
                        relList.Add(newRel);
                    }
                    continue;
                }

                if (rel is Xbim.Ifc2x3.ProductExtension.IfcRelContainedInSpatialStructure)
                {
                    Xbim.Ifc2x3.ProductExtension.IfcRelContainedInSpatialStructure relAgg = (Xbim.Ifc2x3.ProductExtension.IfcRelContainedInSpatialStructure)rel;
                    var superElem = relAgg.RelatingStructure;
                    foreach (var elem in relAgg.RelatedElements)
                    {
                        BIMRelation3 newRel = new BIMRelation3() { ID1 = elem.EntityLabel.ToString(), ID2 = superElem.EntityLabel.ToString(), Relationship = "IfcRelContainedInSpatialStructure" };
                        relList.Add(newRel);
                    }
                    continue;
                }

                if (rel is Xbim.Ifc2x3.ProductExtension.IfcRelAssociatesMaterial)
                {
                    Xbim.Ifc2x3.ProductExtension.IfcRelAssociatesMaterial matRel = (Xbim.Ifc2x3.ProductExtension.IfcRelAssociatesMaterial)rel;
                    if (matRel.RelatingMaterial is Xbim.Ifc2x3.MaterialResource.IfcMaterialList)
                    {
                        List<string> materials = ((Xbim.Ifc2x3.MaterialResource.IfcMaterialList)matRel.RelatingMaterial).Materials.Select(m => m.Name.Value.ToString()).ToList();
                        foreach (var elem in matRel.RelatedObjects)
                        {
                            foreach (string matName in materials)
                            {
                                if (!materialsDict.ContainsKey(elem.EntityLabel.ToString()))
                                {
                                    materialsDict[elem.EntityLabel.ToString()] = new List<string>();
                                }
                                materialsDict[elem.EntityLabel.ToString()].Add(matName);
                            }
                        }
                        continue;
                    }
                    if (matRel.RelatingMaterial is Xbim.Ifc2x3.MaterialResource.IfcMaterialLayerSetUsage)
                    {
                        List<string> materials = ((Xbim.Ifc2x3.MaterialResource.IfcMaterialLayerSetUsage)matRel.RelatingMaterial).ForLayerSet.MaterialLayers.Select(m => m.Material.Name.Value.ToString()).ToList();
                        foreach (var elem in matRel.RelatedObjects)
                        {
                            foreach (string matName in materials)
                            {
                                if (!materialsDict.ContainsKey(elem.EntityLabel.ToString()))
                                {
                                    materialsDict[elem.EntityLabel.ToString()] = new List<string>();
                                }
                                materialsDict[elem.EntityLabel.ToString()].Add(matName);
                            }
                        }
                        continue;
                    }
                    if (matRel.RelatingMaterial is Xbim.Ifc2x3.MaterialResource.IfcMaterialLayerSet)
                    {
                        List<string> materials = ((Xbim.Ifc2x3.MaterialResource.IfcMaterialLayerSet)matRel.RelatingMaterial).MaterialLayers.Select(m => m.Material.Name.Value.ToString()).ToList();
                        foreach (var elem in matRel.RelatedObjects)
                        {
                            foreach (string matName in materials)
                            {
                                if (!materialsDict.ContainsKey(elem.EntityLabel.ToString()))
                                {
                                    materialsDict[elem.EntityLabel.ToString()] = new List<string>();
                                }
                                materialsDict[elem.EntityLabel.ToString()].Add(matName);
                            }
                        }
                        continue;
                    }
                    if (matRel.RelatingMaterial is Xbim.Ifc2x3.MaterialResource.IfcMaterial)
                    {
                        string matName = ((Xbim.Ifc2x3.MaterialResource.IfcMaterial)matRel.RelatingMaterial).Name.Value.ToString();
                        foreach (var elem in matRel.RelatedObjects)
                        {
                            if (!materialsDict.ContainsKey(elem.EntityLabel.ToString()))
                            {
                                materialsDict[elem.EntityLabel.ToString()] = new List<string>();
                            }
                            materialsDict[elem.EntityLabel.ToString()].Add(matName);
                        }
                        continue;
                    }
                }

                // TODO: Still lots of unsupported IfcRels...
                //Debug.Print("Unsupported relationship type: " + rel.GetType().ToString() + " for relationship: " + rel.EntityLabel.ToString());
            }

            return relList;
        }

        public static ConcurrentBag<BIMRelation3> GetRelationInModel4(IfcStore model, out Dictionary<string, List<string>> materialsDict)
        {
            ConcurrentBag<BIMRelation3> relList = new ConcurrentBag<BIMRelation3>();
            materialsDict = new Dictionary<string, List<string>>();
            var modelRels = model.Instances.OfType<Xbim.Ifc4.Kernel.IfcRelationship>().ToList();
            foreach (var rel in modelRels)
            {
                if (rel is Xbim.Ifc4.Kernel.IfcRelAggregates)
                {
                    Xbim.Ifc4.Kernel.IfcRelAggregates relAgg = (Xbim.Ifc4.Kernel.IfcRelAggregates)rel;
                    var superElem = relAgg.RelatingObject;
                    foreach (var elem in relAgg.RelatedObjects)
                    {
                        BIMRelation3 newRel = new BIMRelation3() { ID1 = elem.EntityLabel.ToString(), ID2 = superElem.EntityLabel.ToString(), Relationship = "IfcRelAggregates" };
                        relList.Add(newRel);
                    }
                    continue;
                }

                if (rel is Xbim.Ifc4.ProductExtension.IfcRelContainedInSpatialStructure)
                {
                    Xbim.Ifc4.ProductExtension.IfcRelContainedInSpatialStructure relAgg = (Xbim.Ifc4.ProductExtension.IfcRelContainedInSpatialStructure)rel;
                    var superElem = relAgg.RelatingStructure;
                    foreach (var elem in relAgg.RelatedElements)
                    {
                        BIMRelation3 newRel = new BIMRelation3() { ID1 = elem.EntityLabel.ToString(), ID2 = superElem.EntityLabel.ToString(), Relationship = "IfcRelContainedInSpatialStructure" };
                        relList.Add(newRel);
                    }
                    continue;
                }

                if (rel is Xbim.Ifc4.ProductExtension.IfcRelAssociatesMaterial)
                {
                    Xbim.Ifc4.ProductExtension.IfcRelAssociatesMaterial matRel = (Xbim.Ifc4.ProductExtension.IfcRelAssociatesMaterial)rel;
                    if (matRel.RelatingMaterial is Xbim.Ifc4.MaterialResource.IfcMaterialList)
                    {
                        List<string> materials = ((Xbim.Ifc4.MaterialResource.IfcMaterialList)matRel.RelatingMaterial).Materials.Select(m => m.Name.Value.ToString()).ToList();
                        foreach (var elem in matRel.RelatedObjects)
                        {
                            foreach (string matName in materials)
                            {
                                if (!materialsDict.ContainsKey(elem.EntityLabel.ToString()))
                                {
                                    materialsDict[elem.EntityLabel.ToString()] = new List<string>();
                                }
                                materialsDict[elem.EntityLabel.ToString()].Add(matName);
                            }
                        }
                        continue;
                    }
                    if (matRel.RelatingMaterial is Xbim.Ifc4.MaterialResource.IfcMaterialLayerSetUsage)
                    {
                        List<string> materials = ((Xbim.Ifc4.MaterialResource.IfcMaterialLayerSetUsage)matRel.RelatingMaterial).ForLayerSet.MaterialLayers.Select(m => m.Material.Name.Value.ToString()).ToList();
                        foreach (var elem in matRel.RelatedObjects)
                        {
                            foreach (string matName in materials)
                            {
                                if (!materialsDict.ContainsKey(elem.EntityLabel.ToString()))
                                {
                                    materialsDict[elem.EntityLabel.ToString()] = new List<string>();
                                }
                                materialsDict[elem.EntityLabel.ToString()].Add(matName);
                            }
                        }
                        continue;
                    }
                    if (matRel.RelatingMaterial is Xbim.Ifc4.MaterialResource.IfcMaterialLayerSet)
                    {
                        List<string> materials = ((Xbim.Ifc4.MaterialResource.IfcMaterialLayerSet)matRel.RelatingMaterial).MaterialLayers.Select(m => m.Material.Name.Value.ToString()).ToList();
                        foreach (var elem in matRel.RelatedObjects)
                        {
                            foreach (string matName in materials)
                            {
                                if (!materialsDict.ContainsKey(elem.EntityLabel.ToString()))
                                {
                                    materialsDict[elem.EntityLabel.ToString()] = new List<string>();
                                }
                                materialsDict[elem.EntityLabel.ToString()].Add(matName);
                            }
                        }
                        continue;
                    }
                    if (matRel.RelatingMaterial is Xbim.Ifc4.MaterialResource.IfcMaterial)
                    {
                        string matName = ((Xbim.Ifc4.MaterialResource.IfcMaterial)matRel.RelatingMaterial).Name.Value.ToString();
                        foreach (var elem in matRel.RelatedObjects)
                        {
                            if (!materialsDict.ContainsKey(elem.EntityLabel.ToString()))
                            {
                                materialsDict[elem.EntityLabel.ToString()] = new List<string>();
                            }
                            materialsDict[elem.EntityLabel.ToString()].Add(matName);
                        }
                        continue;
                    }
                }

                // TODO: Still lots of unsupported IfcRels...
                //Debug.Print("Unsupported relationship type: " + rel.GetType().ToString() + " for relationship: " + rel.EntityLabel.ToString());
            }

            return relList;
        }

        public static ConcurrentBag<BIMRelation3> GetRelationInModel4x3(IfcStore model, out Dictionary<string, List<string>> materialsDict)
        {
            ConcurrentBag<BIMRelation3> relList = new ConcurrentBag<BIMRelation3>();
            materialsDict = new Dictionary<string, List<string>>();
            var modelRels = model.Instances.OfType<Xbim.Ifc4x3.Kernel.IfcRelationship>().ToList();
            foreach (var rel in modelRels)
            {
                if (rel is Xbim.Ifc4x3.Kernel.IfcRelAggregates)
                {
                    Xbim.Ifc4x3.Kernel.IfcRelAggregates relAgg = (Xbim.Ifc4x3.Kernel.IfcRelAggregates)rel;
                    var superElem = relAgg.RelatingObject;
                    foreach (var elem in relAgg.RelatedObjects)
                    {
                        BIMRelation3 newRel = new BIMRelation3() { ID1 = elem.EntityLabel.ToString(), ID2 = superElem.EntityLabel.ToString(), Relationship = "IfcRelAggregates" };
                        relList.Add(newRel);
                    }
                    continue;
                }

                if (rel is Xbim.Ifc4x3.ProductExtension.IfcRelContainedInSpatialStructure)
                {
                    Xbim.Ifc4x3.ProductExtension.IfcRelContainedInSpatialStructure relAgg = (Xbim.Ifc4x3.ProductExtension.IfcRelContainedInSpatialStructure)rel;
                    var superElem = relAgg.RelatingStructure;
                    foreach (var elem in relAgg.RelatedElements)
                    {
                        BIMRelation3 newRel = new BIMRelation3() { ID1 = elem.EntityLabel.ToString(), ID2 = superElem.EntityLabel.ToString(), Relationship = "IfcRelContainedInSpatialStructure" };
                        relList.Add(newRel);
                    }
                    continue;
                }

                if (rel is Xbim.Ifc4x3.ProductExtension.IfcRelAssociatesMaterial)
                {
                    Xbim.Ifc4x3.ProductExtension.IfcRelAssociatesMaterial matRel = (Xbim.Ifc4x3.ProductExtension.IfcRelAssociatesMaterial)rel;
                    if (matRel.RelatingMaterial is Xbim.Ifc4x3.MaterialResource.IfcMaterialList)
                    {
                        List<string> materials = ((Xbim.Ifc4x3.MaterialResource.IfcMaterialList)matRel.RelatingMaterial).Materials.Select(m => m.Name.Value.ToString()).ToList();
                        foreach (var elem in matRel.RelatedObjects)
                        {
                            foreach (string matName in materials)
                            {
                                if (!materialsDict.ContainsKey(elem.EntityLabel.ToString()))
                                {
                                    materialsDict[elem.EntityLabel.ToString()] = new List<string>();
                                }
                                materialsDict[elem.EntityLabel.ToString()].Add(matName);
                            }
                        }
                        continue;
                    }
                    if (matRel.RelatingMaterial is Xbim.Ifc4x3.MaterialResource.IfcMaterialLayerSetUsage)
                    {
                        List<string> materials = ((Xbim.Ifc4x3.MaterialResource.IfcMaterialLayerSetUsage)matRel.RelatingMaterial).ForLayerSet.MaterialLayers.Select(m => m.Material.Name.Value.ToString()).ToList();
                        foreach (var elem in matRel.RelatedObjects)
                        {
                            foreach (string matName in materials)
                            {
                                if (!materialsDict.ContainsKey(elem.EntityLabel.ToString()))
                                {
                                    materialsDict[elem.EntityLabel.ToString()] = new List<string>();
                                }
                                materialsDict[elem.EntityLabel.ToString()].Add(matName);
                            }
                        }
                        continue;
                    }
                    if (matRel.RelatingMaterial is Xbim.Ifc4x3.MaterialResource.IfcMaterialLayerSet)
                    {
                        List<string> materials = ((Xbim.Ifc4x3.MaterialResource.IfcMaterialLayerSet)matRel.RelatingMaterial).MaterialLayers.Select(m => m.Material.Name.Value.ToString()).ToList();
                        foreach (var elem in matRel.RelatedObjects)
                        {
                            foreach (string matName in materials)
                            {
                                if (!materialsDict.ContainsKey(elem.EntityLabel.ToString()))
                                {
                                    materialsDict[elem.EntityLabel.ToString()] = new List<string>();
                                }
                                materialsDict[elem.EntityLabel.ToString()].Add(matName);
                            }
                        }
                        continue;
                    }
                    if (matRel.RelatingMaterial is Xbim.Ifc4x3.MaterialResource.IfcMaterial)
                    {
                        string matName = ((Xbim.Ifc4x3.MaterialResource.IfcMaterial)matRel.RelatingMaterial).Name.Value.ToString();
                        foreach (var elem in matRel.RelatedObjects)
                        {
                            if (!materialsDict.ContainsKey(elem.EntityLabel.ToString()))
                            {
                                materialsDict[elem.EntityLabel.ToString()] = new List<string>();
                            }
                            materialsDict[elem.EntityLabel.ToString()].Add(matName);
                        }
                        continue;
                    }
                }

                // TODO: Still lots of unsupported IfcRels...
                //Debug.Print("Unsupported relationship type: " + rel.GetType().ToString() + " for relationship: " + rel.EntityLabel.ToString());
            }

            return relList;
        }

        public static void SaveModelAsXBIM(IfcStore model, string FileName)
        {
            // Save IFC to the internal XBIM format, which includes geometry
            Xbim3DModelContext context = new Xbim3DModelContext(model);
            context.CreateContext();

            // Save IFC to the internal XBIM format, which includes geometry
            model.SaveAs(FileName, StorageType.Xbim);
        }

        public static void SaveModelAsWEBIM(IfcStore model, string FileName)
        {
            // Save IFC to the internal XBIM format, which includes geometry
            Xbim3DModelContext context = new Xbim3DModelContext(model);
            context.CreateContext();

            using (var wexBimFile = File.Create(FileName))
            {
                using (var wexBimBinaryWriter = new BinaryWriter(wexBimFile))
                {
                    model.SaveAsWexBim(wexBimBinaryWriter);
                    wexBimBinaryWriter.Close();
                }
                wexBimFile.Close();
            }
        }
    }

    public static class ConverterGeneral
    {
        public enum Datasets { _3DFRONT, OBJ, COOHOM, IFC, GBXML }

        public static Vector3d VectorConverter(XbimPoint3D point)
        {
            return new Vector3d((float)point.X, (float)point.Y, (float)point.Z);
        }
        public static Vector4d VectorConverter(XbimQuaternion quaternion)
        {
            return new Vector4d((float)quaternion.X, (float)quaternion.Y, (float)quaternion.Z, (float)quaternion.W);
        }
    }
}
