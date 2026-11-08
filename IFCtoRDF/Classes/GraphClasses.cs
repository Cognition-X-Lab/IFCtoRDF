using Microsoft.SqlServer.Server;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using VDS.RDF;
using VDS.RDF.Ontology;
using VDS.RDF.Parsing;
using VDS.RDF.Parsing.Handlers;
using VDS.RDF.Query;
using VDS.RDF.Query.Algebra;
using VDS.RDF.Query.Inference;
using VDS.RDF.Shacl;
using VDS.RDF.Shacl.Validation;
using VDS.RDF.Writing;
using static Lucene.Net.QueryParsers.Flexible.Core.Nodes.PathQueryNode;
using static Lucene.Net.Util.Fst.Util;
using Graph = VDS.RDF.Graph;
using Path = System.IO.Path;

namespace IFCtoRDF
{
    public class BIMGraph
    {
        public OntologyGraph Data = new OntologyGraph();

        // There is probably a better way to do this using ReasonerGraph
        public OntologyGraph Schema = new OntologyGraph();
        private RdfsReasoner reasonerRdfs = new RdfsReasoner();
        private SimpleN3RulesReasoner reasonerN3 = new SimpleN3RulesReasoner();
        private SkosReasoner reasonerSkos = new SkosReasoner();
        private OwlReasoner_CS reasonerOwl = new OwlReasoner_CS();
        //private OwlReasonerWrapper reasonerOwl_wrapper;

        private readonly object _sync = new object();

        public BIMGraph()
        {
            //reasonerOwl_wrapper = new OwlReasonerWrapper(reasonerOwl);
        }

        public bool ApplySchemaFile(string fileName)
        {
            OntologyGraph newSchema = new OntologyGraph();
            if (Path.GetExtension(fileName).ToLower() == ".ttl")
            {
                TurtleParser parser = new TurtleParser();
                parser.Load(newSchema, fileName);
            }
            else if (Path.GetExtension(fileName).ToLower() == ".xml")
            {
                RdfXmlParser parser = new RdfXmlParser();
                parser.Load(newSchema, fileName);
            }
            else
            {
                Debug.Print($"Unsupported file extension: {Path.GetExtension(fileName)}. Only .ttl and .xml are supported.");
                return false;
            }

            return ApplySchema(newSchema);
        }

        public bool ApplySchema(OntologyGraph newSchema)
        {
            bool containsNewContent = false;
            List<string> badNameSpaces = new List<string>();
            Dictionary<string, Uri> newPrefixes = new Dictionary<string, Uri>();

            // Some schemas do not specify the prefix and Uri directly but have it under the ontology.
            IUriNode rdfType = newSchema.CreateUriNode(new Uri("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"));
            IUriNode owlOntology = newSchema.CreateUriNode(new Uri("http://www.w3.org/2002/07/owl#Ontology"));
            IUriNode preferredPrefix = newSchema.CreateUriNode(new Uri("http://purl.org/vocab/vann/preferredNamespacePrefix"));
            IUriNode preferredUri = newSchema.CreateUriNode(new Uri("http://purl.org/vocab/vann/preferredNamespaceUri"));

            List<Triple> newSchemas = newSchema.GetTriplesWithPredicateObject(rdfType, owlOntology).ToList();

            foreach (Triple triple in newSchemas)
            {
                Triple prefixTriple = newSchema.GetTriplesWithSubjectPredicate(triple.Subject, preferredPrefix).FirstOrDefault();
                Triple uriTriple = newSchema.GetTriplesWithSubjectPredicate(triple.Subject, preferredUri).FirstOrDefault();

                if (prefixTriple?.Object is ILiteralNode prefixNode && uriTriple?.Object is IUriNode uriNode)
                {
                    string prefix = prefixNode.Value;
                    Uri uri = uriNode.Uri;

                    newSchema.NamespaceMap.AddNamespace(prefix, uri);
                }
            }

            foreach (string prefix in newSchema.NamespaceMap.Prefixes)
            {
                Uri newNamespaceUri = newSchema.NamespaceMap.GetNamespaceUri(prefix);

                if (Schema.NamespaceMap.HasNamespace(prefix))
                {
                    Uri existingNamespaceUri = Schema.NamespaceMap.GetNamespaceUri(prefix);

                    // The same prefix is being used for a different namespace
                    if (!existingNamespaceUri.Equals(newNamespaceUri))
                    {
                        Debug.Print($"Prefix '{prefix}' is already mapped to " + $"'{existingNamespaceUri}' and cannot be mapped to " + $"'{newNamespaceUri}'. New mapping ignored");
                        badNameSpaces.Add(prefix);
                        newPrefixes[prefix] = existingNamespaceUri;
                    }
                }
                else
                {
                    containsNewContent = true;
                }
            }

            foreach (string prefix in badNameSpaces)
            {
                newSchema.NamespaceMap.RemoveNamespace(prefix);
            }

            foreach (var kvp in newPrefixes)
            {
                newSchema.NamespaceMap.AddNamespace(kvp.Key, kvp.Value);
            }

            // The schema might add triples to an existing namespace
            if (!containsNewContent)
            {
                foreach (Triple triple in newSchema.Triples)
                {
                    if (!Schema.ContainsTriple(triple))
                    {
                        containsNewContent = true;
                        break;
                    }
                }
            }

            // Nothing new was found
            if (!containsNewContent)
            {
                Debug.Print("Schema already exists.");
                return false;
            }

            // Add the namespaces and triples from the new schema
            Schema.Merge(newSchema, true);

            // Reinitialise the reasoners using the updated schema
            reasonerRdfs.Initialise(Schema);
            reasonerN3.Initialise(Schema);
            reasonerSkos.Initialise(Schema);
            reasonerOwl.Initialise(Schema);

            // Copy any new schema namespaces into the data graph
            badNameSpaces = new List<string>();
            foreach (string prefix in Schema.NamespaceMap.Prefixes)
            {
                if (string.IsNullOrWhiteSpace(prefix))
                {
                    Debug.Print($"Prefix '{prefix}' is empty and should not be used.");
                    badNameSpaces.Add(prefix);
                    continue;
                }

                Uri namespaceUri = Schema.NamespaceMap.GetNamespaceUri(prefix);

                if (!Data.NamespaceMap.HasNamespace(prefix))
                {
                    Data.NamespaceMap.AddNamespace(prefix, namespaceUri);
                }
            }

            foreach (string prefix in badNameSpaces)
            {
                Schema.NamespaceMap.RemoveNamespace(prefix);
            }

            return true;
        }

