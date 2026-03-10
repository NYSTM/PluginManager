using PluginManager;

namespace SamplePluginApp;

public partial class PluginManagerForm : Form
{
    private PluginLoader? _loader;
    private IReadOnlyList<PluginLoadResult> _loadResults = [];

    // ステージをまたいで結果を受け渡すための実行スコープ。ロード時に生成し直す。
    private PluginContext _executionContext = new();

    public PluginManagerForm()
    {
        InitializeComponent();
        PopulateStageComboBox();
        FormClosed += PluginManagerForm_FormClosed;
    }

    // ──────────────────────────────────────────────
    // 初期化
    // ──────────────────────────────────────────────

    private void PopulateStageComboBox()
    {
        _comboBoxStage.Items.Clear();
        _comboBoxStage.Items.Add(PluginStage.PreProcessing);
        _comboBoxStage.Items.Add(PluginStage.Processing);
        _comboBoxStage.Items.Add(PluginStage.PostProcessing);
        _comboBoxStage.DisplayMember = nameof(PluginStage.Id);
        _comboBoxStage.SelectedIndex = 1;
    }

    // ──────────────────────────────────────────────
    // イベントハンドラ
    // ──────────────────────────────────────────────

    private void ButtonBrowse_Click(object sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "設定ファイルを選択",
            Filter = "JSON ファイル (*.json)|*.json|すべてのファイル (*.*)|*.*",
            InitialDirectory = AppContext.BaseDirectory,
        };

