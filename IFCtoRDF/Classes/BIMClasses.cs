using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using g3;

namespace IFCtoRDF
{
    public class BIMThing
    {
        public string ID;
        public Dictionary<string, object> Properties = new Dictionary<string, object>();

        public override string ToString()
        {
            return Properties.ContainsKey("Name") ? Properties["Name"].ToString() : ID;
        }
    }

    public class BIMModel : BIMThing
    {
        public int TRICOUNT = 1000;

        public Dictionary<string, BIMObject> Objects = new Dictionary<string, BIMObject>();
        public List<BIMRelation3> Relations = new List<BIMRelation3>();

        private static readonly string splitter = ":::";
        private static string[] splitters = { ":::" };
        public async static Task<BIMModel> Read_V1(string filePath, bool readCompObj = true)
        {
            bool CheckIfLineHeader(string lineStr)
            {
                return (lineStr == "-MODEL-" || lineStr == "-OBJECT-" || lineStr == "-COMPONENT-" || lineStr == "-RELATIONS-" || lineStr == "-END-");
            }

            BIMModel model = new BIMModel();
            using (StreamReader reader = new StreamReader(filePath))
            {
                string line = reader.ReadLine();
                while (!reader.EndOfStream)
                {
                    if (line == null)
                    {
                        break;
                    }
                    if (line == "-MODEL-")
                    {
                        model.ID = reader.ReadLine();
                        line = reader.ReadLine();
                        while (!CheckIfLineHeader(line))
                        {
                            string[] splitLine = line.Split(splitters, StringSplitOptions.None);
                            model.Properties.Add(splitLine[0], splitLine[1]);
                            line = reader.ReadLine();
                        }
                        continue;
                    }
                    if (line == "-OBJECT-")
                    {
                        BIMObject newObject = new BIMObject();
                        newObject.ID = reader.ReadLine();
                        newObject.CenterLocation = Converters.VectorConverter3(reader.ReadLine());
                        newObject.Orientation = Converters.VectorConverter4(reader.ReadLine());
                        line = reader.ReadLine();
                        while (!CheckIfLineHeader(line))
                        {
                            string[] splitLine = line.Split(splitters, StringSplitOptions.None);
                            newObject.Properties.Add(splitLine[0], splitLine[1]);
                            line = reader.ReadLine();
                        }
                        model.Objects.Add(newObject.ID, newObject);
                        continue;
                    }
                    if (line == "-COMPONENT-")
                    {
                        BIMComponent newComp = new BIMComponent();
                        string objID = reader.ReadLine();
                        newComp.ID = reader.ReadLine();
                        line = reader.ReadLine();
                        while (!CheckIfLineHeader(line))
                        {
                            string[] splitLine = line.Split(splitters, StringSplitOptions.None);
                            newComp.Properties.Add(splitLine[0], splitLine[1]);
                            line = reader.ReadLine();
                        }
                        model.Objects[objID].Components.Add(newComp.ID, newComp);
                        continue;
                    }
                    if (line == "-RELATIONS-")
                    {
                        line = reader.ReadLine();
                        while (!CheckIfLineHeader(line))
                        {
                            string[] splitLine = line.Split(splitters, StringSplitOptions.RemoveEmptyEntries);
                            model.Relations.Add(new BIMRelation3() { ID1 = splitLine[0], Relationship = splitLine[1], ID2 = splitLine[2] });
                            line = reader.ReadLine();
                        }
                        continue;
                    }
                    if (line == "-END-")
                    {
                        line = reader.ReadLine();
                        break;
                    }

                    line = reader.ReadLine();
                }
            }

            if (readCompObj)
            {
                Dictionary<string, BIMComponent> comps = new Dictionary<string, BIMComponent>();
                string dirName = Path.GetDirectoryName(filePath) + "\\" + Path.GetFileNameWithoutExtension(filePath);
                string[] objFilePaths = Directory.GetFiles(dirName, "*.obj");

                List<Task> tasks = new List<Task>();
                ConcurrentDictionary<string, BIMComponent> loadedComps = new ConcurrentDictionary<string, BIMComponent>();
                foreach (string objFilePath in objFilePaths)
                {
                    tasks.Add(Task.Run(() =>
                    {
                        BIMComponent compMesh = BIMComponent.ReadMesh(objFilePath);
                        loadedComps.TryAdd(compMesh.ID, compMesh);
                    }));
                }

                await Task.WhenAll(tasks);

                foreach (var obj in model.Objects)
                {
                    foreach (var compKey in obj.Value.Components.Keys)
                    {
                        BIMComponent comp = obj.Value.Components[compKey];
                        if (loadedComps.ContainsKey(comp.ID))
                        {
                            comp.Mesh = loadedComps[comp.ID].Mesh;
                        }
                    }
                }
            }

            return model;
        }