        public void SaveSchemaFile(string fileName)
        {
            using (StreamWriter streamWriter = new StreamWriter(fileName))
            {
                var writer = new CompressingTurtleWriter(); // or RdfXmlWriter(), or NTriplesWriter()
                writer.Save(Schema, streamWriter);
            }
        }

        public bool CheckIfSchemaLoaded(string schema)
        {
            return Schema.NamespaceMap.Prefixes.Contains(schema);
        }

        public void AppendDataFile(string fileName)
        {
            TurtleParser parser = new TurtleParser();
            parser.Load(Data, fileName);
        }

        public void SaveDataFile(string fileName)
        {
            lock (_sync)
            {
                using (StreamWriter streamWriter = new StreamWriter(fileName))
                {
                    var writer = new CompressingTurtleWriter(); // or RdfXmlWriter(), or NTriplesWriter()
                    writer.Save(Data, streamWriter);
                }
            }
        }

        public void ClearAllData()
        {
            Data.Clear();
        }

        public int TripleCount(bool fromData = true)
        {
            return fromData ? Data.Triples.Count : Schema.Triples.Count;
        }

        public enum Location { Data, Schema, Both }
        public void AddNamespace(string nameSpaceShort, string nameSpace, Location location)
        {
            if (location == Location.Data || location == Location.Both)
            {
                AddNamespace(nameSpaceShort, nameSpace, true);
            }
            if (location == Location.Schema || location == Location.Both)
            {
                AddNamespace(nameSpaceShort, nameSpace, false);
            }
        }
        private void AddNamespace(string nameSpaceShort, string nameSpace, bool fromData)
        {
            var datasource = fromData ? Data : Schema;
            var ex = new NamespaceMapper();
            ex.AddNamespace(nameSpaceShort, new Uri(nameSpace));
            datasource.NamespaceMap.Import(ex);
        }

        public void RemoveNamespace(string nameSpaceShort, string nameSpace, Location location)
        {
            if (location == Location.Data || location == Location.Both)
            {
                RemoveNamespace(nameSpaceShort, true);
            }
            if (location == Location.Schema || location == Location.Both)
            {
                RemoveNamespace(nameSpaceShort, false);
            }
        }
        private void RemoveNamespace(string nameSpaceShort, bool fromData = true)
        {
            var datasource = fromData ? Data : Schema;
            datasource.NamespaceMap.RemoveNamespace(nameSpaceShort);
        }

        public Dictionary<string, string> GetNamespaces(bool fromData = true)
        {
            var datasource = fromData ? Data : Schema;
            var namespaces = new Dictionary<string, string>();
            foreach (string prefix in datasource.NamespaceMap.Prefixes)
            {
                namespaces[prefix] = datasource.NamespaceMap.GetNamespaceUri(prefix).ToString();
            }
            return namespaces;
        }

