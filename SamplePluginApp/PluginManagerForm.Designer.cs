namespace SamplePluginApp
{
    partial class PluginManagerForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            _tableLayout        = new TableLayoutPanel();
            _topPanel           = new Panel();
            _labelConfig        = new Label();
            _textBoxConfig      = new TextBox();
            _buttonBrowse       = new Button();
            _buttonLoad         = new Button();
            _executePanel       = new Panel();
            _labelStage         = new Label();
            _comboBoxStage      = new ComboBox();
            _buttonExecute      = new Button();
            _buttonRunAll       = new Button();
            _richTextBoxLog     = new RichTextBox();
            _statusStrip        = new StatusStrip();
            _toolStripStatus    = new ToolStripStatusLabel();
            _buttonClear        = new Button();

            _tableLayout.SuspendLayout();
            _topPanel.SuspendLayout();
            _executePanel.SuspendLayout();
            _statusStrip.SuspendLayout();
            SuspendLayout();

            // _tableLayout
            _tableLayout.ColumnCount = 1;
            _tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _tableLayout.Controls.Add(_topPanel, 0, 0);
            _tableLayout.Controls.Add(_executePanel, 0, 1);
            _tableLayout.Controls.Add(_richTextBoxLog, 0, 2);
            _tableLayout.Dock = DockStyle.Fill;
            _tableLayout.RowCount = 3;
            _tableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            _tableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            _tableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _tableLayout.Padding = new Padding(8, 8, 8, 4);

            // _topPanel（設定ファイル行）
            _topPanel.Dock = DockStyle.Fill;

            _labelConfig.AutoSize = true;
            _labelConfig.Text = "設定ファイル:";
            _labelConfig.Location = new Point(0, 12);

            _textBoxConfig.Location = new Point(80, 8);
            _textBoxConfig.Size = new Size(460, 23);
            _textBoxConfig.Text = "pluginsettings.json";

            _buttonBrowse.Text = "参照...";
            _buttonBrowse.Location = new Point(546, 7);
            _buttonBrowse.Size = new Size(64, 26);
            _buttonBrowse.Click += ButtonBrowse_Click;

            _buttonLoad.Text = "ロード";
            _buttonLoad.Location = new Point(616, 7);
            _buttonLoad.Size = new Size(80, 26);
            _buttonLoad.Click += ButtonLoad_Click;

            _topPanel.Controls.AddRange(new Control[] {_labelConfig, _textBoxConfig, _buttonBrowse, _buttonLoad});

            // _executePanel（ステージ実行行）
            _executePanel.Dock = DockStyle.Fill;

            _labelStage.AutoSize = true;
            _labelStage.Text = "ステージ:";
            _labelStage.Location = new Point(0, 12);

            _comboBoxStage.Location = new Point(62, 8);
            _comboBoxStage.Size = new Size(200, 23);
            _comboBoxStage.DropDownStyle = ComboBoxStyle.DropDownList;

            _buttonExecute.Text = "実行";
            _buttonExecute.Location = new Point(268, 7);
            _buttonExecute.Size = new Size(80, 26);
            _buttonExecute.Enabled = false;
            _buttonExecute.Click += ButtonExecute_Click;

            _buttonRunAll.Text = "すべて実行";
            _buttonRunAll.Location = new Point(354, 7);
            _buttonRunAll.Size = new Size(96, 26);
            _buttonRunAll.Enabled = false;
            _buttonRunAll.Click += ButtonRunAll_Click;

            _buttonClear.Text = "クリア";
            _buttonClear.Location = new Point(440, 7);
            _buttonClear.Size = new Size(80, 26);
            _buttonClear.Click += ButtonClear_Click;

            _executePanel.Controls.AddRange(new Control[] {_labelStage, _comboBoxStage, _buttonExecute, _buttonRunAll, _buttonClear});

            // _richTextBoxLog
            _richTextBoxLog.Dock = DockStyle.Fill;
            _richTextBoxLog.ReadOnly = true;
            _richTextBoxLog.BackColor = Color.FromArgb(30, 30, 30);
            _richTextBoxLog.ForeColor = Color.LightGray;
            _richTextBoxLog.Font = new Font("Consolas", 9.5F);
            _richTextBoxLog.ScrollBars = RichTextBoxScrollBars.Vertical;

            // _statusStrip
            _toolStripStatus.Text = "待機中";
            _statusStrip.Items.Add(_toolStripStatus);
            _statusStrip.SizingGrip = false;

            // PluginManagerForm
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 520);
            Controls.Add(_tableLayout);
            Controls.Add(_statusStrip);
            MinimumSize = new Size(640, 400);
            Text = "SamplePluginApp";
            StartPosition = FormStartPosition.CenterScreen;

            _tableLayout.ResumeLayout(false);
            _topPanel.ResumeLayout(false);
            _executePanel.ResumeLayout(false);
            _statusStrip.ResumeLayout(false);
            _statusStrip.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TableLayoutPanel   _tableLayout;
        private Panel              _topPanel;
        private Label              _labelConfig;
        private TextBox            _textBoxConfig;
        private Button             _buttonBrowse;
        private Button             _buttonLoad;
        private Panel              _executePanel;
        private Label              _labelStage;
        private ComboBox           _comboBoxStage;
        private Button             _buttonExecute;
        private Button             _buttonRunAll;
        private Button             _buttonClear;
        private RichTextBox        _richTextBoxLog;
        private StatusStrip        _statusStrip;
        private ToolStripStatusLabel _toolStripStatus;
    }
}