        if (dialog.ShowDialog() == DialogResult.OK)
            _textBoxConfig.Text = dialog.FileName;
    }

    private async void ButtonLoad_Click(object sender, EventArgs e)
    {
        var configPath = _textBoxConfig.Text.Trim();
        if (string.IsNullOrEmpty(configPath))
        {
            AppendLog("設定ファイルのパスを入力してください。", LogLevel.Warn);
            return;
        }

        if (!Path.IsPathRooted(configPath))
            configPath = Path.Combine(AppContext.BaseDirectory, configPath);

        if (!File.Exists(configPath))
        {
            AppendLog($"設定ファイルが見つかりません: {configPath}", LogLevel.Error);
            return;
        }

        SetBusy("プラグインをロード中...");
        try
        {
            _loader?.Dispose();
            _loader = new PluginLoader();
            _loadResults = [];

            // コールバック方式で通知を受け取る（イベントハンドラより直感的）
            _loader.SetCallback(new SimplePluginCallback(AppendLog));

            // ロードのたびに実行コンテキストをリセット
            _executionContext = new PluginContext();

            _loadResults = await _loader.LoadFromConfigurationAsync(configPath, _executionContext);

            AppendLog($"ロード完了: {_loadResults.Count} 件");
            foreach (var r in _loadResults)
            {
                if (r.Success)
                    AppendLog($"  ├─ {r.Descriptor.Id}  v{r.Descriptor.Version}  " +
                              $"[{string.Join(", ", r.Descriptor.SupportedStages.Select(s => s.Id))}]", LogLevel.Success);
                else
                    AppendLog($"  └─ {r.Descriptor.Id}  {r.Error?.Message}", LogLevel.Error);
            }

            var hasPlugin = _loadResults.Any(r => r.Success);
            _buttonExecute.Enabled = hasPlugin;
            _buttonRunAll.Enabled  = hasPlugin;
            SetStatus(hasPlugin ? "ロード成功。ステージを選択して実行してください。" : "ロードしましたが有効なプラグインがありません。");
        }
        catch (Exception ex)
        {
            AppendLog($"ロードに失敗しました: {ex.Message}", LogLevel.Error);
            SetStatus("ロード失敗");
        }
        finally
        {
            SetIdle();
        }
    }

    /// <summary>
    /// 選択した単一ステージを実行します。
    /// 同一 _executionContext を使用するため、他のステージの結果を参照できます。
    /// </summary>
    private async void ButtonExecute_Click(object sender, EventArgs e)
    {
        if (_loader is null || _loadResults.Count == 0)
        {
            AppendLog("先にプラグインをロードしてください。", LogLevel.Warn);
            return;
        }

        if (_comboBoxStage.SelectedItem is not PluginStage stage)
        {
            AppendLog("実行するステージを選択してください。", LogLevel.Warn);
            return;
        }

        SetBusy($"{stage.Id} ステージを実行中...");
        try
        {
            await ExecuteStageAsync(stage);
            SetStatus($"{stage.Id} ステージ実行完了");
        }
        catch (Exception ex)
        {
            AppendLog($"実行中にエラーが発生しました: {ex.Message}", LogLevel.Error);
            SetStatus("実行失敗");
        }
        finally
        {
            SetIdle();
        }
    }

    /// <summary>
    /// pluginsettings.json の StageOrders に定義された全ステージを順番に実行します。
    /// 全ステージで同一 _executionContext を共有するため、
    /// PreProcessing の結果を PostProcessing で参照できます。
    /// </summary>
    private async void ButtonRunAll_Click(object sender, EventArgs e)
    {
        if (_loader is null || _loadResults.Count == 0)
        {
            AppendLog("先にプラグインをロードしてください。", LogLevel.Warn);
            return;
        }

        SetBusy("全ステージを実行中...");
        try
        {
            AppendLog("====== 全ステージ実行 開始 ======");

            // pluginsettings.json の StageOrders 定義順に実行
            var stages = _loadResults
                .SelectMany(r => r.Descriptor.SupportedStages)
                .Distinct()
                .ToList();

            foreach (var stage in stages)
                await ExecuteStageAsync(stage);

            AppendLog("====== 全ステージ実行 完了 ======", LogLevel.Success);
            SetStatus("全ステージ実行完了");
        }
        catch (Exception ex)
        {
            AppendLog($"実行中にエラーが発生しました: {ex.Message}", LogLevel.Error);
            SetStatus("実行失敗");
        }
        finally
        {
            SetIdle();
        }
    }

    private void ButtonClear_Click(object sender, EventArgs e)
        => _richTextBoxLog.Clear();

    private async void PluginManagerForm_FormClosed(object? sender, FormClosedEventArgs e)
        => await (_loader?.DisposeAsync() ?? ValueTask.CompletedTask);

    // ──────────────────────────────────────────────
    // ステージ実行（単一・全実行共通）
    // ──────────────────────────────────────────────

    /// <summary>
    /// 指定ステージを <see cref="_executionContext"/> を使って実行します。
    /// _executionContext はステージをまたいで共有されるため、
    /// PreProcessing で context.SetProperty した値を PostProcessing で GetProperty できます。
    /// </summary>
    private async Task ExecuteStageAsync(PluginStage stage)
    {
        AppendLog($"─── {stage.Id} ステージ 開始 ───");

        var results = await _loader!.ExecutePluginsAndWaitAsync(
            _loadResults, stage, _executionContext);

        AppendLog($"─── {stage.Id} ステージ 完了 ({results.Count} 件) ───", LogLevel.Success);
        foreach (var r in results)
        {
            if (r.Success)
                AppendLog($"  [{r.Descriptor.Id}]: {r.Value ?? "(null)"}");
            else
                AppendLog($"  [{r.Descriptor.Id}] エラー: {r.Error?.Message}", LogLevel.Error);
        }
    }

    // ──────────────────────────────────────────────
    // ログ出力
    // ──────────────────────────────────────────────

    private void AppendLog(string message, LogLevel level = LogLevel.Info)
    {
        if (InvokeRequired)
        {
            Invoke(() => AppendLog(message, level));
            return;
        }

        var color = level switch
        {
            LogLevel.Success => Color.LightGreen,
            LogLevel.Warn    => Color.Yellow,
            LogLevel.Error   => Color.Tomato,
            _                => Color.LightGray,
        };

        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        _richTextBoxLog.SelectionStart = _richTextBoxLog.TextLength;
        _richTextBoxLog.SelectionLength = 0;
        _richTextBoxLog.SelectionColor = Color.DarkGray;
        _richTextBoxLog.AppendText($"[{timestamp}] ");
        _richTextBoxLog.SelectionColor = color;
        _richTextBoxLog.AppendText(message + Environment.NewLine);
        _richTextBoxLog.ScrollToCaret();
    }

    // ──────────────────────────────────────────────
    // UI 状態管理
    // ──────────────────────────────────────────────

    private void SetBusy(string message)
    {
        _buttonLoad.Enabled    = false;
        _buttonExecute.Enabled = false;
        _buttonRunAll.Enabled  = false;
        _buttonBrowse.Enabled  = false;
        SetStatus(message);
        Cursor = Cursors.WaitCursor;
    }

    private void SetIdle()
    {
        var hasPlugin = _loadResults.Any(r => r.Success);
        _buttonLoad.Enabled    = true;
        _buttonBrowse.Enabled  = true;
        _buttonExecute.Enabled = hasPlugin;
        _buttonRunAll.Enabled  = hasPlugin;
        Cursor = Cursors.Default;
    }

    private void SetStatus(string message)
    {
        _toolStripStatus.Text = message;
        _statusStrip.Refresh();
    }
}