        // ================================================================================================================================================================================
        // This might not be needed anymore since V1 seems to run better
        public static async Task<BIMModel> Read_V2(string filePath, bool readCompObj = true)
        {
            int splitterLength = splitter.Length;
            HashSet<string> headers = new HashSet<string>() { "-MODEL-", "-OBJECT-", "-COMPONENT-", "-RELATIONS-", "-END-" };
            void FastSplitKeyValue(string line, out string key, out string val)
            {
                int idx = line.IndexOf(splitter);
                key = line.Substring(0, idx);
                val = line.Substring(idx + splitterLength);
            }

            void FastSplitThree(string line, out string a, out string b, out string c)
            {
                int i1 = line.IndexOf(splitter);
                int i2 = line.IndexOf(splitter, i1 + splitterLength);

                a = line.Substring(0, i1);
                b = line.Substring(i1 + splitterLength, i2 - (i1 + splitterLength));
                c = line.Substring(i2 + splitterLength);
            }

            BIMModel model = new BIMModel();

            using (StreamReader reader = new StreamReader(filePath))
            {
                string line = reader.ReadLine();

                while (line != null && !reader.EndOfStream)
                {
                    switch (line)
                    {
                        case "-MODEL-":
                            {
                                model.ID = reader.ReadLine();
                                line = reader.ReadLine();

                                while (line != null && !headers.Contains(line))
                                {
                                    FastSplitKeyValue(line, out string key, out string val);
                                    model.Properties[key] = val;
                                    line = reader.ReadLine();
                                }
                                continue;
                            }

                        case "-OBJECT-":
                            {
                                BIMObject obj = new BIMObject
                                {
                                    ID = reader.ReadLine(),
                                    CenterLocation = Converters.VectorConverter3(reader.ReadLine()),
                                    Orientation = Converters.VectorConverter4(reader.ReadLine())
                                };

                                line = reader.ReadLine();

                                while (line != null && !headers.Contains(line))
                                {
                                    FastSplitKeyValue(line, out string key, out string val);
                                    obj.Properties[key] = val;
                                    line = reader.ReadLine();
                                }

                                model.Objects[obj.ID] = obj;
                                continue;
                            }

                        case "-COMPONENT-":
                            {
                                string objID = reader.ReadLine();
                                BIMComponent comp = new BIMComponent
                                {
                                    ID = reader.ReadLine()
                                };

                                line = reader.ReadLine();

                                while (line != null && !headers.Contains(line))
                                {
                                    FastSplitKeyValue(line, out string key, out string val);
                                    comp.Properties[key] = val;
                                    line = reader.ReadLine();
                                }

                                model.Objects[objID].Components[comp.ID] = comp;
                                continue;
                            }

                        case "-RELATIONS-":
                            {
                                line = reader.ReadLine();
                                while (line != null && !headers.Contains(line))
                                {
                                    FastSplitThree(line, out string id1, out string rel, out string id2);
                                    model.Relations.Add(new BIMRelation3 { ID1 = id1, Relationship = rel, ID2 = id2 });
                                    line = reader.ReadLine();
                                }
                                continue;
                            }

                        case "-END-":
                            return model;
                    }

                    line = reader.ReadLine();
                }
            }

            // --------- Load mesh components (optimized) ----------
            if (readCompObj)
            {
                string dir = Path.Combine(Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(filePath));
                string[] objFiles = Directory.GetFiles(dir, "*.obj");

                ConcurrentDictionary<string, BIMComponent> loaded = new ConcurrentDictionary<string, BIMComponent>();

                // Much faster than spawning 1 task per file
                Parallel.ForEach(objFiles, objPath =>
                {
                    BIMComponent comp = BIMComponent.ReadMesh(objPath);
                    loaded.TryAdd(comp.ID, comp);
                });

                foreach (var obj in model.Objects.Values)
                {
                    foreach (var comp in obj.Components.Values)
                    {
                        if (loaded.TryGetValue(comp.ID, out var loadedComp))
                        {
                            comp.Mesh = loadedComp.Mesh;
                        }
                    }
                }
            }

            return model;
        }
        // ================================================================================================================================================================================

