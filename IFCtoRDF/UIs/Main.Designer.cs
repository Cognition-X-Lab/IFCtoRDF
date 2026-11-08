namespace IFCtoRDF
{
    partial class Main
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.buttonConvertIFCtoRDF = new System.Windows.Forms.Button();
            this.groupBoxSetting = new System.Windows.Forms.GroupBox();
            this.checkBoxTranslate = new System.Windows.Forms.CheckBox();
            this.textBoxNS = new System.Windows.Forms.TextBox();
            this.textBoxNSShort = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.checkBoxDetailedBOT = new System.Windows.Forms.CheckBox();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.labelAPIKey = new System.Windows.Forms.Label();
            this.textBoxLLMAPIKey = new System.Windows.Forms.TextBox();
            this.checkBoxSkip = new System.Windows.Forms.CheckBox();
            this.label3 = new System.Windows.Forms.Label();
            this.comboBoxAIModel = new System.Windows.Forms.ComboBox();
            this.label4 = new System.Windows.Forms.Label();
            this.numericUpDownConfidence = new System.Windows.Forms.NumericUpDown();
            this.buttonTTLReasoning = new System.Windows.Forms.Button();
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.labelModel = new System.Windows.Forms.Label();
            this.textBoxModel = new System.Windows.Forms.TextBox();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.textBoxTranslateAPIKey = new System.Windows.Forms.TextBox();
            this.labelTranlateKey = new System.Windows.Forms.Label();
            this.groupBoxSetting.SuspendLayout();
            this.groupBox1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownConfidence)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.SuspendLayout();
            // 
            // buttonConvertIFCtoRDF
            // 
            this.buttonConvertIFCtoRDF.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonConvertIFCtoRDF.Location = new System.Drawing.Point(3, 143);
            this.buttonConvertIFCtoRDF.Name = "buttonConvertIFCtoRDF";
            this.buttonConvertIFCtoRDF.Size = new System.Drawing.Size(391, 23);
            this.buttonConvertIFCtoRDF.TabIndex = 0;
            this.buttonConvertIFCtoRDF.Text = "Convert IFC file to RDF";
            this.buttonConvertIFCtoRDF.UseVisualStyleBackColor = true;
            this.buttonConvertIFCtoRDF.Click += new System.EventHandler(this.buttonConvertIFCtoRDF_Click);
            // 
            // groupBoxSetting
            // 
            this.groupBoxSetting.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBoxSetting.Controls.Add(this.labelTranlateKey);
            this.groupBoxSetting.Controls.Add(this.textBoxTranslateAPIKey);
            this.groupBoxSetting.Controls.Add(this.checkBoxTranslate);
            this.groupBoxSetting.Controls.Add(this.textBoxNS);
            this.groupBoxSetting.Controls.Add(this.textBoxNSShort);
            this.groupBoxSetting.Controls.Add(this.label2);
            this.groupBoxSetting.Controls.Add(this.label1);
            this.groupBoxSetting.Controls.Add(this.checkBoxDetailedBOT);
            this.groupBoxSetting.Location = new System.Drawing.Point(3, 3);
            this.groupBoxSetting.Name = "groupBoxSetting";
            this.groupBoxSetting.Size = new System.Drawing.Size(391, 134);
            this.groupBoxSetting.TabIndex = 1;
            this.groupBoxSetting.TabStop = false;
            this.groupBoxSetting.Text = "Converter Settings";
            // 
            // checkBoxTranslate
            // 
            this.checkBoxTranslate.AutoSize = true;
            this.checkBoxTranslate.Location = new System.Drawing.Point(9, 74);
            this.checkBoxTranslate.Name = "checkBoxTranslate";
            this.checkBoxTranslate.Size = new System.Drawing.Size(70, 17);
            this.checkBoxTranslate.TabIndex = 24;
            this.checkBoxTranslate.Text = "Translate";
            this.checkBoxTranslate.UseVisualStyleBackColor = true;
            this.checkBoxTranslate.CheckedChanged += new System.EventHandler(this.checkBoxTranslate_CheckedChanged);
            // 
            // textBoxNS
            // 
            this.textBoxNS.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.textBoxNS.Location = new System.Drawing.Point(79, 19);
            this.textBoxNS.Name = "textBoxNS";
            this.textBoxNS.Size = new System.Drawing.Size(306, 20);
            this.textBoxNS.TabIndex = 23;
            // 
            // textBoxNSShort
            // 
            this.textBoxNSShort.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.textBoxNSShort.Location = new System.Drawing.Point(107, 45);
            this.textBoxNSShort.Name = "textBoxNSShort";
            this.textBoxNSShort.Size = new System.Drawing.Size(278, 20);
            this.textBoxNSShort.TabIndex = 22;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(6, 22);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(67, 13);
            this.label2.TabIndex = 21;
            this.label2.Text = "Namespace:";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(6, 48);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(95, 13);
            this.label1.TabIndex = 20;
            this.label1.Text = "Namespace Short:";
            // 
            // checkBoxDetailedBOT
            // 
            this.checkBoxDetailedBOT.AutoSize = true;
            this.checkBoxDetailedBOT.Location = new System.Drawing.Point(9, 97);
            this.checkBoxDetailedBOT.Name = "checkBoxDetailedBOT";
            this.checkBoxDetailedBOT.Size = new System.Drawing.Size(122, 17);
            this.checkBoxDetailedBOT.TabIndex = 16;
            this.checkBoxDetailedBOT.Text = "Detailed BOT (Slow)";
            this.checkBoxDetailedBOT.UseVisualStyleBackColor = true;
            // 
            // groupBox1
            // 
            this.groupBox1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBox1.Controls.Add(this.textBoxModel);
            this.groupBox1.Controls.Add(this.labelModel);
            this.groupBox1.Controls.Add(this.labelAPIKey);
            this.groupBox1.Controls.Add(this.textBoxLLMAPIKey);
            this.groupBox1.Controls.Add(this.checkBoxSkip);
            this.groupBox1.Controls.Add(this.label3);
            this.groupBox1.Controls.Add(this.comboBoxAIModel);
            this.groupBox1.Controls.Add(this.label4);
            this.groupBox1.Controls.Add(this.numericUpDownConfidence);
            this.groupBox1.Location = new System.Drawing.Point(3, 3);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(391, 151);
            this.groupBox1.TabIndex = 2;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "LLM Settings";
            // 
            // labelAPIKey
            // 
            this.labelAPIKey.AutoSize = true;
            this.labelAPIKey.Location = new System.Drawing.Point(6, 75);
            this.labelAPIKey.Name = "labelAPIKey";
            this.labelAPIKey.Size = new System.Drawing.Size(58, 13);
            this.labelAPIKey.TabIndex = 33;
            this.labelAPIKey.Text = "API Token";
            // 
            // textBoxLLMAPIKey
            // 
            this.textBoxLLMAPIKey.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.textBoxLLMAPIKey.Location = new System.Drawing.Point(71, 72);
            this.textBoxLLMAPIKey.Name = "textBoxLLMAPIKey";
            this.textBoxLLMAPIKey.Size = new System.Drawing.Size(314, 20);
            this.textBoxLLMAPIKey.TabIndex = 32;
            // 
            // checkBoxSkip
            // 
            this.checkBoxSkip.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.checkBoxSkip.AutoSize = true;
            this.checkBoxSkip.Checked = true;
            this.checkBoxSkip.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBoxSkip.Location = new System.Drawing.Point(6, 128);
            this.checkBoxSkip.Name = "checkBoxSkip";
            this.checkBoxSkip.Size = new System.Drawing.Size(146, 17);
            this.checkBoxSkip.TabIndex = 31;
            this.checkBoxSkip.Text = "Skip Completed Elements";
            this.checkBoxSkip.UseVisualStyleBackColor = true;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(6, 22);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(59, 13);
            this.label3.TabIndex = 30;
            this.label3.Text = "AI Provider";
            // 
            // comboBoxAIModel
            // 
            this.comboBoxAIModel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.comboBoxAIModel.FormattingEnabled = true;
            this.comboBoxAIModel.Location = new System.Drawing.Point(71, 19);
            this.comboBoxAIModel.Name = "comboBoxAIModel";
            this.comboBoxAIModel.Size = new System.Drawing.Size(314, 21);
            this.comboBoxAIModel.TabIndex = 27;
            this.comboBoxAIModel.SelectedIndexChanged += new System.EventHandler(this.comboBoxAIModel_SelectedIndexChanged);
            // 
            // label4
            // 
            this.label4.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(6, 104);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(144, 13);
            this.label4.TabIndex = 29;
            this.label4.Text = "Semantic Similarity Threshold";
            // 
            // numericUpDownConfidence
            // 
            this.numericUpDownConfidence.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.numericUpDownConfidence.DecimalPlaces = 2;
            this.numericUpDownConfidence.Increment = new decimal(new int[] {
            1,
            0,
            0,
            131072});
            this.numericUpDownConfidence.Location = new System.Drawing.Point(156, 102);
            this.numericUpDownConfidence.Maximum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numericUpDownConfidence.Name = "numericUpDownConfidence";
            this.numericUpDownConfidence.Size = new System.Drawing.Size(55, 20);
            this.numericUpDownConfidence.TabIndex = 28;
            this.numericUpDownConfidence.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numericUpDownConfidence.Value = new decimal(new int[] {
            5,
            0,
            0,
            65536});
            // 
            // buttonTTLReasoning
            // 
            this.buttonTTLReasoning.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonTTLReasoning.Location = new System.Drawing.Point(3, 160);
            this.buttonTTLReasoning.Name = "buttonTTLReasoning";
            this.buttonTTLReasoning.Size = new System.Drawing.Size(391, 23);
            this.buttonTTLReasoning.TabIndex = 3;
            this.buttonTTLReasoning.Text = "Perform Merge and Reasoning on TTL Files";
            this.buttonTTLReasoning.UseVisualStyleBackColor = true;
            this.buttonTTLReasoning.Click += new System.EventHandler(this.buttonTTLReasoning_Click);
            // 
            // splitContainer1
            // 
            this.splitContainer1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.splitContainer1.Location = new System.Drawing.Point(0, 0);
            this.splitContainer1.Name = "splitContainer1";
            this.splitContainer1.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.Controls.Add(this.groupBoxSetting);
            this.splitContainer1.Panel1.Controls.Add(this.buttonConvertIFCtoRDF);
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.Controls.Add(this.groupBox1);
            this.splitContainer1.Panel2.Controls.Add(this.buttonTTLReasoning);
            this.splitContainer1.Size = new System.Drawing.Size(398, 359);
            this.splitContainer1.SplitterDistance = 169;
            this.splitContainer1.TabIndex = 4;
            // 
            // labelModel
            // 
            this.labelModel.AutoSize = true;
            this.labelModel.Location = new System.Drawing.Point(6, 49);
            this.labelModel.Name = "labelModel";
            this.labelModel.Size = new System.Drawing.Size(36, 13);
            this.labelModel.TabIndex = 34;
            this.labelModel.Text = "Model";
            // 
            // textBoxModel
            // 
            this.textBoxModel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.textBoxModel.Location = new System.Drawing.Point(71, 46);
            this.textBoxModel.Name = "textBoxModel";
            this.textBoxModel.Size = new System.Drawing.Size(314, 20);
            this.textBoxModel.TabIndex = 35;
            // 
            // progressBar
            // 
            this.progressBar.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.progressBar.Location = new System.Drawing.Point(0, 365);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(394, 16);
            this.progressBar.TabIndex = 5;
            // 
            // textBoxTranslateAPIKey
            // 
            this.textBoxTranslateAPIKey.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.textBoxTranslateAPIKey.Location = new System.Drawing.Point(224, 71);
            this.textBoxTranslateAPIKey.Name = "textBoxTranslateAPIKey";
            this.textBoxTranslateAPIKey.Size = new System.Drawing.Size(159, 20);
            this.textBoxTranslateAPIKey.TabIndex = 25;
            this.textBoxTranslateAPIKey.Visible = false;
            // 
            // labelTranlateKey
            // 
            this.labelTranlateKey.AutoSize = true;
            this.labelTranlateKey.Location = new System.Drawing.Point(104, 75);
            this.labelTranlateKey.Name = "labelTranlateKey";
            this.labelTranlateKey.Size = new System.Drawing.Size(114, 13);
            this.labelTranlateKey.TabIndex = 26;
            this.labelTranlateKey.Text = "AzureTranslatorApiKey";
            this.labelTranlateKey.Visible = false;
            // 
            // Main
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(398, 382);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.splitContainer1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "Main";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "IFC to RDF";
            this.groupBoxSetting.ResumeLayout(false);
            this.groupBoxSetting.PerformLayout();
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDownConfidence)).EndInit();
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button buttonConvertIFCtoRDF;
        private System.Windows.Forms.GroupBox groupBoxSetting;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.Button buttonTTLReasoning;
        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.TextBox textBoxNS;
        private System.Windows.Forms.TextBox textBoxNSShort;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.CheckBox checkBoxDetailedBOT;
        private System.Windows.Forms.CheckBox checkBoxTranslate;
        private System.Windows.Forms.CheckBox checkBoxSkip;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.ComboBox comboBoxAIModel;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.NumericUpDown numericUpDownConfidence;
        private System.Windows.Forms.TextBox textBoxLLMAPIKey;
        private System.Windows.Forms.Label labelAPIKey;
        private System.Windows.Forms.TextBox textBoxModel;
        private System.Windows.Forms.Label labelModel;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.TextBox textBoxTranslateAPIKey;
        private System.Windows.Forms.Label labelTranlateKey;
    }
}