        public List<string> GetSchemaPrefixes()
        {
            List<string> namespaces = new List<string>();
            foreach (string classes in GetClasses(false))
            {
                namespaces.Add(classes.Split(':')[0]);
            }
            return namespaces.Distinct().ToList();
        }

        //public bool CheckIfUri(string element)
        //{
        //    return Uri.IsWellFormedUriString(element, UriKind.Absolute);
        //}
        //public void AppendTripple(string sub, string pre, string obj, bool fromData = true)
        //{
        //    IGraph dataSource = fromData ? Data : Schema;
        //    IUriNode subNode = dataSource.CreateUriNode(sub);
        //    IUriNode preNode = dataSource.CreateUriNode(pre);
        //    INode objNode = CheckIfUri(obj) ? (INode)dataSource.CreateUriNode(obj) : (INode)dataSource.CreateLiteralNode(obj);
        //    Triple triple = new Triple(subNode, preNode, objNode);
        //    dataSource.Assert(triple);
        //}
        //public bool RemoveMatchingTriple(string sub, string pre, string obj, bool fromData = true)
        //{
        //    IGraph dataSource = fromData ? Data : Schema;
        //    int counter = dataSource.Triples.Count;

        //    IUriNode subNode = dataSource.CreateUriNode(sub);
        //    IUriNode preNode = dataSource.CreateUriNode(pre);
        //    INode objNode = CheckIfUri(obj) ? (INode)dataSource.CreateUriNode(obj) : dataSource.CreateLiteralNode(obj);

        //    dataSource.Retract(subNode, preNode, objNode);

        //    if (dataSource.Triples.Count == counter)
        //    {
        //        // If no exact match was found, try to remove triples with the same subject and predicate, but matching object value
        //        var temp = dataSource.GetTriplesWithSubjectPredicate(subNode, preNode);
        //        List<Triple> triplesToRemove = temp.Where(triple => triple.Object is ILiteralNode && obj.Contains(((ILiteralNode)triple.Object).Value)).ToList();
        //        //List<Triple> triplesToRemove = new List<Triple>();
        //        //foreach (Triple triple in temp)
        //        //{
        //        //    if(triple.Object is ILiteralNode && obj.Contains(((ILiteralNode)triple.Object).Value))
        //        //    {
        //        //        triplesToRemove.Add(triple);
        //        //    }
        //        //}
        //        dataSource.Retract(triplesToRemove);
        //    }

        //    return dataSource.Triples.Count != counter;
        //}

        public void AppendTripple(string sub, string pre, string obj, bool fromData = true)
        {
            lock (_sync)
            {
                IGraph dataSource = fromData ? Data : Schema;

                INode subNode = StringToNode(dataSource, sub);
                INode preNode = StringToNode(dataSource, pre);
                INode objNode = StringToNode(dataSource, obj);

                if (subNode != null && preNode != null && objNode != null)
                {
                    dataSource.Assert(new Triple(subNode, preNode, objNode));
                }
            }
        }
        public bool RemoveMatchingTriple(string sub, string pre, string obj, bool fromData = true)
        {
            lock (_sync)
            {
                IGraph dataSource = fromData ? Data : Schema;
                int counter = dataSource.Triples.Count;

                INode subNode = StringToNode(dataSource, sub);
                INode preNode = StringToNode(dataSource, pre);
                INode objNode = StringToNode(dataSource, obj);

                // Primary path: exact node match (now symmetric with AppendTripple).
                dataSource.Retract(new Triple(subNode, preNode, objNode));

                // Fallback: only if nothing was removed AND the object is a literal.
                // Matches on lexical value alone, ignoring datatype/language differences.
                if (dataSource.Triples.Count == counter && objNode is ILiteralNode objLit)
                {
                    var triplesToRemove = dataSource
                        .GetTriplesWithSubjectPredicate(subNode, preNode)
                        .Where(t => t.Object is ILiteralNode lit && lit.Value == objLit.Value)
                        .ToList();

                    dataSource.Retract(triplesToRemove);
                }

                return dataSource.Triples.Count != counter;
            }
        }
        public void ApplyChanges(IEnumerable<Tuple<string, string, string>> deletes, IEnumerable<Tuple<string, string, string>> adds)
        {
            lock (_sync)
            {
                if (deletes != null)
                    foreach (var t in deletes) RemoveMatchingTriple(t.Item1, t.Item2, t.Item3);
                if (adds != null)
                    foreach (var t in adds) AppendTripple(t.Item1, t.Item2, t.Item3);
            }
        }

        public void ApplyReasoners()
        {
            // Check rdfs:domain, rdfs:range, schema:domainIncludes, schema:rangeIncludes, owl:disjointWith, owl:propertyDisjointWith
            reasonerRdfs.Apply(Data);
            reasonerN3.Apply(Data);
            reasonerSkos.Apply(Data);
            reasonerOwl.Apply(Data);
        }

