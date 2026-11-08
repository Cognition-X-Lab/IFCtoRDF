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
                OpenFileDialog ofd = new OpenFileDialog();
                ofd.Filter = "IFC Files (*.ifc)|*.ifc";
                ofd.Multiselect = true;
                if (ofd.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                SaveFileDialog sfd = new SaveFileDialog();
                sfd.Filter = "Turtle File (*.ttl)|*.ttl";
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
                OpenFileDialog ofd = new OpenFileDialog();
                ofd.Filter = "Turtle File (*.ttl)|*.ttl";
                ofd.Multiselect = true;
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

                SaveFileDialog sfd = new SaveFileDialog();
                sfd.Filter = "Turtle File|*.ttl";
                sfd.FileName = "MergedGraph.ttl";
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

            int promptCount = 0;
            int prompt_tokenCount = 0;
            int result_tokenCount = 0;
            double inferenceTime = 0;

            int completed = 0;
            IProgress<int> progress = new Progress<int>(value => this.progressBar.Value = value);

            model = (string)comboBoxAIModel.SelectedItem;
            SemanticEnrichment.MODEL = model;
            int parallelCapacity = 5;
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
            buildingElems = new List<string>() { buildingElems[0] };

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
                        promptCount += 1;
                        prompt_tokenCount += elementDetailResult.prompt_token;
                        result_tokenCount += elementDetailResult.response_token;
                        inferenceTime += elementDetailResult.inference_time;
                    }

                    SemanticEnrichment.FindClosestMatchingMaterial(graph, elem, materialDef, confidenceThreshold, "prop:guessedMaterial");
                    SemanticEnrichment.FindClosestMatchingRealTasks(graph, elem, constructionTaskDef, confidenceThreshold);

                    var activities = graph.TripleSearch(null, new List<string>() { "prop:forElement" }, new List<string>() { elem }).Select(t => t.Item1).ToList();
                    foreach (var activity in activities)
                    {
                        PromptResult activityResult = SemanticEnrichment.PredictActivityDuration(graph, activity);

                        lock (statsLock)
                        {
                            promptCount += 1;
                            prompt_tokenCount += activityResult.prompt_token;
                            result_tokenCount += activityResult.response_token;
                            inferenceTime += activityResult.inference_time;
                        }
                    }

                    graph.SaveDataFile(saveFileName);

                    //Debug.Print($"Element: {elem} processed. Running Prompt Count: {promptCount}, Mean Prompt Tokens: {prompt_tokenCount / promptCount}, Mean Result Tokens: {result_tokenCount / promptCount}, Mean Inference Time: {inferenceTime / promptCount}");
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

            if (promptCount > 0)
            {
                MessageBox.Show($"Element: {buildingElems.Count} processed.\nPrompt Count: {promptCount}\nMean Prompt Tokens: {(double)prompt_tokenCount / promptCount}\nMean Result Tokens: {(double)result_tokenCount / promptCount}\nMean Inference Time: {(double)inferenceTime / promptCount}");
            }
            else
            {
                MessageBox.Show("No elements were processed. Please check the graph and ensure that it contains valid elements for reasoning.");
            }
        }

        private void comboBoxAIModel_SelectedIndexChanged(object sender, EventArgs e)
        {
            string selectedProvider = (string)this.comboBoxAIModel.SelectedItem;
            textBoxModel.Text = selectedProvider == "ChatGPT" ? SemanticEnrichment.openAI_model : selectedProvider == "Claude" ? SemanticEnrichment.anthropic_model : "Qwen2.5-3B-Instruct-Q4_K_M";
            if (selectedProvider == "ChatGPT" || selectedProvider == "Claude")
            {
                labelAPIKey.Visible = true;
                textBoxLLMAPIKey.Visible = true;
            }
            else
            {
                labelAPIKey.Visible = false;
                textBoxLLMAPIKey.Visible = false;
            }
        }

        private void checkBoxTranslate_CheckedChanged(object sender, EventArgs e)
        {
            this.labelTranlateKey.Visible = checkBoxTranslate.Checked;
            this.textBoxTranslateAPIKey.Visible = checkBoxTranslate.Checked;
        }
    }
}