        public static async Task<BIMModel> Read(string fileName, bool readCompObj = true)
        {
            return await Read_V1(fileName, readCompObj);
        }

        public static void Write(string filePath, BIMModel model, bool writeCompObj = true)
        {
            using (StreamWriter streamWriter = new StreamWriter(filePath))
            {
                streamWriter.AutoFlush = true;
                streamWriter.WriteLine("-MODEL-");
                streamWriter.WriteLine(model.ID);
                foreach (var property in model.Properties)
                {
                    streamWriter.WriteLine(property.Key + splitter + property.Value);
                }
                foreach (var obj in model.Objects.Values)
                {
                    streamWriter.WriteLine("-OBJECT-");
                    streamWriter.WriteLine(obj.ID);
                    streamWriter.WriteLine(obj.CenterLocation.ToString());
                    streamWriter.WriteLine(obj.Orientation.ToString());
                    foreach (var property in obj.Properties)
                    {
                        streamWriter.WriteLine(property.Key + splitter + property.Value);
                    }
                    foreach (var comp in obj.Components.Values)
                    {
                        streamWriter.WriteLine("-COMPONENT-");
                        streamWriter.WriteLine(obj.ID);
                        streamWriter.WriteLine(comp.ID);
                        foreach (var property in comp.Properties)
                        {
                            streamWriter.WriteLine(property.Key + splitter + property.Value);
                        }
                    }
                }
                streamWriter.WriteLine("-RELATIONS-");
                foreach (var rel in model.Relations)
                {
                    streamWriter.WriteLine(rel.ID1 + splitter + rel.Relationship + splitter + rel.ID2);
                }
                streamWriter.WriteLine("-END-");
            }

            if (writeCompObj)
            {
                string dirName = Path.GetDirectoryName(filePath) + "\\" + Path.GetFileNameWithoutExtension(filePath);
                if (Directory.Exists(dirName))
                {
                    Directory.Delete(dirName, true);
                }
                Directory.CreateDirectory(dirName);
                foreach (BIMComponent comp in model.Objects.SelectMany(o => o.Value.Components.Select(c => c.Value)))
                {
                    string cmopFilePath = dirName + "\\" + comp.ID + ".obj";
                    if (!Directory.Exists(cmopFilePath))
                    {
                        BIMComponent.WriteMesh(cmopFilePath, comp, model.TRICOUNT);
                    }
                }
            }
        }

        public static BIMModel CombineModels(List<BIMModel> models, string modelName)
        {
            BIMModel combinedModel = new BIMModel();
            Dictionary<string, BIMComponent> tempCompDict = new Dictionary<string, BIMComponent>();
            for (int i = 0; i < models.Count; i++)
            {
                BIMModel model = models[i];

                foreach (var prop in model.Properties)
                {
                    if (combinedModel.Properties.TryGetValue(prop.Key, out object value))
                    {
                        if ((string)value != (string)prop.Value)
                        {
                            combinedModel.Properties[prop.Key] = (string)value + "$$$" + prop.Value;
                        }
                    }
                    else
                    {
                        combinedModel.Properties.Add(prop.Key, prop.Value);
                    }
                }
                foreach (var obj in model.Objects)
                {
                    if (combinedModel.Objects.ContainsKey(obj.Key))
                    {
                        string newID = Guid.NewGuid().ToString();
                        SwapIDsInRelations(ref model, obj.Value.ID, newID);
                        obj.Value.ID = newID;
                    }

                    combinedModel.Objects[obj.Value.ID] = obj.Value;

                    foreach (var compKey in obj.Value.Components.Keys.ToList())
                    {
                        if (tempCompDict.ContainsKey(compKey))
                        {
                            string newID = Guid.NewGuid().ToString();
                            SwapIDsInRelations(ref model, compKey, newID);
                            obj.Value.Components[compKey].ID = newID;
                            combinedModel.Objects[obj.Value.ID].Components.Add(obj.Value.Components[compKey].ID, obj.Value.Components[compKey]);
                        }

                        tempCompDict.Add(obj.Value.Components[compKey].ID, obj.Value.Components[compKey]);
                    }
                }
                foreach (var rel in model.Relations)
                {
                    combinedModel.Relations.Add(rel);
                }
            }

            combinedModel.Properties["Name"] = modelName;
            combinedModel.ID = Guid.NewGuid().ToString();

            return combinedModel;
        }

