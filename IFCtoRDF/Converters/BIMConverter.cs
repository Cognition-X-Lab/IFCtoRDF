using g3;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace IFCtoRDF
{
    public static class BIMConverter
    {
        public static BIMGraph ConvertBIMtoRDF(BIMModel model, string modelNamespace, string modelNamespaceShort, ProgressBar progressBar, bool detailedBOT = false)
        {
            progressBar.Value = 0;

            BIMGraph graph = new BIMGraph();

            graph.AddNamespace(modelNamespaceShort, modelNamespace, BIMGraph.Location.Data);

            string propNamespace = "http://bimkit.ca/props#";
            string propNamespaceShort = "prop";
            graph.AddNamespace(propNamespaceShort, propNamespace, BIMGraph.Location.Data);

            string ifcNamespaceShort = "ifc";
            string ifcNamespace = "https://standards.buildingsmart.org/IFC/DEV/IFC4/FINAL/OWL#";
            graph.AddNamespace(ifcNamespaceShort, ifcNamespace, BIMGraph.Location.Data);

            string botNamespaceShort = "bot";
            string botNamespace = "https://w3id.org/bot#";
            graph.AddNamespace(botNamespaceShort, botNamespace, BIMGraph.Location.Data);

            List<string> oldBotInterfaceElements = model.Objects.Where(k => k.Value.Properties.ContainsKey(botNamespaceShort) &&
                                                                                 (string)k.Value.Properties[botNamespaceShort] == "Interface")
                                                                       .Select(k => k.Value.ID)
                                                                       .ToList();
            for (int i = 0; i < oldBotInterfaceElements.Count; i++)
            {
                model.Objects.Remove(oldBotInterfaceElements[i]);
            }

            List<BIMObject> globalObjects = model.GetGlobalObjectList();
            progressBar.Maximum = globalObjects.Count;
            for (int i = 0; i < globalObjects.Count; i++)
            {
                BIMObject obj1 = globalObjects[i];

                // The Types
                string type = obj1.GetObjectType();
                graph.AppendTripple(modelNamespaceShort + ":" + obj1.ID, "rdf:type", ifcNamespaceShort + ":" + type);
                switch (type)
                {
                    case "IfcSite":
                        graph.AppendTripple(modelNamespaceShort + ":" + obj1.ID, "rdf:type", botNamespaceShort + ":Site");
                        obj1.Properties[botNamespaceShort] = "Site";
                        break;
                    case "IfcBuilding":
                        graph.AppendTripple(modelNamespaceShort + ":" + obj1.ID, "rdf:type", botNamespaceShort + ":Building");
                        obj1.Properties[botNamespaceShort] = "Building";
                        break;
                    case "IfcBuildingStorey":
                        graph.AppendTripple(modelNamespaceShort + ":" + obj1.ID, "rdf:type", botNamespaceShort + ":Storey");
                        obj1.Properties[botNamespaceShort] = "Storey";
                        break;
                    case "IfcSpace":
                        graph.AppendTripple(modelNamespaceShort + ":" + obj1.ID, "rdf:type", botNamespaceShort + ":Space");
                        obj1.Properties[botNamespaceShort] = "Space";
                        break;
                    default:
                        graph.AppendTripple(modelNamespaceShort + ":" + obj1.ID, "rdf:type", botNamespaceShort + ":Element");
                        obj1.Properties[botNamespaceShort] = "Element";
                        break;
                }

                // Existing Properties
                foreach (var prop in obj1.Properties)
                {
                    // 1. Sanitize the key so it's a valid QName local part.
                    string safeKey = SanitizeLocalName(prop.Key);

                    // 2. Pass the value as a proper, escaped literal (quoted).
                    string literalValue = "\"" + EscapeLiteral(prop.Value?.ToString() ?? "") + "\"";

                    graph.AppendTripple(modelNamespaceShort + ":" + obj1.ID, propNamespaceShort + ":" + safeKey, literalValue);
                }

                // Custom Properties
                graph.AppendTripple(modelNamespaceShort + ":" + obj1.ID, propNamespaceShort + ":location", obj1.CenterLocation.ToString());
                graph.AppendTripple(modelNamespaceShort + ":" + obj1.ID, propNamespaceShort + ":orientation", obj1.Orientation.ToString());
                double objVolume = obj1.Components.Sum(c => BIMComponent.CalculateVolumeAndSA(c.Value.Mesh).x);
                double objSA = obj1.Components.Sum(c => BIMComponent.CalculateVolumeAndSA(c.Value.Mesh).y);
                graph.AppendTripple(modelNamespaceShort + ":" + obj1.ID, propNamespaceShort + ":volume", Math.Abs(objVolume).ToString());
                graph.AppendTripple(modelNamespaceShort + ":" + obj1.ID, propNamespaceShort + ":surfaceArea", Math.Abs(objSA).ToString());

                // Components
                foreach (var comp in obj1.Components)
                {
                    // TODO: This is for future use, but for now we will not add the components to the graph to keep it simpler.
                    // It would use bot:hasSubElement relationship, and would also include the component's properties.
                }

                progressBar.Value = i;
            }

            progressBar.Value = progressBar.Maximum;

            // Now set all the relationships between the elements and their containing zones
            progressBar.Value = 0;
            for (int i = 0; i < globalObjects.Count; i++)
            {
                BIMObject obj1 = globalObjects[i];
                List<BIMRelation3> rels = model.Relations.Where(r => r.ID1 == obj1.ID && r.Relationship == "IfcRelContainedInSpatialStructure").ToList();
                foreach (var rel in rels)
                {
                    graph.AppendTripple(modelNamespaceShort + ":" + rel.ID2, botNamespaceShort + ":hasElement", modelNamespaceShort + ":" + rel.ID1);
                }
                progressBar.Value = i;
            }

            progressBar.Value = progressBar.Maximum;

            if (!detailedBOT)
            {
                return graph;
            }

            CreateBotInterfaces(model, graph, globalObjects, modelNamespaceShort, botNamespaceShort, progressBar);

            return graph;
        }

        private static string SanitizeLocalName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "_";

            var sb = new System.Text.StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                // Allow letters, digits, and the separators you already use.
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.')
                    sb.Append(c);
                else
                    sb.Append('_');   // spaces, colons, slashes, etc. -> underscore
            }

            // A QName local part can't start with '.', '-' or a digit in strict grammars;
            // prefix an underscore if needed to be safe.
            char first = sb[0];
            if (first == '.' || first == '-' || char.IsDigit(first))
                sb.Insert(0, '_');

            return sb.ToString();
        }

        private static string EscapeLiteral(string raw)
        {
            return raw
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        private static void CreateBotInterfaces(BIMModel model, BIMGraph graph, List<BIMObject> globalObjects, string modelNamespaceShort, string botNamespaceShort, ProgressBar progressBar)
        {
            progressBar.Value = 0;
            progressBar.Maximum = globalObjects.Count;
            for (int i = 0; i < globalObjects.Count; i++)
            {
                BIMObject obj1 = globalObjects[i];
                DMesh3 mesh1 = obj1.Components["combined"].MeshBB();
                if (obj1.Components.Sum(c => c.Value.Mesh.TriangleCount) > 0)
                {
                    for (int j = i + 1; j < globalObjects.Count; j++)
                    {
                        BIMObject obj2 = globalObjects[j];
                        DMesh3 mesh2 = obj2.Components["combined"].MeshBB();
                        if (obj2.Components.Sum(c => c.Value.Mesh.TriangleCount) > 0)
                        {
                            bool meshesAdjacent = GeomUtils.MeshDistance(mesh1, mesh2, out var m1, out var m2) < 0.001;
                            //bool meshesOverlap = GeomUtils.MeshOverlap(mesh1, mesh2);
                            if (meshesAdjacent)// && !meshesOverlap)
                            {
                                BIMObject newObj = new BIMObject()
                                {
                                    CenterLocation = (m1 + m2) / 2,
                                    Orientation = new Vector4d(),
                                    ID = Guid.NewGuid().ToString()
                                };
                                newObj.Properties[botNamespaceShort] = "Interface";
                                graph.AppendTripple(modelNamespaceShort + ":" + newObj.ID, "rdf:type", botNamespaceShort + ":Interface");
                                graph.AppendTripple(modelNamespaceShort + ":" + newObj.ID, botNamespaceShort + ":interfaceOf", modelNamespaceShort + ":" + obj1.ID);
                                graph.AppendTripple(modelNamespaceShort + ":" + newObj.ID, botNamespaceShort + ":interfaceOf", modelNamespaceShort + ":" + obj2.ID);

                                model.Objects.Add(newObj.ID, newObj);

                                BIMObject storyOfObj1 = BIMAutomationUtils.GetContainingStoryOfObject(model, obj1);
                                if (storyOfObj1 != null)
                                {
                                    BIMRelation3 levelreation = new BIMRelation3() { ID1 = newObj.ID, Relationship = "IfcRelContainedInSpatialStructure", ID2 = storyOfObj1.ID };
                                    model.Relations.Add(levelreation);
                                }

                                BIMRelation3 o1Relation = new BIMRelation3() { ID1 = newObj.ID, Relationship = "IfcRelConnectsElements", ID2 = obj1.ID };
                                model.Relations.Add(o1Relation);
                                BIMRelation3 o2Relation = new BIMRelation3() { ID1 = newObj.ID, Relationship = "IfcRelConnectsElements", ID2 = obj2.ID };
                                model.Relations.Add(o2Relation);
                            }
                        }
                    }
                }
                progressBar.Value = i;
            }
            progressBar.Value = progressBar.Maximum;
        }
    }
}