        public string CheckDataSHACL()
        {
            ApplyReasoners();

            ShapesGraph shapes = new ShapesGraph(Schema);
            Report report = shapes.Validate(Data);

            string returnString = report.Conforms ? "Data Passes SHACL Checks" : "Data Violates SHACL Checks:";
            foreach (var result in report.Results)
            {
                returnString += "\n\n";
                returnString += result.FocusNode.ToString() + " \n " + result.ResultValue.ToString() + "\n" + result.Message.ToString();
            }

            return returnString;
        }

        public string CheckDataOwl()
        {
            ApplyReasoners();

            string returnString = "";

            var rdfType = Data.CreateUriNode("rdf:type");

            // Class disjoint violations (owl:disjointWith)
            var disjointClasses = Schema.GetTriplesWithPredicate(Data.CreateUriNode("owl:disjointWith"));
            foreach (var d in disjointClasses)
            {
                var c1 = d.Subject;
                var c2 = d.Object;
                var conflictNodes = Data.GetTriplesWithPredicateObject(rdfType, c1)
                                        .Select(t => t.Subject)
                                        .Intersect(Data.GetTriplesWithPredicateObject(rdfType, c2)
                                                      .Select(t => t.Subject));
                foreach (var node in conflictNodes)
                    returnString += "\nOWL Class Disjoint Violation: " +
                                    NodeToQNameOrString(Data, node) +
                                    " is instance of both " +
                                    NodeToQNameOrString(Data, c1) +
                                    " and " +
                                    NodeToQNameOrString(Data, c2);
            }

            // Property disjoint violations (owl:propertyDisjointWith)
            var propDisjoint = Schema.GetTriplesWithPredicate(Data.CreateUriNode("owl:propertyDisjointWith"));
            foreach (var d in propDisjoint)
            {
                var p1 = d.Subject;
                var p2 = d.Object;
                var conflictNodes = Data.GetTriplesWithPredicate(p1)
                                        .Select(t => t.Subject)
                                        .Intersect(Data.GetTriplesWithPredicate(p2)
                                                      .Select(t => t.Subject));
                foreach (var node in conflictNodes)
                    returnString += "\nOWL Property Disjoint Violation: " +
                                    NodeToQNameOrString(Data, node) +
                                    " has valued for both " +
                                    NodeToQNameOrString(Data, p1) +
                                    " and " +
                                    NodeToQNameOrString(Data, p2);
            }

            return string.IsNullOrWhiteSpace(returnString) ? "Data Passes Owl Checks" : "Data Violates Owl Checks:\n" + returnString;
        }

        public List<string> GetClasses(bool fromData)
        {
            var datasource = fromData ? Data : Schema;
            return datasource.AllClasses.Select(c => NodeToQNameOrString(datasource, c.Resource)).ToList();
        }

        public List<string> GetAllProperties(bool fromData)
        {
            var datasource = fromData ? Data : Schema;
            return datasource.AllProperties.Select(c => NodeToQNameOrString(datasource, c.Resource)).ToList();
        }

        public List<string> GetOwlObjectProperties()
        {
            return Schema.OwlObjectProperties.Select(p => NodeToQNameOrString(Schema, p.Resource)).ToList();
        }

        public List<string> GetOwlDatatypeProperties()
        {
            return Schema.OwlDatatypeProperties.Select(p => NodeToQNameOrString(Schema, p.Resource)).ToList();
        }

        public List<string> GetOwlAnnotationProperties()
        {
            return Schema.OwlAnnotationProperties.Select(p => NodeToQNameOrString(Schema, p.Resource)).ToList();
        }

        public List<string> GetRDFProperties()
        {
            return Schema.RdfProperties.Select(p => NodeToQNameOrString(Schema, p.Resource)).ToList();
        }

        public List<string> GetObjectsOfType(string classType)
        {
            IUriNode classNode = Schema.GetUriNode(classType);
            OntologyClass classOnt = new OntologyClass(classNode, Schema);
            return Data.GetTriplesWithObject(classNode).Select(t => NodeToQNameOrString(Data, t.Subject)).ToList();
        }

        public List<string> GetDomainOfProperty(string prop)
        {
            IUriNode propertyNode = Schema.GetUriNode(prop);
            OntologyProperty property = new OntologyProperty(propertyNode, Schema);

            return property.Domains.Select(d => NodeToQNameOrString(Schema, d.Resource)).ToList();
        }

        public List<string> GetRangeOfProperty(string prop)
        {
            IUriNode propertyNode = Schema.GetUriNode(prop);
            OntologyProperty property = new OntologyProperty(propertyNode, Schema);

            return property.Ranges.Select(d => NodeToQNameOrString(Schema, d.Resource)).ToList();
        }