        public static void SwapIDsInRelations(ref BIMModel model, string objID, string newID)
        {
            foreach (var rel in model.Relations)
            {
                if (rel.ID1 == objID)
                {
                    rel.ID1 = newID;
                }
                if (rel.ID2 == objID)
                {
                    rel.ID2 = newID;
                }
            }
        }

        public List<BIMObject> GetGlobalObjectList()
        {
            List<BIMObject> globalObjects = new List<BIMObject>();
            foreach (BIMObject o in Objects.Values)
            {
                BIMComponent combinedComp = BIMComponent.CombineComps(o.Components.Values.ToList());
                Matrix4 transMat = GeomUtils.GetTranslationMatrixFromLocationOrientation(o.CenterLocation, o.Orientation);
                combinedComp.Mesh = GeomUtils.TranslateMesh(transMat, combinedComp.Mesh);
                BIMObject newObject = new BIMObject()
                {
                    ID = o.ID,
                    Properties = o.Properties,
                    CenterLocation = o.CenterLocation,
                    Orientation = o.Orientation,
                    Components = new Dictionary<string, BIMComponent>() { { combinedComp.ID, combinedComp } }
                };

                globalObjects.Add(newObject);
            }

            return globalObjects;
        }
    }

    public class BIMObject : BIMThing
    {
        public Vector3d CenterLocation;
        public Vector4d Orientation;
        public Dictionary<string, BIMComponent> Components = new Dictionary<string, BIMComponent>();

        public string GetObjectType()
        {
            if (Properties.ContainsKey("IfcType") == false)
            {
                return null;
            }
            return Properties["IfcType"].ToString().Split(new string[] { "." }, StringSplitOptions.RemoveEmptyEntries).Last();
        }
        public string GetObjectInfoString(string seperator = ", ", bool includeKeys = true)
        {
            return string.Join(seperator, Properties.Select(property => (includeKeys ? property.Key + " = " : "") + (string)property.Value));
        }
    }

    public class ccBIMObject : BIMThing
    {
        public Vector3d CenterLocation;
        public Vector4d Orientation;
        public ConcurrentDictionary<string, BIMComponent> Components = new ConcurrentDictionary<string, BIMComponent>();

        public BIMObject MakeRegularBIMObject()
        {
            BIMObject newObj = new BIMObject();
            newObj.ID = this.ID;
            newObj.Properties = this.Properties;
            newObj.CenterLocation = this.CenterLocation;
            newObj.Orientation = this.Orientation;
            foreach (var comp in Components)
            {
                newObj.Components.Add(comp.Key, comp.Value);
            }
            return newObj;
        }
    }

    public class BIMComponent : BIMThing
    {
        public DMesh3 Mesh;

        public static BIMComponent ReadMesh(string filePath)
        {
            BIMComponent newComp = new BIMComponent
            {
                Mesh = StandardMeshReader.ReadMesh(filePath),
                ID = Path.GetFileNameWithoutExtension(filePath)
            };
            return newComp;

            //DMesh3Builder builder = new DMesh3Builder();
            //StandardMeshReader reader = new StandardMeshReader() { MeshBuilder = builder };
            //IOReadResult result = reader.Read(path, ReadOptions.Defaults);
            //if (result.code == IOCode.Ok)
            //    List<DMesh3> meshes = builder.Meshes;
        }

