using AngleSharp.Html.Dom;
using Azure;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace IFCtoRDF
{
    public static class SemanticEnrichment
    {
        public static int AINumber = 3;
        public static string baseURL = "http://127.0.0.1:8000";
        public static string generateURL = $"{baseURL}/generate";
        public static string rdfURL = $"{baseURL}/rdf";
        public static string sparqlURL = $"{baseURL}/sparql";
        public static string similarityURL = $"{baseURL}/similarity";
        public static string dictionarySimilarityURL = $"{baseURL}/dictionary_similarity";
        public static string capacityURL = $"{baseURL}/capacity/simple";

        public static string openAI_URL = "https://api.openai.com/v1/responses";
        public static string openAI_model = "gpt-6-luna";
        public static string OPEN_API_KEY;

        public static string anthropic_URL = "https://api.anthropic.com/v1/messages";
        public static string anthropic_model = "claude-haiku-5-5";
        public static string ANTHROPIC_API_KEY;

        public static int MAX_OUT_TOKENS = 4096;

        public static string MODEL;

        public static HttpClient client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        private static readonly Random jitterRng = new Random();

        public static string SendPostRequest(string model, string jsonPayload, string url)
        {
            switch (model)
            {
                case "ChatGPT": return SendOpenAIRequest(jsonPayload);
                case "Claude": return SendClaudeRequest(jsonPayload);
                case "PortableLLM":
                    using (var response = SendWithRetry(() => new HttpRequestMessage(HttpMethod.Post, url)
                    {
                        Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                    }))
                    {
                        string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        if (!response.IsSuccessStatusCode)
                            throw new HttpRequestException($"Local service returned {(int)response.StatusCode}: {body}");
                        return body;
                    }
                default:
                    throw new InvalidOperationException("Unknown model: " + model);
            }
        }

        private static void ReadPrompt(string jsonPayload, out string prompt, out string instructions)
        {
            using (var doc = JsonDocument.Parse(jsonPayload))
            {
                var r = doc.RootElement;
                prompt = r.TryGetProperty("prompt", out var p) ? p.GetString() ?? "" : "";
                instructions = r.TryGetProperty("prompt_information", out var i) ? i.GetString() ?? "" : "";
            }
        }

        public static string SendOpenAIRequest(string jsonPayload)
        {
            ReadPrompt(jsonPayload, out string prompt, out string instructions);
            string body = JsonSerializer.Serialize(new { model = openAI_model, instructions, input = prompt, max_output_tokens = MAX_OUT_TOKENS });

            var sw = Stopwatch.StartNew();
            using (var response = SendWithRetry(() =>
            {
                var req = new HttpRequestMessage(HttpMethod.Post, openAI_URL);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", OPEN_API_KEY);
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                return req;
            }))
            {
                string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"OpenAI returned {(int)response.StatusCode}: {json}");

                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    var text = new StringBuilder();
                    foreach (var item in root.GetProperty("output").EnumerateArray())
                    {
                        if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
                        foreach (var part in content.EnumerateArray())
                            if (part.TryGetProperty("type", out var t) && t.GetString() == "output_text" && part.TryGetProperty("text", out var v))
                                text.Append(v.GetString());
                    }
                    if (root.TryGetProperty("status", out var s) && s.GetString() == "incomplete")
                        Debug.Print("OpenAI response incomplete (hit MAX_OUT_TOKENS).");
                    return Envelope(text.ToString(), root, sw.Elapsed.TotalSeconds);
                }
            }
        }

        public static string SendClaudeRequest(string jsonPayload)
        {
            ReadPrompt(jsonPayload, out string prompt, out string instructions);
            string body = JsonSerializer.Serialize(new
            {
                model = anthropic_model,
                max_tokens = MAX_OUT_TOKENS,
                system = instructions,
                messages = new[] { new { role = "user", content = prompt } }
            });

            var sw = Stopwatch.StartNew();
            using (var response = SendWithRetry(() =>
            {
                var req = new HttpRequestMessage(HttpMethod.Post, anthropic_URL);
                req.Headers.Add("x-api-key", ANTHROPIC_API_KEY);
                req.Headers.Add("anthropic-version", "2023-06-01");
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                return req;
            }))
            {
                string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"Claude returned {(int)response.StatusCode}: {json}");

                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    var text = new StringBuilder();
                    foreach (var block in root.GetProperty("content").EnumerateArray())
                        if (block.TryGetProperty("type", out var t) && t.GetString() == "text" && block.TryGetProperty("text", out var v))
                            text.Append(v.GetString());
                    if (root.TryGetProperty("stop_reason", out var s) && s.GetString() == "max_tokens")
                        Debug.Print("Claude response truncated (hit MAX_OUT_TOKENS).");
                    return Envelope(text.ToString(), root, sw.Elapsed.TotalSeconds);
                }
            }
        }

        private static string Envelope(string text, JsonElement root, double seconds)
        {
            int inTok = 0, outTok = 0;
            if (root.TryGetProperty("usage", out var u))
            {
                if (u.TryGetProperty("input_tokens", out var i)) inTok = i.GetInt32();
                if (u.TryGetProperty("output_tokens", out var o)) outTok = o.GetInt32();
            }
            return JsonSerializer.Serialize(new
            {
                response = text,
                prompt_tokens = inTok,
                response_tokens = outTok,
                total_tokens = inTok + outTok,
                inference_time_seconds = seconds
            });
        }

        private static HttpResponseMessage SendWithRetry(Func<HttpRequestMessage> makeRequest, int maxRetries = 5)
        {
            TimeSpan Backoff(int attempt)
            {
                int jitter;
                lock (jitterRng) jitter = jitterRng.Next(0, 1000);
                return TimeSpan.FromMilliseconds(Math.Min(30000, 1000 * Math.Pow(2, attempt)) + jitter);
            }

            for (int attempt = 0; ; attempt++)
            {
                HttpResponseMessage response = null;
                try
                {
                    using (var req = makeRequest())
                        response = client.SendAsync(req).GetAwaiter().GetResult();
                }
                catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException) && attempt < maxRetries) { }

                if (response != null)
                {
                    int code = (int)response.StatusCode;
                    if (!(code == 429 || code == 529 || code >= 500) || attempt >= maxRetries)
                        return response;
                    var wait = response.Headers.RetryAfter?.Delta ?? Backoff(attempt);
                    response.Dispose();
                    Thread.Sleep(wait);
                }
                else Thread.Sleep(Backoff(attempt));
            }
        }

        public static int GetParallelCapacity()
        {
            try
            {
                using (HttpClient CapacityHttp = new HttpClient())
                {
                    using (var response = CapacityHttp.GetAsync("http://127.0.0.1:8000/capacity/simple").Result)
                    {
                        using (var json = JsonDocument.Parse(response.Content.ReadAsStringAsync().Result))
                        {
                            return Math.Max(1, json.RootElement.GetProperty("max_parallel_requests").GetInt32());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Print("Could not read service capacity; using one worker. " + ex);
                return 1;
            }
        }

        public static string StripCodeFences(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return s;

            s = s.Trim();

            if (s.StartsWith("```"))
            {
                // Remove the opening fence line (```
                int firstNewline = s.IndexOf('\n');
                if (firstNewline >= 0)
                    s = s.Substring(firstNewline + 1);

                // Remove the trailing closing fence.
                int lastFence = s.LastIndexOf("```", StringComparison.Ordinal);
                if (lastFence >= 0)
                    s = s.Substring(0, lastFence);
            }

            return s.Trim();
        }

        public static bool TryParseLiteralDouble(string literal, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(literal)) return false;
            string s = literal;
            int dt = s.IndexOf("\"^^", StringComparison.Ordinal);
            if (dt > 0) s = s.Substring(0, dt + 1);
            s = s.Trim('"');
            return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        public static Dictionary<string, string> GetClassDictionary(BIMGraph graph, string classType)
        {
            Dictionary<string, string> constructionTaskDef = new Dictionary<string, string>();
            var constructionTasks = graph.TripleSearch(null, new List<string>() { "a", "rdf:type" }, new List<string>() { classType });
            foreach (var cTask in constructionTasks)
            {
                var ctInfos = graph.TripleSearch(new List<string>() { cTask.Item1 }, new List<string>() { "rdfs:label", "prop:description" }, null);
                string ctDescription = cTask.Item1 + " = " + string.Join(". ", ctInfos.Select(mi => mi.Item3).Where(v => !string.IsNullOrWhiteSpace(v)))
                                                           .Replace("^^langString", "").Replace("^^string", "").Replace("\"", "").Replace("\t", "").Replace("\n", " ").Replace("\r", " ");
                constructionTaskDef.Add(cTask.Item1, ctDescription);
            }
            return constructionTaskDef;
        }

        public static bool GetMaterialECValueInVolume(string result, out double resultValue, out string resultLogic)
        {
            resultValue = 0;
            resultLogic = null;
            bool parsedOk = false;
            try
            {
                using (JsonDocument outerDoc = JsonDocument.Parse(result))
                {
                    JsonElement outerRoot = outerDoc.RootElement;

                    // The "response" property is a STRING that contains JSON, so extract it first.
                    if (!outerRoot.TryGetProperty("response", out JsonElement responseEl) || responseEl.ValueKind != JsonValueKind.String)
                    {
                        throw new InvalidOperationException("Envelope is missing a string 'response' property.");
                    }

                    string innerJson = responseEl.GetString();

                    // The model sometimes wraps its JSON in ```json ... ``` fences — strip them.
                    innerJson = StripCodeFences(innerJson);

                    // ...then parse that string as a second JSON document.
                    using (JsonDocument innerDoc = JsonDocument.Parse(innerJson))
                    {
                        JsonElement root = innerDoc.RootElement;

                        // "logic": tolerate it being absent.
                        if (root.TryGetProperty("logic", out JsonElement logicEl) && logicEl.ValueKind == JsonValueKind.String)
                        {
                            resultLogic = logicEl.GetString();
                        }

                        // "value": accept it as a number OR a quoted string.
                        if (root.TryGetProperty("value", out JsonElement valueEl))
                        {
                            switch (valueEl.ValueKind)
                            {
                                case JsonValueKind.Number:
                                    resultValue = valueEl.GetDouble();
                                    parsedOk = true;
                                    break;
                                case JsonValueKind.String:
                                    parsedOk = double.TryParse(valueEl.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out resultValue);
                                    break;
                            }
                        }
                    }
                }

                if (!parsedOk)
                {
                    Console.WriteLine("Warning: could not read a numeric 'value' from the response.");
                }
                else
                {
                    Console.WriteLine($"Value: {resultValue.ToString(CultureInfo.InvariantCulture)}");
                    Console.WriteLine($"Logic: {resultLogic ?? "(none provided)"}");
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Failed to parse JSON response: {ex.Message}");
                Console.WriteLine($"Raw result was: {result}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error handling response: {ex.Message}");
            }

            return parsedOk;
        }

        public static Tuple<string, string, string> MaterialECApproximate(BIMGraph graph, Tuple<string, string, string> materialTriple)
        {
            var matInfos = graph.TripleSearch(new List<string>() { materialTriple.Item1 }, new List<string>() { "rdfs:label" }, null);

            // Add a triple to make the material's carbon value based on volume
            var matTrips = graph.TripleSearch(new List<string>() { materialTriple.Item1 }, null, null);
            var unitTrips = graph.TripleSearch(matTrips.Where(t => t.Item2 == "ice:hasUnit").Select(t => t.Item3).ToList(), null, null);
            var request = new
            {
                prompt = string.Join("\n", matTrips.Concat(unitTrips).Select(t => string.Join(" ", t))),
                prompt_information = "You are an expert in estimating the embodied carbon of construction materials from data provided in RDF/Turtle format. " +
                "Your task is to return a single best-estimate embodied carbon value expressed in kgCO2e per cubic metre (kgCO2e/m³) for the given material.\n\n" +
                "FIRST, check whether the material actually has usable carbon data:\n- Look at ice:hasRecordStatus and ice:embodiedCarbonRaw / ice:embodiedCarbonValue.\n" +
                "- If ice:hasRecordStatus is 'missing', 'removed', or 'no-gwp-data', OR embodiedCarbonRaw is empty or explicitly states the data was removed/unavailable, " +
                "do NOT invent a number. Instead return value: null and explain why in logic.\n- Otherwise, proceed to estimate a value. Do not refuse for any other reason " +
                "— if data is incomplete but a value exists, make a reasonable educated guess using your own knowledge of typical materials.\n\n" +
                "The input describes a material and its embodied carbon, usually as a value per kg, per m³, or per unit (check ice:hasUnit, and use any resolved " +
                "category/subcategory labels for context, e.g. 'per 1m2 at 100mm thickness').\n\nWork through these cases in order and use the FIRST one that applies:\n" +
                "1. If the value is already per m³, use it directly.\n2. If the value is per kg or per tonne, estimate the material's density in kg/m³ from any mix design, " +
                "composition, or standard reference you can infer, then multiply: (embodied carbon per kg) × (density in kg/m³).\n3. If the value is per unit, per item, " +
                "per m², or the unit is unclear, use any subcategory/category context (e.g. stated thickness) to convert to per m³. If thickness or area basis is given, " +
                "divide/multiply accordingly rather than guessing blindly.\n4. Always prefer information present in the input over generic assumptions, but use generic " +
                "knowledge to fill any gaps.\n\nSANITY CHECK before finalising:\n- Ask whether your kgCO2e/m³ result is physically realistic for this material. " +
                "Most construction materials fall between ~50 and ~25,000 kgCO2e/m³. If your result is far outside a sensible range for the material, reconsider your " +
                "interpretation of the input unit or density before finalising.\n\nCRITICAL RULES:\n- The \"value\" field MUST be exactly equal to the final number produced " +
                "by the arithmetic shown in your \"logic\". Do NOT round differently, rescale, or substitute a different number. Recompute the multiplication digit-by-digit " +
                "to be sure it is correct.\n- Do NOT confuse the per-kg input value with the final per-m³ result.\n- If you determined no value should be produced " +
                "(per the data-availability check above), set \"value\" to null and do not guess.\n\nReturn ONLY a JSON object with exactly two fields, and do NOT wrap " +
                "it in code fences:\n- \"logic\": a string stating the unit found (or assumed), the density used (if any), the explicit calculation (if any), and either " +
                "a note that the result passed the sanity check, or the reason no value was produced.\n- \"value\": a number (no units, no quotes) equal to the final kgCO2e/m³ " +
                "figure, or the JSON literal null if no value should be produced.\n",
                model = AINumber.ToString()
            };

            string result = SendPostRequest(MODEL, JsonSerializer.Serialize(request), generateURL);

            try
            {
                bool parsedOk = GetMaterialECValueInVolume(result, out double resultValue, out string resultLogic);

                if (!parsedOk)
                {
                    return null;
                }

                var existingValueTrips = graph.TripleSearch(new List<string>() { materialTriple.Item1 }, new List<string>() { "prop:kgCO2ePerM3" }, null);
                foreach (var t in existingValueTrips)
                {
                    graph.RemoveMatchingTriple(t.Item1, t.Item2, t.Item3);
                }

                return new Tuple<string, string, string>(materialTriple.Item1, "prop:kgCO2ePerM3", resultValue.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                Debug.Print("Error processing material " + materialTriple.Item1 + ": " + ex.Message);
                return null;
            }
        }

        public static string GetObjectDescriptionRaw(string objID, BIMGraph graph)
        {
            var triples = graph.TripleSearch(new List<string>() { objID }, null, null);
            List<string> propList = new List<string>();
            foreach (var t in triples)
            {
                propList.Add(t.Item2 + ": " + t.Item3);
            }
            string result = "This is an object or thing represented in RDF that is relevent to construciton in some way (could be a building element, construciton task, material, or anytihng construction related). It has the following properties:\n" + string.Join("\n", propList);
            return result.Replace("\"", "");
        }

        public static async Task<string> DescribeObjectAI(string description)
        {
            var request = new
            {
                prompt = description,
                prompt_information = "You are a helpful assistant who can take messy, unclear information and explain it in a more clear and understandableway. Please provide a natural language description and a best guess classification of the object based on the information provided.",
                model = AINumber.ToString()
            };
            string result = SendPostRequest(MODEL, JsonSerializer.Serialize(request), generateURL);
            using (JsonDocument doc = JsonDocument.Parse(result))
            {
                JsonElement root = doc.RootElement;
                string response = root.GetProperty("response").GetString();
                return response.Replace("\n", " ");
            }
        }

        public static Tuple<string, string, string> GetElementDesciption(string elem, BIMGraph graph)
        {
            var elemInfos = graph.TripleSearch(new List<string>() { elem }, null, null).Select(t => t.Item3).ToList();
            string description = "Here is the element information of " + elem + ": " + string.Join("\n", elemInfos) + "\n\nPlease provide a detailed natural language description of the element based on the information provided.";

            // remove any existing description triples for this element
            var existingDescTrips = graph.TripleSearch(new List<string>() { elem }, new List<string>() { "prop:description" }, null);
            foreach (var descTrip in existingDescTrips)
            {
                graph.RemoveMatchingTriple(descTrip.Item1, descTrip.Item2, descTrip.Item3);
            }
            string response = DescribeObjectAI(description).Result;
            return new Tuple<string, string, string>(elem, "prop:description", response.Replace("\n", " "));
        }

        public static void ElementClassification(BIMGraph graph, string objID, Dictionary<string, string> classDictFiltered, double threshold)
        {
            var request = new
            {
                sentence = GetObjectDescriptionRaw(objID, graph),
                dictionary = classDictFiltered,
                model = AINumber.ToString()
            };

            string result1 = SendPostRequest("PortableLLM", JsonSerializer.Serialize(request), dictionarySimilarityURL);
            using (JsonDocument doc = JsonDocument.Parse(result1))
            {
                JsonElement root = doc.RootElement;
                Dictionary<string, double> resultValues = doc.RootElement.GetProperty("scores").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble());
                if (resultValues.Count == 0)
                    return;

                var topResult = resultValues.OrderByDescending(kvp => kvp.Value).First();
                if (topResult.Value >= threshold)
                {
                    string[] resultSplit = topResult.Key.Split(new char[] { ':' }, 2, StringSplitOptions.None);

                    string prefix, className;
                    if (resultSplit.Length == 2)
                    {
                        prefix = resultSplit[0];
                        className = resultSplit[1];
                    }
                    else if (resultSplit.Length == 1)
                    {
                        prefix = "";
                        className = resultSplit[0];
                    }
                    else
                    {
                        return;
                    }

                    graph.AppendTripple(objID, "prop:guessedType", topResult.Key);
                }
            }
        }

        public static PromptResult ElementDetailGenerator(BIMGraph graph, string elem)
        {
            PromptResult promptResult = new PromptResult()
            {
                success = false,
                prompt_token = 0,
                response_token = 0,
                total_token = 0,
                inference_time = 0,
            };

            List<Tuple<string, string, string>> deleteTriples = new List<Tuple<string, string, string>>();
            List<Tuple<string, string, string>> addTriples = new List<Tuple<string, string, string>>();
            var elemInfos = graph.TripleSearch(new List<string>() { elem }, null, null).Select(t => t.Item2 + " " + t.Item3).ToList(); //new List<string>() { "prop:description" }
            var request = new
            {
                prompt = "Here is the element information of " + elem + ": " + string.Join("\n", elemInfos) + "\n\nPlease provide a list of construction tasks that need to be done in order to build this element.",
                prompt_information =
                    // --- Role ---
                    "You are an experienced construction planner and site engineer. " +
                    "You will be given the RDF/IFC data for a single building element. " +

                    // --- Task ---
                    "Your job is to produce three things:\n" +
                    "1. A short, plain-language description of what the element physically is.\n" +
                    "2. Your best guess at the element's primary material, inferred from everything " +
                    "in the data (IFC type, naming, dimensions, context). Give a material name or a short description.\n" +
                    "3. An ordered list of the real-world, on-site construction tasks a crew would " +
                    "perform to physically build and install this element.\n\n" +

                    // --- How to think about tasks ---
                    "Think in terms of actual field activities in their natural sequence: site preparation, " +
                    "setting out/layout, formwork or framing, positioning and fixing, fastening or connections, " +
                    "sealing/finishing, and inspection. Each task must be a concrete, actionable physical activity " +
                    "that a worker could carry out on site, ordered logically from first to last.\n"+
                    //"Here is an example of a task from the task catalogue:\n" +
                    //"taskName: On-Site Welding.\n" +
                    //"taskDescription \"This task, On-Site Welding, is the process of welding the on-site. "+
                    //"The work is set out from the drawings, the required materials and components are prepared and positioned, then fixed or assembled in "+
                    //"the correct sequence, and the finished result is checked to make sure it meets the specification.\n" +
                    "\n" +

                    // --- What to ignore ---
                    "IMPORTANT: The input contains technical metadata (IFC type names, GUIDs, RDF tags such as " +
                    "'ifc:...' or 'bot:...', coordinate tuples, and internal identifiers). These are NOT construction tasks. " +
                    "Do NOT create tasks that merely restate a data field, such as 'assign ID', 'assign type', " +
                    "or 'define element type'. Instead, interpret what the element actually is in the physical world " +
                    "(e.g. an opening in a wall, a door, a window, a beam) and describe the labor, materials, tools, " +
                    "and steps needed to construct or install it.\n\n" +

                    // --- Output contract ---
                    "Return ONLY a valid and compact JSON object, with no surrounding text, explanation, or markdown. " +
                    "Use exactly this schema:\n" +
                    "{\n" +
                    "  \"description\": \"<string>\",\n" +
                    "  \"material\": \"<string>\",\n" +
                    "  \"tasks\": [\n" +
                    "    { \"taskName\": \"<short action title>\", \"taskDescription\": \"<one or two sentences of physical work>\" }\n" +
                    "  ]\n" +
                    "}\n" +
                    "Every task object must contain exactly the two string fields \"taskName\" and \"taskDescription\". " +
                    "Strictly follow this format.",
                model = AINumber.ToString()
            };

            // remove any existing description triples for this element
            var existingDescTrips = graph.TripleSearch(new List<string>() { elem }, new List<string>() { "prop:guessedDescription", "prop:guessedMaterial" }, null);
            foreach (var descTrip in existingDescTrips)
            {
                //graph.RemoveMatchingTriple(descTrip.Item1, descTrip.Item2, descTrip.Item3);
                deleteTriples.Add(descTrip);
            }

            // Remove the guessed task and all its related triples from the graph
            var guessedTasks = graph.TripleSearch(new List<string>() { elem }, new List<string>() { "prop:guessedTask" }, null).Select(t => t.Item3).ToList();
            foreach (var gTask in guessedTasks)
            {
                //graph.RemoveMatchingTriple(elem, "prop:guessedTask", gTask);
                deleteTriples.Add(Tuple.Create(elem, "prop:guessedTask", gTask));
                foreach (var t in graph.TripleSearch(new List<string>() { gTask }, null, null))
                {
                    //graph.RemoveMatchingTriple(t.Item1, t.Item2, t.Item3);
                    deleteTriples.Add(t);
                }
            }

            Dictionary<string, string> plannedTasks = new Dictionary<string, string>();
            string description = null;
            string material = null;
            try
            {
                string result = SendPostRequest(MODEL, JsonSerializer.Serialize(request), generateURL);

                using (JsonDocument doc = JsonDocument.Parse(result))
                {
                    JsonElement root = doc.RootElement;
                    string response = root.GetProperty("response").GetString();

                    // The LLM should return a JSON object. Extract it defensively in case
                    // the model wraps it in markdown fences or stray prose.
                    int start = response.IndexOf('{');
                    int end = response.LastIndexOf('}');
                    if (start < 0 || end <= start)
                        throw new FormatException("No JSON object found in the LLM response.");

                    string objectJson = response.Substring(start, end - start + 1);

                    using (JsonDocument resultDoc = JsonDocument.Parse(objectJson))
                    {
                        JsonElement resultRoot = resultDoc.RootElement;
                        if (resultRoot.ValueKind != JsonValueKind.Object)
                            throw new FormatException("Expected a JSON object from the LLM.");

                        // --- Description ---
                        if (resultRoot.TryGetProperty("description", out JsonElement descElem))
                            description = descElem.GetString();

                        // --- Material ---
                        if (resultRoot.TryGetProperty("material", out JsonElement matElem))
                            material = matElem.GetString();

                        // --- Tasks ---
                        if (!resultRoot.TryGetProperty("tasks", out JsonElement tasksRoot))
                            throw new FormatException("No 'tasks' property found in the LLM response.");

                        if (tasksRoot.ValueKind != JsonValueKind.Array)
                            throw new FormatException("Expected 'tasks' to be a JSON array.");

                        foreach (JsonElement taskElem in tasksRoot.EnumerateArray())
                        {
                            string name = null;
                            string taskDescription = null;

                            if (taskElem.TryGetProperty("taskName", out JsonElement nameProp))
                                name = nameProp.GetString();

                            if (taskElem.TryGetProperty("taskDescription", out JsonElement descProp))
                                taskDescription = descProp.GetString();

                            if (string.IsNullOrWhiteSpace(name))
                                continue;

                            // Handle duplicate task names gracefully so nothing is silently lost.
                            string key = name;
                            int dupCount = 1;
                            while (plannedTasks.ContainsKey(key))
                                key = $"{name} ({++dupCount})";

                            plannedTasks[key] = taskDescription ?? string.Empty;
                        }
                    }

                    promptResult.success = true;
                    promptResult.prompt_token = root.GetProperty("prompt_tokens").GetInt32();
                    promptResult.response_token = root.GetProperty("response_tokens").GetInt32();
                    promptResult.total_token = root.GetProperty("total_tokens").GetInt32();
                    promptResult.inference_time = root.GetProperty("inference_time_seconds").GetDouble();
                }
            }
            catch (Exception ex)
            {
                Debug.Print("Error processing element " + elem + ": " + ex.Message);
                return promptResult;
            }

            List<Tuple<string, string, string>> newTriples = new List<Tuple<string, string, string>>();
            foreach (var task in plannedTasks)
            {
                var taskID = "prop:Task_" + Guid.NewGuid().ToString();
                newTriples.Add(Tuple.Create(taskID, "a", "prop:GuessedTask"));
                newTriples.Add(Tuple.Create(taskID, "rdfs:label", task.Key));
                newTriples.Add(Tuple.Create(taskID, "prop:description", task.Value));
                newTriples.Add(Tuple.Create(elem, "prop:guessedTask", taskID));
            }
            newTriples.Add(Tuple.Create(elem, "prop:guessedDescription", description));
            newTriples.Add(Tuple.Create(elem, "prop:guessedMaterial", material));

            //foreach (var elementDescription in newTriples)
            //{
            //    graph.AppendTripple(elementDescription.Item1, elementDescription.Item2, elementDescription.Item3);
            //}
            addTriples.AddRange(newTriples);


            foreach (var trip in deleteTriples) { graph.RemoveMatchingTriple(trip.Item1, trip.Item2, trip.Item3); }
            foreach (var trip in addTriples) { graph.AppendTripple(trip.Item1, trip.Item2, trip.Item3); }

            return promptResult;
        }

        public static void FindClosestMatchingMaterial(BIMGraph graph, string elem, Dictionary<string, string> materialDef, double threshold, string materialRel)
        {
            List<Tuple<string, string, string>> addTrips = new List<Tuple<string, string, string>>();

            var materialAssosiations = graph.TripleSearch(new List<string>() { elem }, new List<string>() { materialRel }, null).Select(t => t.Item3).ToList();
            if (materialAssosiations.Count == 0)
            {
                Debug.Print("Warning: No material associations found for element " + elem);
                return;
            }

            // Find the best match for each material based on the similarty of the material property strings and the ICE:mat rep. string
            var request = new
            {
                sentence = string.Join(";", materialAssosiations).Replace("\"", ""),
                dictionary = materialDef,
                model = AINumber.ToString()
            };

            string result = SendPostRequest("PortableLLM", JsonSerializer.Serialize(request), dictionarySimilarityURL);
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(result))
                {
                    JsonElement root = doc.RootElement;

                    Dictionary<string, double> resultValues = doc.RootElement.GetProperty("scores").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble());
                    if (resultValues.Count == 0) 
                        return;

                    var topResult = resultValues.OrderByDescending(kvp => kvp.Value).First();

                    if (topResult.Value >= threshold)
                    {
                        // Make sure it is a real material in the graph before adding the triple
                        if (graph.TripleSearch(new List<string>() { topResult.Key }, null, null).Count > 0)
                        {
                            //graph.AppendTripple(elem, "prop:materialICE", topResult.Key);
                            addTrips.Add(Tuple.Create(elem, "prop:materialICE", topResult.Key));
                        }
                        else
                        {
                            Debug.Print("Warning: top result " + topResult.Key + " for element " + elem + " is not a valid material in the graph.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Print("Error processing element " + elem + ": " + ex.Message);
            }

            foreach (var trip in addTrips) { graph.AppendTripple(trip.Item1, trip.Item2, trip.Item3); }
        }

        public static void FindClosestMatchingRealTasks(BIMGraph graph, string elem, Dictionary<string, string> constructionTaskDef, double threshold)
        {
            List<Tuple<string, string, string>> deleteTriples = new List<Tuple<string, string, string>>();
            List<Tuple<string, string, string>> addTriples = new List<Tuple<string, string, string>>();

            // Get all guessed tasks for this element
            var guessedTasks = graph.TripleSearch(new List<string>() { elem }, new List<string>() { "prop:guessedTask" }, null).Select(t => t.Item3).ToList();
            if (guessedTasks.Count == 0)
            {
                Debug.Print("Warning: No planned tasks found for element " + elem);
                return;
            }

            List<Tuple<string, string, string>> newTripleList = new List<Tuple<string, string, string>>();
            foreach (var gTask in guessedTasks)
            {
                try
                {
                    string guessedTaskDescription = graph.TripleSearch(new List<string>() { gTask }, new List<string>() { "prop:description" }, null).Select(t => t.Item3).FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(guessedTaskDescription)) continue;

                    // Find the best match for each task based on the similarty of the task property strings
                    var request = new
                    {
                        sentence = guessedTaskDescription.Replace("\"", ""),
                        dictionary = constructionTaskDef,
                        model = AINumber.ToString()
                    };

                    string result = SendPostRequest("PortableLLM", JsonSerializer.Serialize(request), dictionarySimilarityURL);
                    using (JsonDocument doc = JsonDocument.Parse(result))
                    {
                        JsonElement root = doc.RootElement;

                        Dictionary<string, double> resultValues = doc.RootElement.GetProperty("scores").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble());
                        if (resultValues.Count == 0) continue;

                        var topResult = resultValues.OrderByDescending(kvp => kvp.Value).First();

                        if (topResult.Value >= threshold)
                        {
                            // Make sure it is a real material in the graph before adding the triple
                            if (graph.TripleSearch(new List<string>() { topResult.Key }, null, null).Count > 0)
                            {
                                string activityID = "act:" + Guid.NewGuid().ToString();
                                newTripleList.Add(Tuple.Create(activityID, "a", "act:ConstructionActivity"));
                                newTripleList.Add(Tuple.Create(activityID, "prop:doingTask", topResult.Key));
                                newTripleList.Add(Tuple.Create(activityID, "prop:forElement", elem));

                                // Remove the guessed task and all its related triples from the graph
                                foreach (var t in graph.TripleSearch(new List<string>() { gTask }, null, null))
                                {
                                    //graph.RemoveMatchingTriple(t.Item1, t.Item2, t.Item3);
                                    deleteTriples.Add(t);
                                }
                                //graph.RemoveMatchingTriple(elem, "prop:guessedTask", gTask);
                                deleteTriples.Add(Tuple.Create(elem, "prop:guessedTask", gTask));
                            }
                            else
                            {
                                Debug.Print("Warning: top result " + topResult.Key + " for element " + elem + " is not a valid construction task in the graph.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.Print("Error processing guessed task " + gTask + " for element " + elem + ": " + ex.Message);
                }
            }

            //foreach (var elementDescription in newTripleList)
            //{
            //    graph.AppendTripple(elementDescription.Item1, elementDescription.Item2, elementDescription.Item3);
            //}

            addTriples.AddRange(newTripleList);

            //foreach (var task in constructionTasks)
            //{
            //    var elemInfos = CurrentGraph.TripleSearch(new List<string>() { elem }, null, null).Select(t => t.Item3).ToList();
            //    var taskInfos = CurrentGraph.TripleSearch(new List<string>() { task }, null, null).Select(t => t.Item3).ToList();

            //    var request = new
            //    {
            //        prompt = "Here is the element information of " + elem + ": " + string.Join("\n", elemInfos) + "\n\nHere is the task information of " + task + ": " + string.Join("\n", taskInfos) + "\n\nDoes the element " + elem + " need the construction task " + task + "? Answer yes or no.",
            //        prompt_information = "You are an expert construction scheduler. You know about what kind of tasks are required for different types of building elements. You are given a building element and its related information and a construction task and its related information. Your task is to determine whether the construction task is required and relevant for this particular building element. Answer only 'yes' or 'no'.",
            //        model = AINumber.ToString()
            //    };

            //    string result = SendPostRequest(generateURL, JsonSerializer.Serialize(request));
            //    using (JsonDocument doc = JsonDocument.Parse(result))
            //    {
            //        JsonElement root = doc.RootElement;
            //        string response = root.GetProperty("response").GetString();
            //        if (response.ToLower().Contains("yes"))
            //        {
            //            CurrentGraph.AppendTripple(elem, "prop:requiresTask", task);
            //        }
            //    }
            //}

            foreach (var trip in deleteTriples) { graph.RemoveMatchingTriple(trip.Item1, trip.Item2, trip.Item3); }
            foreach (var trip in addTriples) { graph.AppendTripple(trip.Item1, trip.Item2, trip.Item3); }
        }

        public static PromptResult PredictActivityDuration(BIMGraph graph, string activityID)
        {
            PromptResult promptResult = new PromptResult()
            {
                success = false,
                prompt_token = 0,
                response_token = 0,
                total_token = 0,
                inference_time = 0,
            };

            List<Tuple<string, string, string>> deleteTriples = new List<Tuple<string, string, string>>();
            List<Tuple<string, string, string>> addTriples = new List<Tuple<string, string, string>>();

            List<Tuple<string, string, string>> newTriples = new List<Tuple<string, string, string>>();

            var elem = graph.TripleSearch(new List<string>() { activityID }, new List<string>() { "prop:forElement" }, null).Select(t => t.Item3).FirstOrDefault();
            var task = graph.TripleSearch(new List<string>() { activityID }, new List<string>() { "prop:doingTask" }, null).Select(t => t.Item3).FirstOrDefault();

            if (elem == null || task == null)
            {
                Debug.Print("Warning: Could not find element or task for activity " + activityID);
                return promptResult;
            }

            // Deterimine a task duration based on the element and task information, and add it to the activity in the graph
            var elemInfos = graph.TripleSearch(new List<string>() { elem }, new List<string>() { "prop:Name", "prop:Description", "prop:guessedDescription", "prop:volume", "prop:surfaceArea" }, null).Select(t => t.Item1 + " " + t.Item2 + " " + t.Item3).ToList();
            var taskInfos = graph.TripleSearch(new List<string>() { task }, new List<string>() { "rdfs:label", "prop:Description" }, null).Select(t => t.Item1 + " " + t.Item2 + " " + t.Item3).ToList();

            var request = new
            {
                prompt = "Here is the element information of " + elem + ":\n" + string.Join("\n", elemInfos) + "\n\nHere is the task information of " + task + ":\n" + string.Join("\n", taskInfos) + "\n\nEstimate the duration required to perform task " + task + " for element " + elem + ".",
                prompt_information = "You are an expert construction project and task estimator. You understand the construction activities required" +
                " for different types of building elements and how the duration of an activity varies based on the characteristics of the building element, " +
                "the activity, and the expected productivity of an average worker." +
                "\nYou will be provided with:\n- A building element and its relevant characteristics." +
                "\n- A construction task/activity and its relevant characteristics." +
                "\n- Any additional information that may affect the task duration." +
                "\n\nYour task is to estimate the duration required to perform the specified construction task for the given building element." +
                "\n\nReturn your answer as a JSON object with exactly three fields:" +
                "\n\n{" +
                "\n  \"description\": <What the activity is doing on this particular element>," +
                "\n  \"value\": <duration in hours>," +
                "\n  \"logic\": \"<concise explanation of how the duration was estimated>\"" +
                "\n}" +
                "\n\nRequirements:" +
                "\n- \"description\" must be a brief description of the activity being performed on the element." +
                "\n- \"value\" must be a numerical value representing the estimated task duration in hours." +
                "\n- \"logic\" must briefly explain the main factors used to determine the duration, such as element characteristics, quantity, dimensions, complexity, task requirements, or worker productivity." +
                "\n- Base the estimate only on the information provided and reasonable construction knowledge." +
                "\n- If the provided information is insufficient for an exact estimate, make a reasonable assumption and state that assumption in \"logic\"." +
                "\n- Do not include units in \"value\"; the unit is always hours." +
                "\n- Do not include any additional fields or text outside the JSON object." +
                "\n- The JSON must be valid and parseable.",
                model = AINumber.ToString()
            };

            //prompt_information = "You are an expert construction project/task estimator. You know about what kind of tasks are required for different types of building elements and how long it takes to do a task based on the element characteristics and the task time for an average worker. You are given a building element and its related information and a construction task and its related information. Your task is to determine the duration of the construction task for this particular building element. Answer only with a number in hours.",

            string result = SendPostRequest(MODEL, JsonSerializer.Serialize(request), generateURL);
            double duration = 0;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(result))
                {
                    JsonElement root = doc.RootElement;
                    string response = root.GetProperty("response").GetString();
                    response = StripCodeFences(response);
                    using (JsonDocument estimateDoc = JsonDocument.Parse(response))
                    {
                        JsonElement estimate = estimateDoc.RootElement;
                        var valueEl = estimate.GetProperty("value");
                        duration = valueEl.ValueKind == JsonValueKind.Number
                            ? valueEl.GetDouble()
                            : double.Parse(valueEl.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture);
                        string description = estimate.TryGetProperty("description", out var d) ? d.GetString() : null;

                        newTriples.Add(Tuple.Create(activityID, "prop:duration", duration.ToString(CultureInfo.InvariantCulture)));
                        if (description != null)
                            newTriples.Add(Tuple.Create(activityID, "prop:description", description));
                    }

                    promptResult.success = true;

                    // Search if the task uses machinery and if so then calculate the energy consumption of the machinery based on the task duration and add it to the activity in the graph
                    var machinery = graph.TripleSearch(new List<string>() { task }, new List<string>() { "prop:usesMachine" }, null).Select(t => t.Item3).ToList();
                    var efficiency = graph.TripleSearch(machinery, new List<string>() { "prop:kgCO2perHour" }, null).Select(t => t.Item3).ToList();

                    if (efficiency.Count > 0 && TryParseLiteralDouble(efficiency[0], out double eff))
                        newTriples.Add(Tuple.Create(activityID, "prop:predictedEmissions",
                            (duration * eff).ToString(CultureInfo.InvariantCulture)));

                    promptResult.prompt_token = root.GetProperty("prompt_tokens").GetInt32();
                    promptResult.response_token = root.GetProperty("response_tokens").GetInt32();
                    promptResult.total_token = root.GetProperty("total_tokens").GetInt32();
                    promptResult.inference_time = root.GetProperty("inference_time_seconds").GetDouble();
                }
            }
            catch (Exception ex)
            {
                Debug.Print("Error processing activity for element " + elem + " and task " + task + ": " + ex.Message);
            }

            //foreach (var elementDescription in newTriples)
            //{
            //    graph.AppendTripple(elementDescription.Item1, elementDescription.Item2, elementDescription.Item3);
            //}

            addTriples.AddRange(newTriples);

            foreach (var trip in deleteTriples) { graph.RemoveMatchingTriple(trip.Item1, trip.Item2, trip.Item3); }
            foreach (var trip in addTriples) { graph.AppendTripple(trip.Item1, trip.Item2, trip.Item3); }

            return promptResult;
        }
    }

    public class PromptResult
    {
        public int prompt_token { get; set; }
        public int response_token { get; set; }
        public int total_token { get; set; }
        public double inference_time { get; set; }
        public bool success { get; set; }
    }
}
