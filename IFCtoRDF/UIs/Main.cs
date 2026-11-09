using IFCtoRDF.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using ProgressBar = System.Windows.Forms.ProgressBar;

namespace IFCtoRDF
{
    public partial class Main : Form
    {
        private string modelNamespace = "http://www.example.org#";
        private string modelNamespaceShort = "ex";

        private int DEFAULT_PARALLEL_CAPACITY = 20;

        public Main()
        {
            InitializeComponent();

            System.Net.ServicePointManager.DefaultConnectionLimit = 50;

            textBoxNS.Text = modelNamespace;
            textBoxNSShort.Text = modelNamespaceShort;

            this.comboBoxAIModel.Items.Add("PortableLLM");
            this.comboBoxAIModel.Items.Add("ChatGPT");
            this.comboBoxAIModel.Items.Add("Claude");

            this.comboBoxAIModel.SelectedIndex = 0;
        }

        public async void buttonConvertIFCtoRDF_Click(object sender, System.EventArgs e)
        {
            try
            {
                OpenFileDialog ofd = new OpenFileDialog
                {
                    Title = "Select IFC Files",
                    Filter = "IFC Files (*.ifc)|*.ifc",
                    Multiselect = true
                };
                if (ofd.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                SaveFileDialog sfd = new SaveFileDialog
                {
                    Title = "Select TTL Save Location",
                    Filter = "Turtle File (*.ttl)|*.ttl"
                };
                if (ofd.FileNames.Length == 1)
                {
                    sfd.FileName = Path.GetFileName(Path.ChangeExtension(ofd.FileNames[0], ".ttl"));
                }
                else
                {
                    sfd.FileName = "combinedModel.ttl";
                }

                if (sfd.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                string extension = Path.GetExtension(sfd.FileName);
                if (extension != ".ttl")
                {
                    return;
                }
                List<string> fileNames = ofd.FileNames.ToList();

                BIMModel model = await IFCtoBIM(fileNames, sfd.FileName, progressBar, checkBoxTranslate.Checked);
                BIMGraph graph = BIMConverter.ConvertBIMtoRDF(model, textBoxNS.Text, textBoxNSShort.Text, progressBar, checkBoxDetailedBOT.Checked);

                graph.SaveDataFile(sfd.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private async Task<BIMModel> IFCtoBIM(List<string> fileNames, string outputModelName, ProgressBar progressBar, bool translate = false)
        {
            progressBar.Value = 0;
            progressBar.Maximum = fileNames.Count;
            var progressRead = new Progress<int>(value => { progressBar.Value = Math.Min(value, progressBar.Maximum); });
            int completed = 0;

            ConcurrentBag<BIMModel> models = new ConcurrentBag<BIMModel>();
            List<Task> tasks = new List<Task>();
            foreach (string fileName in fileNames)
            {
                tasks.Add(Task.Run(async () =>
                {
                    BIMModel model = null;
                    if (Path.GetExtension(fileName) == ".ifc")
                    {
                        model = IfcConverter.GetModelFromIFC(fileName);
                    }
                    if (Path.GetExtension(fileName) == ".bimk")
                    {
                        model = await BIMModel.Read(fileName, true);
                    }

                    models.Add(model);
                    int newProgress = Interlocked.Increment(ref completed);
                    ((IProgress<int>)progressRead).Report(newProgress);

                    if (translate)
                    {
                        TranslateModel(model);
                    }
                }));
            }

            await Task.WhenAll(tasks);
            progressBar.Value = progressBar.Maximum;

            BIMModel combinedModel = BIMModel.CombineModels(models.ToList(), Path.GetFileNameWithoutExtension(outputModelName));
            return combinedModel;
        }

        private async void TranslateModel(BIMModel model)
        {
            Translator.AzureTranslatorApiKey = textBoxTranslateAPIKey.Text;
            Translator.CreateTranslator();

            // go over all objects and their properties and translate them:
            Dictionary<string, string> newPropsModel = new Dictionary<string, string>();
            foreach (var prop in model.Properties)
            {
                string propNew = await Translator.TranslateToEnglishAsync(prop.Key);
                string valueNew = prop.Value == null ? null : await Translator.TranslateToEnglishAsync(prop.Value.ToString());
                newPropsModel[propNew] = valueNew;
            }
            model.Properties = newPropsModel.ToDictionary(entry => entry.Key, entry => (object)entry.Value);
            foreach (var obj in model.Objects)
            {
                Dictionary<string, string> newPropsObj = new Dictionary<string, string>();
                foreach (var prop in obj.Value.Properties)
                {
                    string propNew = await Translator.TranslateToEnglishAsync(prop.Key);
                    string valueNew = prop.Value == null ? null : await Translator.TranslateToEnglishAsync(prop.Value.ToString());
                    newPropsObj[propNew] = valueNew;
                }
                obj.Value.Properties = newPropsObj.ToDictionary(entry => entry.Key, entry => (object)entry.Value);
                foreach (var comp in obj.Value.Components)
                {
                    Dictionary<string, string> newPropsComp = new Dictionary<string, string>();
                    foreach (var prop in comp.Value.Properties)
                    {
                        string propNew = await Translator.TranslateToEnglishAsync(prop.Key);
                        string valueNew = prop.Value == null ? null : await Translator.TranslateToEnglishAsync(prop.Value.ToString());
                        newPropsComp[propNew] = valueNew;
                    }
                    comp.Value.Properties = newPropsComp.ToDictionary(entry => entry.Key, entry => (object)entry.Value);
                }
            }
        }

        private async void buttonTTLReasoning_Click(object sender, System.EventArgs e)
        {
            try
            {
                OpenFileDialog ofd = new OpenFileDialog
                {
                    Title = "Select All Build, Construction, and Carbon Data Files",
                    Filter = "Turtle File (*.ttl)|*.ttl",
                    Multiselect = true
                };
                if (ofd.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                BIMGraph graph = new BIMGraph();
                foreach (string file in ofd.FileNames)
                {
                    graph.AppendDataFile(file);
                }

                double confidenceThreshold = (double)this.numericUpDownConfidence.Value;
                bool skipExisting = checkBoxSkip.Checked;

                SaveFileDialog sfd = new SaveFileDialog
                {
                    Title = "Select Merged TTL Save Location",
                    Filter = "Turtle File|*.ttl",
                    FileName = "MergedGraph.ttl"
                };
                if (sfd.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                PerformGraphReasoning(graph, sfd.FileName, this.comboBoxAIModel.SelectedText, confidenceThreshold, skipExisting);
                graph.SaveDataFile(sfd.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private async void PerformGraphReasoning(BIMGraph graph, string saveFileName, string model, double confidenceThreshold, bool skipExisting)
        {
            Dictionary<string, string> materialDef = SemanticEnrichment.GetClassDictionary(graph, "ice:MaterialProduct");
            Dictionary<string, string> constructionTaskDef = SemanticEnrichment.GetClassDictionary(graph, "prop:BuildingWork");

            if (materialDef.Count == 0 || constructionTaskDef.Count == 0)
            {
                MessageBox.Show("No material or construction task definitions found in the graph. Please ensure that the graph contains these definitions.");
                return;
            }

            // Go over each element and do all the actions: classification, material merge, element description, task creation, task connection, activity generation
            var buildingElems = graph.TripleSearch(null, new List<string>() { "a", "rdf:type" }, new List<string>() { "bot:Element" }).Select(t => t.Item1).ToList();
            this.progressBar.Maximum = buildingElems.Count;
            this.progressBar.Value = 0;

            object statsLock = new object();

            int promptCountElement = 0;
            int input_tokenCountElement = 0;
            int output_tokenCountElement = 0;
            double inferenceTimeElement = 0;

            int total_promptCountActivity = 0;
            int total_input_tokenCountActivity = 0;
            int total_output_tokenCountActivity = 0;
            double total_inferenceTimeActivity = 0;

            int completed = 0;
            IProgress<int> progress = new Progress<int>(value => this.progressBar.Value = value);

            model = (string)comboBoxAIModel.SelectedItem;
            SemanticEnrichment.MODEL = model;
            int parallelCapacity = DEFAULT_PARALLEL_CAPACITY;
            if (model == "PortableLLM")
            {
                parallelCapacity = SemanticEnrichment.GetParallelCapacity();
            }
            else if (model == "ChatGPT")
            {
                SemanticEnrichment.OPEN_API_KEY = textBoxLLMAPIKey.Text;
            }
            else if (model == "Claude")
            {
                SemanticEnrichment.ANTHROPIC_API_KEY = textBoxLLMAPIKey.Text;
            }
            else
            {
                throw new Exception("Invalid LLM selected.");
            }

            // TEST
            //int testingRange = 10;
            //buildingElems = buildingElems.GetRange(0, Math.Min(testingRange, buildingElems.Count));
            //parallelCapacity = 1;

            Stopwatch fullTimer = new Stopwatch();
            fullTimer.Start();
            var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Min(parallelCapacity, Math.Max(buildingElems.Count, 1)) };
            await Task.Run(() => Parallel.ForEach(buildingElems, options, elem =>
            {
                try
                {
                    if (skipExisting && graph.TripleSearch(new List<string>() { elem }, new List<string>() { "prop:guessedDescription" }, null).Count > 0)
                    {
                        return;
                    }
                    PromptResult elementDetailResult = SemanticEnrichment.ElementDetailGenerator(graph, elem);
                    if (!elementDetailResult.success)
                    {
                        return;
                    }

                    lock (statsLock)
                    {
                        promptCountElement += 1;
                        input_tokenCountElement += elementDetailResult.prompt_token;
                        output_tokenCountElement += elementDetailResult.response_token;
                        inferenceTimeElement += elementDetailResult.inference_time;
                    }

                    SemanticEnrichment.FindClosestMatchingMaterial(graph, elem, materialDef, confidenceThreshold, "prop:guessedMaterial");
                    SemanticEnrichment.FindClosestMatchingRealTasks(graph, elem, constructionTaskDef, confidenceThreshold);

                    var activities = graph.TripleSearch(null, new List<string>() { "prop:forElement" }, new List<string>() { elem }).Select(t => t.Item1).ToList();

                    int promptCountActivity = 0;
                    int input_tokenCountActivity = 0;
                    int output_tokenCountActivity = 0;
                    double inferenceTimeActivity = 0;
                    foreach (var activity in activities)
                    {
                        PromptResult activityResult = SemanticEnrichment.PredictActivityDuration(graph, activity);

                        promptCountActivity += 1;
                        input_tokenCountActivity += activityResult.prompt_token;
                        output_tokenCountActivity += activityResult.response_token;
                        inferenceTimeActivity += activityResult.inference_time;
                    }

                    lock (statsLock)
                    {
                        total_promptCountActivity += promptCountActivity;
                        total_input_tokenCountActivity += input_tokenCountActivity;
                        total_output_tokenCountActivity += output_tokenCountActivity;
                        total_inferenceTimeActivity += inferenceTimeActivity;
                    }

                    graph.SaveDataFile(saveFileName);

                    if (promptCountActivity > 0)
                    {
                        //Debug.Print($"Element {elem} processed.\n" +
                        //    $"Element Input Tokens: {elementDetailResult.prompt_token}\n" +
                        //    $"Element Output Tokens: {elementDetailResult.response_token}\n" +
                        //    $"Element Inference Time: {elementDetailResult.inference_time}\n" +
                        //    $"{promptCountActivity} Activities\n" +
                        //    $"Mean Activity Input Tokens: {input_tokenCountActivity / promptCountActivity}\n" +
                        //    $"Mean Activity Output Tokens: {output_tokenCountActivity / promptCountActivity}\n" +
                        //    $"Mean Activity Inference Time: {inferenceTimeActivity / promptCountActivity}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.Print($"Element: {elem} had an issue: {ex}");
                }
                finally
                {
                    progress.Report(Interlocked.Increment(ref completed));
                }
            }));

            progressBar.Value = progressBar.Maximum;

            string messageString = "";
            if (promptCountElement > 0 && total_promptCountActivity > 0)
            {
                messageString = $"{promptCountElement} Elements processed.\n" +
                    $"Mean Element Input Tokens: {(double)input_tokenCountElement / promptCountElement}\n" +
                    $"Mean Element Output Tokens: {(double)output_tokenCountElement / promptCountElement}\n" +
                    $"Mean Element Inference Time: {(double)inferenceTimeElement / promptCountElement}\n" +
                    $"Mean Activities per Element {(double)total_promptCountActivity / promptCountElement}\n" +
                    $"{total_promptCountActivity} Activities\n" +
                    $"Mean Activity Input Tokens: {(double)total_input_tokenCountActivity / total_promptCountActivity}\n" +
                    $"Mean Activity Output Tokens: {(double)total_output_tokenCountActivity / total_promptCountActivity}\n" +
                    $"Mean Activity Inference Time: {(double)total_inferenceTimeActivity / total_promptCountActivity}\n" +
                    $"\n" +
                    $"Total Input Tokens = {input_tokenCountElement + total_input_tokenCountActivity}\n" +
                    $"Total Ouput Tokens = {output_tokenCountElement + total_output_tokenCountActivity}\n" +
                    $"Total Cost = {(input_tokenCountElement + total_input_tokenCountActivity) / 1000000 * 0.10 + (output_tokenCountElement + total_output_tokenCountActivity) / 1000000 * 0.5}\n" +
                    $"Full Time ({parallelCapacity} running parallel) = {fullTimer.Elapsed.ToString()}";
            }
            else
            {
                messageString = "No elements were processed. Please check the graph and ensure that it contains valid elements for reasoning.";
            }

            MessageBox.Show(messageString);
        }

        private void comboBoxAIModel_SelectedIndexChanged(object sender, EventArgs e)
        {
            string selectedProvider = (string)this.comboBoxAIModel.SelectedItem;
            textBoxModel.Text = selectedProvider == "ChatGPT" ? SemanticEnrichment.openAI_model : selectedProvider == "Claude" ? SemanticEnrichment.anthropic_model : "Qwen2.5-3B-Instruct-Q4_K_M";
            if (selectedProvider == "ChatGPT" || selectedProvider == "Claude")
            {
                labelAPIKey.Visible = true;
                textBoxLLMAPIKey.Visible = true;
                buttonCostEstimator.Visible = true;
            }
            else
            {
                labelAPIKey.Visible = false;
                textBoxLLMAPIKey.Visible = false;
                buttonCostEstimator.Visible = false;
            }
        }

        private void checkBoxTranslate_CheckedChanged(object sender, EventArgs e)
        {
            this.labelTranlateKey.Visible = checkBoxTranslate.Checked;
            this.textBoxTranslateAPIKey.Visible = checkBoxTranslate.Checked;
        }

        private void buttonCostEstimator_Click(object sender, EventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Title = "Select All Build, Construction, and Carbon Data Files",
                Filter = "Turtle File (*.ttl)|*.ttl",
                Multiselect = true
            };
            if (ofd.ShowDialog() != DialogResult.OK)
            {
                return;
            }
            BIMGraph graph = new BIMGraph();
            foreach (string file in ofd.FileNames)
            {
                graph.AppendDataFile(file);
            }

            var buildingElems = graph.TripleSearch(null, new List<string>() { "a", "rdf:type" }, new List<string>() { "bot:Element" }).Select(t => t.Item1).ToList();
            int elementCount = buildingElems.Count;

            double inputTokensPerElement = 750;
            double OutputTokensPerElement = 900;
            double inferenceTimePerElement = 6;
            double estimateTasksPerElement = 8;
            double inputTokensPerTask = 600;
            double outputTokensPerTask = 300;
            double inferenceTimePerTask = 3;

            double inputTokensPerElementTotal = inputTokensPerElement + (estimateTasksPerElement * inputTokensPerTask);
            double outputTokensPerElementTotal = OutputTokensPerElement + (estimateTasksPerElement * outputTokensPerTask);

            double inputCostPerElement = (inputTokensPerElementTotal / 1000000) * 0.1;
            double outputCostPerElement = (outputTokensPerElementTotal / 1000000) * 0.5;

            double totalCostPerElement = inputCostPerElement + outputCostPerElement;
            double totalCost = totalCostPerElement * elementCount;

            double totalInputTokens = inputTokensPerElementTotal * elementCount;
            double totalOutputTokens = outputTokensPerElementTotal * elementCount;

            double timePerElement = inferenceTimePerElement + estimateTasksPerElement * inferenceTimePerTask; // seconds
            double totalTime = timePerElement * elementCount;
            TimeSpan runtime = TimeSpan.FromSeconds(totalTime/ DEFAULT_PARALLEL_CAPACITY);

            string message =
                "Estimated Cost and Runtime:\n\n" +
                $"Elements to process: {elementCount:N0}\n" +
                $"Estimated tasks per element: {estimateTasksPerElement:N0}\n\n" +
                "--- Tokens ---\n" +
                $"Input tokens per element: {inputTokensPerElementTotal:N0}\n" +
                $"Output tokens per element: {outputTokensPerElementTotal:N0}\n" +
                $"Total input tokens: {totalInputTokens:N0}\n" +
                $"Total output tokens: {totalOutputTokens:N0}\n\n" +
                "--- Cost (USD) ---\n" +
                $"Input cost per element: ${inputCostPerElement:F6}\n" +
                $"Output cost per element: ${outputCostPerElement:F6}\n" +
                $"Total cost per element: ${totalCostPerElement:F6}\n" +
                $"Total estimated cost: ${totalCost:F4}\n\n" +
                "--- Runtime ---\n" +
                $"Time per element: {timePerElement} s\n" +
                $"Total estimated time: {runtime.Hours}h {runtime.Minutes}m {runtime.Seconds}s " +
                $"({totalTime:N0} seconds)";

            MessageBox.Show(message, "Processing Estimate", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}