        public static void WriteMesh(string filePath, BIMComponent component, int triCount = int.MaxValue)
        {
            DMesh3 newMesh = component.Mesh;
            try
            {
                if (triCount != int.MaxValue)
                {
                    newMesh = MeshFixUtils.MeshReduce(newMesh, triCount);
                    newMesh = MeshFixUtils.UnusedVertexRemover(newMesh);
                }
                //newMesh = Utils.DuplicateTriangleRemover(newMesh);
                newMesh = MeshFixUtils.VertexAdderByTri(newMesh);
            }
            catch (Exception e)
            {
                string errorString = e.ToString();
            }
            if (newMesh.TriangleCount == 0 || newMesh.VertexCount == 0)
            {
                throw new Exception("Mesh Reduce and Triangle Adder Failed");
            }
            IOWriteResult result = StandardMeshWriter.WriteFile(filePath, new List<WriteMesh>() { new WriteMesh(newMesh) }, WriteOptions.Defaults);
        }

        public static BIMComponent CombineComps(List<BIMComponent> components, string defaultID = "combined")
        {
            BIMComponent combinedComp = new BIMComponent() { ID = defaultID };
            DMesh3 combined = new DMesh3();
            MeshEditor editor = new MeshEditor(combined);
            for (int i = 0; i < components.Count; i++)
            {
                BIMComponent modelComp = components[i];
                foreach (var prop in modelComp.Properties)
                {
                    if (combinedComp.Properties.ContainsKey(prop.Key))
                    {
                        combinedComp.Properties[prop.Key] = (string)combinedComp.Properties[prop.Key] + ":::" + (string)prop.Value;
                    }
                    else
                    {
                        combinedComp.Properties.Add(prop.Key, prop.Value);
                    }
                }

                if (modelComp.Mesh != null)
                {
                    editor.AppendMesh(modelComp.Mesh);
                }
            }

            combinedComp.Mesh = editor.Mesh;

            return combinedComp;
        }

        public static bool AreMeshesIdentical(DMesh3 meshA, DMesh3 meshB)
        {
            if (meshA.VertexCount != meshB.VertexCount || meshA.TriangleCount != meshB.TriangleCount)
                return false;

            // Compare all vertices
            foreach (int vid in meshA.VertexIndices())
            {
                if (meshA.GetVertex(vid) != meshB.GetVertex(vid)) return false;
            }

            // Compare all triangles
            foreach (int tid in meshA.TriangleIndices())
            {
                if (meshA.GetTriangle(tid) != meshB.GetTriangle(tid)) return false;
            }

            return true;
        }

        public DMesh3 MeshBB()
        {
            if (Mesh == null)
            {
                return null;
            }

            DMesh3 boxMesh = new TrivialBox3Generator
            {
                Box = new Box3d(Mesh.CachedBounds),
                NoSharedVertices = false
            }.Generate().MakeDMesh();

            return boxMesh;
        }

        public static Vector2d CalculateVolumeAndSA(DMesh3 mesh)
        {
            // 1. Get all triangle indices belonging to the mesh
            var triangleIndices = mesh.TriangleIndices();

            // 2. Call the VolumeArea measurement function
            // Pass the mesh, the list of triangles, and the vertex lookup delegate
            Vector2d volArea = MeshMeasurements.VolumeArea(mesh, triangleIndices, mesh.GetVertex);

            // 3. Extract and return the volume (stored in the X property) and surface area (stored in the Y property)
            return volArea;
        }
    }

    public class BIMRelation3
    {
        public string ID1;
        public string ID2;
        public string Relationship;
    }

    public class BIMRelation4
    {
        public string ID1;
        public string ID2;
        public string Relationship;
        public string Value;
    }