        public static bool IsValidSparqlQuery(string queryText, out string error)
        {
            try
            {
                SparqlQueryParser parser = new SparqlQueryParser();
                SparqlQuery query = parser.ParseFromString(queryText);
                error = "Valid SPARQL Query";
                return true;
            }
            catch (RdfParseException ex)
            {
                error = ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public string ExecuteQuery(string queryText, out bool wasSuccessful, bool fromData = true)
        {
            wasSuccessful = true;
            IGraph dataSource = fromData ? Data : Schema;
            try
            {
                SparqlQueryParser parser = new SparqlQueryParser();
                SparqlQuery query = parser.ParseFromString(queryText);
                var results = dataSource.ExecuteQuery(query);
                return SparqlResultToString(results);
            }
            catch (Exception ex)
            {
                wasSuccessful = false;
                return ex.Message;
            }
        }

        public static string SparqlResultToString(object result)
        {
            using (var sw = new System.IO.StringWriter())
            {
                switch (result)
                {
                    case SparqlResultSet rset:
                        new SparqlJsonWriter().Save(rset, sw);
                        break;

                    case IGraph graph:
                        new CompressingTurtleWriter().Save(graph, sw);
                        break;

                    default:
                        return result?.ToString() ?? string.Empty;
                }

                return sw.ToString();
            }
        }

        public List<Tuple<string, string, string>> TripleSearchOLD(List<string> sub, List<string> pre, List<string> obj, bool fromData = true)
        {
            IGraph dataSource = fromData ? Data : Schema;
            List<Tuple<string, string, string>> returnList = new List<Tuple<string, string, string>>();
            foreach (Triple triple in dataSource.Triples)
            {
                string subject = NodeToQNameOrString(dataSource, triple.Subject);
                string predicate = NodeToQNameOrString(dataSource, triple.Predicate);
                string objec = NodeToQNameOrString(dataSource, triple.Object);

                if ((sub == null || sub.Contains(subject)) && (pre == null || pre.Contains(predicate)) && (obj == null || obj.Contains(objec)))
                {
                    returnList.Add(new Tuple<string, string, string>(subject, predicate, objec));
                }
            }

            return returnList;
        }
        public List<Tuple<string, string, string>> TripleSearchSparql(List<string> sub, List<string> pre, List<string> obj, bool fromData = true)
        {
            IGraph dataSource = fromData ? Data : Schema;

            StringBuilder sb = new StringBuilder();
            // Include your prefixes so QNames resolve.
            foreach (var prefix in dataSource.NamespaceMap.Prefixes)
                sb.AppendLine($"PREFIX {prefix}: <{dataSource.NamespaceMap.GetNamespaceUri(prefix)}>");

            sb.AppendLine("SELECT ?s ?p ?o WHERE {");
            sb.AppendLine("  ?s ?p ?o .");
            if (sub != null) sb.AppendLine($"  VALUES ?s {{ {string.Join(" ", sub)} }}");
            if (pre != null) sb.AppendLine($"  VALUES ?p {{ {string.Join(" ", pre)} }}");
            if (obj != null) sb.AppendLine($"  VALUES ?o {{ {string.Join(" ", obj)} }}");
            sb.AppendLine("}");

            var results = (SparqlResultSet)dataSource.ExecuteQuery(sb.ToString());

            var returnList = new List<Tuple<string, string, string>>();
            foreach (SparqlResult r in results)
            {
                returnList.Add(Tuple.Create(
                    NodeToQNameOrString(dataSource, r["s"]),
                    NodeToQNameOrString(dataSource, r["p"]),
                    NodeToQNameOrString(dataSource, r["o"])));
            }
            return returnList;
        }
        public List<Tuple<string, string, string>> TripleSearch(List<string> sub, List<string> pre, List<string> obj, bool fromData = true)
        {
            lock (_sync)
            {
                // Turn a list of QName/URI/literal strings into a HashSet of INodes for O(1) lookups.
                HashSet<INode> ToNodes(IGraph g, List<string> values)
                {
                    if (values == null) return null;
                    var set = new HashSet<INode>();
                    foreach (var v in values)
                    {
                        var node = StringToNode(g, v);   // inverse of NodeToQNameOrString (see note below)
                        if (node != null) set.Add(node);
                    }
                    return set;
                }

                // Choose the filter that narrows results the most and query its index.
                IEnumerable<Triple> SelectCandidates(IGraph g, HashSet<INode> subNodesX, HashSet<INode> preNodesX, HashSet<INode> objNodesX)
                {
                    // No filters at all -> we have to return everything.
                    if (subNodesX == null && preNodesX == null && objNodesX == null)
                        return g.Triples;

                    // Subjects and objects are usually far more selective than predicates,
                    // so prefer them. Union the indexed lookups for each value in the list.
                    if (subNodesX != null)
                        return subNodesX.SelectMany(g.GetTriplesWithSubject);
                    if (objNodesX != null)
                        return objNodesX.SelectMany(g.GetTriplesWithObject);
                    return preNodesX.SelectMany(g.GetTriplesWithPredicate);
                }

                IGraph dataSource = fromData ? Data : Schema;

                // Convert the (small) filter lists into INodes ONCE, using the graph's
                // namespace map so QNames resolve the same way NodeToQNameOrString expects.
                var subNodes = ToNodes(dataSource, sub);
                var preNodes = ToNodes(dataSource, pre);
                var objNodes = ToNodes(dataSource, obj);

                // Gather candidate triples using indexes, picking the most selective filter.
                IEnumerable<Triple> candidates = SelectCandidates(dataSource, subNodes, preNodes, objNodes);

                var returnList = new List<Tuple<string, string, string>>();
                foreach (Triple triple in candidates)
                {
                    // Apply remaining filters by comparing INodes directly (no string conversion).
                    if ((subNodes == null || subNodes.Contains(triple.Subject)) &&
                        (preNodes == null || preNodes.Contains(triple.Predicate)) &&
                        (objNodes == null || objNodes.Contains(triple.Object)))
                    {
                        returnList.Add(Tuple.Create(NodeToQNameOrString(dataSource, triple.Subject), NodeToQNameOrString(dataSource, triple.Predicate), NodeToQNameOrString(dataSource, triple.Object)));
                    }
                }
                return returnList;
            }
        }

        public static INode StringToNode(IGraph graph, string value)
        {
            if (value == null) return null;

            if (value.StartsWith("_:"))
                return graph.CreateBlankNode(value.Substring(2));

            if (value.StartsWith("\""))
            {
                int langMarker = value.IndexOf("\"@", StringComparison.Ordinal);
                if (langMarker > 0)
                {
                    string lex = value.Substring(1, langMarker - 1);
                    string lang = value.Substring(langMarker + 2);
                    return graph.CreateLiteralNode(lex, lang);
                }

                int dtMarker = value.IndexOf("\"^^", StringComparison.Ordinal);
                if (dtMarker > -1)
                {
                    string lex = value.Substring(1, dtMarker - 1);
                    string fragment = value.Substring(dtMarker + 3);
                    Uri dtUri = UriFactory.Create(XmlSpecsHelper.NamespaceXmlSchema + fragment);
                    return graph.CreateLiteralNode(lex, dtUri);
                }

                return graph.CreateLiteralNode(value.Substring(1, value.Length - 2));
            }

            // --- URI or QName ---    if (value == "a")
            if (value == "a")
                return graph.CreateUriNode(
                    UriFactory.Create(RdfSpecsHelper.RdfType));

            int colon = value.IndexOf(':');
            if (colon > -1)
            {
                string prefix = value.Substring(0, colon);  // may be "" for default ns ":Foo"

                // Handles both named prefixes AND the default (empty) prefix.
                if (graph.NamespaceMap.HasNamespace(prefix))
                {
                    // Reconstruct the full URI ourselves and validate, rather than
                    // relying on CreateUriNode(qname) which can also throw on odd input.
                    string ns = graph.NamespaceMap.GetNamespaceUri(prefix).ToString();
                    string full = ns + value.Substring(colon + 1);
                    return graph.CreateUriNode(UriFactory.Create(full));
                }
            }

            // Bare full URI branch — validate BEFORE creating.
            if (Uri.IsWellFormedUriString(value, UriKind.Absolute))
                return graph.CreateUriNode(UriFactory.Create(value));

            try
            {
                return graph.CreateLiteralNode(value);
            }
            catch
            {

            }

            // Nothing matched: surface a helpful error instead of UriFormatException.
            throw new ArgumentException(
                $"StringToNode could not interpret '{value}' as a QName (no registered " +
                $"prefix '{(colon > -1 ? value.Substring(0, colon) : "")}'), a full URI, " +
                $"a literal, or a blank node in this graph.");
        }

        public static string NodeToQNameOrString(IGraph graph, INode node)
        {
            switch (node.NodeType)
            {
                case NodeType.Uri:
                    var uriNode = (IUriNode)node;
                    if (graph.NamespaceMap.ReduceToQName(uriNode.Uri.ToString(), out string qname))
                        return qname;
                    else
                        return uriNode.Uri.ToString();

                case NodeType.Literal:
                    var lit = (ILiteralNode)node;
                    return lit.DataType != null ? $"\"{lit.Value}\"^^{lit.DataType.Fragment.TrimStart('#')}" : $"\"{lit.Value}\"";

                case NodeType.Blank:
                    return "_:" + ((IBlankNode)node).InternalID;

                default:
                    return node.ToString();
            }
        }

        public void MinimizeGraph()
        {
            foreach (Triple t in Data.Triples.ToList())
            {
                Graph temp = new Graph();
                temp.Merge(Data);
                temp.Retract(t);

                Graph originalClosure = new Graph();
                originalClosure.Merge(Data);
                reasonerRdfs.Apply(originalClosure);
                reasonerN3.Apply(originalClosure);
                reasonerSkos.Apply(originalClosure);
                reasonerOwl.Apply(originalClosure);

                Graph tempClosure = new Graph();
                tempClosure.Merge(temp);
                reasonerRdfs.Apply(tempClosure);
                reasonerN3.Apply(tempClosure);
                reasonerSkos.Apply(tempClosure);
                reasonerOwl.Apply(tempClosure);

                if (tempClosure.Triples.Count == originalClosure.Triples.Count)
                    Data.Retract(t);
            }
        }

        public Dictionary<string, string> GetClassDictionaryFromSchema(string schema = null)
        {
            Dictionary<string, string> classDict = new Dictionary<string, string>();
            //List<string> classesA  = Schema.AllClasses.Select(c => NodeToQNameOrString(Schema, c.Resource)).ToList();
            List<string> classesB = TripleSearch(null, new List<string>() { "rdf:type", "a" }, new List<string>() { "rdfs:Class", "owl:Class" }, false).Select(t => t.Item1).Distinct().ToList();
            List<string> depricatedClasses = TripleSearch(null, new List<string>() { "owl:deprecated" }, new List<string>() { "\"true\"^^boolean", "\"true\"^^xsd:boolean", "\"true\"" }, false).Select(t => t.Item1).Distinct().ToList();
            foreach (string c in classesB)
            {
                if (schema != null && !c.StartsWith(schema + ":"))
                {
                    continue;
                }

                if (depricatedClasses.Contains(c))
                {
                    continue;
                }

                List<string> classComments = TripleSearch(new List<string>() { c }, new List<string>() { "rdfs:comment", "skos:definition" }, null, false).Select(t => t.Item3).Distinct().ToList();
                string classDescription = c + ": " + string.Join(" ", classComments.Where(v => !string.IsNullOrWhiteSpace(v)))
                                                           .Replace("^^langString", "").Replace("^^string", "")
                                                           .Replace("\"", "").Replace("\t", "").Replace("\n", " ").Replace("\r", " ");
                if (!string.IsNullOrWhiteSpace(classDescription))
                {
                    classDict[c] = classDescription;
                }
            }

            return classDict;
        }

        public string GetQueryResultAsTable(string resultString)
        {
            var result = SparqlResultFormatter.Parse(resultString, Data.NamespaceMap);
            List<List<string>> table = result.Item1;
            return result.Item2;
        }
    }

    public static class SparqlResultFormatter
    {
        public static Tuple<List<List<string>>, string> Parse(string json, INamespaceMapper namespaces = null)
        {
            int brace = json.IndexOf('{');
            if (brace > 0) json = json.Substring(brace);

            var table = new List<List<string>>();
            var root = JObject.Parse(json);

            var vars = root["head"]["vars"]
                .Select(v => v.ToString())
                .ToList();
            table.Add(new List<string>(vars));

            var bindings = root["results"] != null ? root["results"]["bindings"] : null;
            if (bindings != null)
            {
                foreach (var binding in bindings)
                {
                    var row = new List<string>(vars.Count);
                    foreach (var v in vars)
                    {
                        var cell = binding[v];
                        row.Add(cell != null ? FormatCell(cell, namespaces) : "");
                    }
                    table.Add(row);
                }
            }

            return Tuple.Create(table, ToAsciiTable(table));
        }

        private static string FormatCell(JToken cell, INamespaceMapper namespaces)
        {
            string type = cell["type"] != null ? cell["type"].ToString() : "";
            string value = cell["value"] != null ? cell["value"].ToString() : "";

            if (type == "uri" && namespaces != null &&
                namespaces.ReduceToQName(value, out string qname))
            {
                return qname;
            }

            return value;
        }

        private static string ToAsciiTable(List<List<string>> table)
        {
            if (table.Count == 0) return "";

            int cols = table.Max(r => r.Count);
            var widths = new int[cols];
            foreach (var row in table)
                for (int c = 0; c < row.Count; c++)
                    widths[c] = Math.Max(widths[c], (row[c] ?? "").Length);

            Func<string> separator = () =>
                "+" + string.Join("+", widths.Select(w => new string('-', w + 2))) + "+";

            Func<List<string>, string> format = row =>
            {
                var cells = new List<string>();
                for (int c = 0; c < cols; c++)
                {
                    string val = c < row.Count ? (row[c] ?? "") : "";
                    cells.Add(" " + val.PadRight(widths[c]) + " ");
                }
                return "|" + string.Join("|", cells) + "|";
            };

            var sb = new StringBuilder();
            sb.AppendLine(separator());
            sb.AppendLine(format(table[0]));
            sb.AppendLine(separator());
            foreach (var row in table.Skip(1))
                sb.AppendLine(format(row));
            if (table.Count > 1)
                sb.AppendLine(separator());

            return sb.ToString();
        }
    }

    public class OwlReasoner_CS : IOwlReasoner
    {
        private IGraph _ontology;

        public IEnumerable<OntologyResource> Unsatisfiable => throw new NotImplementedException();

        public void Initialise(IGraph ontology)
        {
            _ontology = ontology;
        }

        public void Apply(IGraph data)
        {
            if (_ontology == null) return;

            var rdfType = data.CreateUriNode("rdf:type");
            var owlEquivalentClass = data.CreateUriNode("owl:equivalentClass");
            var owlSameAs = data.CreateUriNode("owl:sameAs");
            var rdfsSubClassOf = data.CreateUriNode("rdfs:subClassOf");
            var rdfsSubPropertyOf = data.CreateUriNode("rdfs:subPropertyOf");
            var owlInverseOf = data.CreateUriNode("owl:inverseOf");
            var owlSymmetricProperty = data.CreateUriNode("owl:SymmetricProperty");
            var owlDisjointWith = data.CreateUriNode("owl:disjointWith");
            var owlPropertyDisjointWith = data.CreateUriNode("owl:propertyDisjointWith");

            var toAdd = new List<Triple>();

            // === Handle equivalentClass ===
            foreach (var eq in _ontology.GetTriplesWithPredicate(owlEquivalentClass))
            {
                var c1 = eq.Subject;
                var c2 = eq.Object;

                foreach (var t in data.GetTriplesWithPredicateObject(rdfType, c1))
                    toAdd.Add(new Triple(t.Subject, rdfType, c2));
                foreach (var t in data.GetTriplesWithPredicateObject(rdfType, c2))
                    toAdd.Add(new Triple(t.Subject, rdfType, c1));
            }

            // === Handle sameAs ===
            foreach (var eq in data.GetTriplesWithPredicate(owlSameAs))
            {
                var s = eq.Subject;
                var o = eq.Object;

                var aboutS = data.GetTriplesWithSubject(s).ToList();
                foreach (var t in aboutS)
                    toAdd.Add(new Triple(o, t.Predicate, t.Object));
            }

            // === Handle subClassOf ===
            foreach (var sub in _ontology.GetTriplesWithPredicate(rdfsSubClassOf))
            {
                var subC = sub.Subject;
                var superC = sub.Object;

                foreach (var inst in data.GetTriplesWithPredicateObject(rdfType, subC))
                    toAdd.Add(new Triple(inst.Subject, rdfType, superC));
            }

            // === Handle subPropertyOf ===
            foreach (var sub in _ontology.GetTriplesWithPredicate(rdfsSubPropertyOf))
            {
                var subP = sub.Subject;
                var superP = sub.Object;

                foreach (var t in data.GetTriplesWithPredicate(subP))
                    toAdd.Add(new Triple(t.Subject, superP, t.Object));
            }

            // === Handle inverseOf ===
            foreach (var inv in _ontology.GetTriplesWithPredicate(owlInverseOf))
            {
                var p1 = inv.Subject;
                var p2 = inv.Object;

                foreach (var t in data.GetTriplesWithPredicate(p1))
                    toAdd.Add(new Triple(t.Object, p2, t.Subject));
            }

            // === Handle SymmetricProperty ===
            foreach (var sp in _ontology.GetTriplesWithPredicate(rdfType)
                     .Where(t => t.Object.Equals(owlSymmetricProperty)))
            {
                var prop = sp.Subject;
                foreach (var t in data.GetTriplesWithPredicate(prop))
                    toAdd.Add(new Triple(t.Object, prop, t.Subject));
            }

            data.Assert(toAdd);
        }

        public IEnumerable<Triple> GetInferredTriples(IGraph data) => data.Triples;

        public void Add(IGraph g)
        {
            throw new NotImplementedException();
        }

        public IGraph Extract(IGraph g, INode n)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<Triple> Extract(string mode)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<Triple> Extract(IEnumerable<string> modes)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<Triple> Classify()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<Triple> Realize()
        {
            throw new NotImplementedException();
        }

        public bool IsConsistent()
        {
            throw new NotImplementedException();
        }

        public bool IsConsistent(IGraph g)
        {
            throw new NotImplementedException();
        }
    }
}