    public static class Converters
    {
        public static Vector3d VectorConverter3(string vstring)
        {
            string[] stringSplit = vstring.Split(new char[] { '(', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return new Vector3d(Convert.ToDouble(stringSplit[0]), Convert.ToDouble(stringSplit[1]), Convert.ToDouble(stringSplit[2]));
        }
        public static Vector4d VectorConverter4(string qstring)
        {
            string[] stringSplit = qstring.Split(new char[] { '(', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return new Vector4d(Convert.ToDouble(stringSplit[0]), Convert.ToDouble(stringSplit[1]), Convert.ToDouble(stringSplit[2]), Convert.ToDouble(stringSplit[3]));
        }
    }

    public static class BIMAutomationUtils
    {
        public static List<string> StandardSpaceBoundTypesXY = new List<string>() { "IfcWall", "IfcWallStandardCase", "IfcPlate", "IfcSlab", "IfcDoor", "IfcWindow", "IfcCovering" };
        public static List<string> StandardSpaceBoundTypesZ = new List<string>() { "IfcPlate", "IfcSlab", "IfcRoof", "IfcCeiling", "IfcCovering" };
        public static BIMObject CreateSpaceFromLocation(BIMModel model, Vector3d location, ref BIMRelation3 levelRelation, bool bb = true, float maxX = 10.0f, float maxY = 10.0f, float maxZ = 10.0f)
        {
            Vector3d xRight = new Vector3d(1, 0, 0);
            Vector3d xLeft = new Vector3d(-1, 0, 0);
            Vector3d yForward = new Vector3d(0, 1, 0);
            Vector3d yBack = new Vector3d(0, -1, 0);
            Vector3d zUp = new Vector3d(0, 0, 1);
            Vector3d zDown = new Vector3d(0, 0, -1);

            Dictionary<Vector3d, float> directionVectDict = new Dictionary<Vector3d, float>
            {
                { xRight, maxX },
                { xLeft, maxX },
                { yForward, maxY },
                { yBack, maxY },
                { zUp, maxZ },
                { zDown, maxZ }
            };

            BIMObject roomBoarderObject = null;
            foreach (BIMObject os in model.Objects.Values)
            {
                if (StandardSpaceBoundTypesXY.Contains(os.GetObjectType()) || StandardSpaceBoundTypesZ.Contains(os.GetObjectType()))
                {
                    Matrix4 T = GeomUtils.GetTranslationMatrixFromLocationOrientation(os.CenterLocation, os.Orientation);
                    GeomUtils.GetXYZDimentions(os.Components.SelectMany(v => v.Value.Mesh.Vertices()).ToList(), out Vector3d center, out Vector3d dimentions);
                    var meshes = bb ? new List<DMesh3>() { GeomUtils.CreateBoundingBox(center, dimentions) } : os.Components.Select(c => c.Value.Mesh);
                    foreach (DMesh3 mesh in meshes)
                    {
                        DMesh3 tMesh = GeomUtils.TranslateMesh(T, mesh);
                        foreach (var vect in directionVectDict.Keys.ToList())
                        {
                            if (vect == zUp && location.z > os.CenterLocation.z)
                            {
                                continue;
                            }
                            if (vect == zDown && location.z < os.CenterLocation.z)
                            {
                                continue;
                            }
                            if (vect == xRight && location.x > os.CenterLocation.x)
                            {
                                continue;
                            }
                            if (vect == xLeft && location.x < os.CenterLocation.x)
                            {
                                continue;
                            }
                            if (vect == yForward && location.y > os.CenterLocation.y)
                            {
                                continue;
                            }
                            if (vect == yBack && location.y < os.CenterLocation.y)
                            {
                                continue;
                            }

                            if (vect == zUp || vect == zDown)
                            {
                                if (!StandardSpaceBoundTypesZ.Contains(os.GetObjectType()))
                                {
                                    continue;
                                }
                            }
                            if (vect == xRight || vect == xLeft || vect == yForward || vect == yBack)
                            {
                                if (!StandardSpaceBoundTypesXY.Contains(os.GetObjectType()))
                                {
                                    continue;
                                }
                            }

                            if (GeomUtils.RayIntersectsMesh(location, vect, tMesh, out Vector3d intPoint, false))
                            {
                                float dist = (float)(intPoint - location).Length;
                                if (dist < directionVectDict[vect])
                                {
                                    directionVectDict[vect] = dist;
                                    if (vect == xRight || vect == xLeft || vect == yForward || vect == yBack)
                                    {
                                        roomBoarderObject = os;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            Vector3d midpoint = location + new Vector3d((directionVectDict[xRight] - directionVectDict[xLeft]) / 2.0, (directionVectDict[yForward] - directionVectDict[yBack]) / 2.0, (directionVectDict[zUp] - directionVectDict[zDown]) / 2.0);
            Vector3d dims = new Vector3d(directionVectDict[xRight] + directionVectDict[xLeft], directionVectDict[yForward] + directionVectDict[yBack], directionVectDict[zUp] + directionVectDict[zDown]);

            DMesh3 newMesh = GeomUtils.CreateBoundingBox(new Vector3d(), dims, FaceSide.FRONT);
            BIMObject newObj = new BIMObject() { ID = Guid.NewGuid().ToString(), CenterLocation = midpoint, Orientation = new Vector4d() };
            newObj.Properties.Add("IfcType", "IfcSpace");
            BIMComponent newComp = new BIMComponent() { ID = Guid.NewGuid().ToString(), Mesh = newMesh };
            newObj.Components.Add(newComp.ID, newComp);

            levelRelation = new BIMRelation3() { ID1 = newObj.ID, Relationship = "IfcRelContainedInSpatialStructure" };
            if (roomBoarderObject != null)
            {
                BIMObject story = GetContainingStoryOfObject(model, roomBoarderObject);
                if (story != null)
                {
                    levelRelation.ID2 = story.ID;
                }
            }

            return newObj;
        }

        public static BIMObject GetContainingStoryOfObject(BIMModel model, BIMObject obj)
        {
            // Go over the relationships and find the IfcRelContainted of the object to find the story of the object
            foreach (BIMRelation3 rel in model.Relations)
            {
                if (rel.ID1 == obj.ID && (rel.Relationship == "IfcRelAggregates" || rel.Relationship == "IfcRelContainedInSpatialStructure"))
                {
                    if (model.Objects.TryGetValue(rel.ID2, out BIMObject story))
                    {
                        if (story.GetObjectType() == "IfcBuildingStorey")
                        {
                            return story;
                        }
                    }
                }
            }

            return null;
        }

        public static List<BIMObject> GetStories(BIMModel model)
        {
            return model.Objects.Values.Where(o => o.GetObjectType() == "IfcBuildingStorey").ToList();
        }
        public static List<BIMObject> GetObjectPartOfStory(BIMModel model, List<BIMObject> stories, bool children = true, bool parents = true, bool includeStory = true)
        {
            List<BIMObject> allObjects = model.Objects.Values.ToList();
            if (stories.Count == 0)
            {
                return allObjects;
            }

            var objectsByID = allObjects.ToDictionary(o => o.ID, o => o);
            var relationsByID1 = model.Relations.GroupBy(r => r.ID1).ToDictionary(g => g.Key, g => g.ToList());
            var relationsByID2 = model.Relations.GroupBy(r => r.ID2).ToDictionary(g => g.Key, g => g.ToList());

            HashSet<BIMObject> result = new HashSet<BIMObject>();
            foreach (var story in stories)
            {
                result.Add(story);
            }

            Queue<BIMObject> queue = new Queue<BIMObject>();
            if (children)
            {
                queue = new Queue<BIMObject>(result);
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    if (relationsByID2.TryGetValue(current.ID, out var outgoing))
                    {
                        foreach (var rel in outgoing)
                        {
                            if (rel.Relationship == "IfcRelAggregates" || rel.Relationship == "IfcRelContainedInSpatialStructure")
                            {
                                if (objectsByID.TryGetValue(rel.ID1, out var child))
                                    if (result.Add(child))
                                        queue.Enqueue(child);
                            }
                        }
                    }
                }
            }

            if (parents)
            {
                queue = new Queue<BIMObject>(result);
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    if (relationsByID1.TryGetValue(current.ID, out var incoming2))
                    {
                        foreach (var rel in incoming2)
                        {
                            if (rel.Relationship == "IfcRelAggregates" || rel.Relationship == "IfcRelContainedInSpatialStructure")
                            {
                                if (objectsByID.TryGetValue(rel.ID2, out var parent))
                                    if (result.Add(parent))
                                        queue.Enqueue(parent);
                            }
                        }
                    }
                }
            }

            if (!includeStory)
            {
                result.RemoveWhere(o => stories.Contains(o));
            }

            return result.ToList();
        }
    }
}
