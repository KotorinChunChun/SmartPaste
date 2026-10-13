namespace SmartPaste;

internal sealed class SettingsForm : Form
{
    public event Action? ClipboardChanged;
    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg == 0x031D)
            ClipboardChanged?.Invoke();
    }

    public CheckBox EnabledBox { get; } = new()
    {
        Name = "EnableInterception",
        Text = "SmartPasteを有効にする",
        AutoSize = true,
        Dock = DockStyle.Fill
    };
    public CheckBox ReviewBox { get; } = new()
    {
        Name = "AlwaysReview",
        Text = "決定前に数字・日付を確認・修正する",
        AutoSize = true,
        Dock = DockStyle.Fill
    };
    public TextBox CodexPath { get; } = Ui.Text("CodexPath");
    public TextBox History { get; } = new()
    {
        Name = "History",
        Dock = DockStyle.Fill,
        ReadOnly = true,
        Multiline = true,
        ScrollBars = ScrollBars.Vertical
    };
    public ComboBox OcrEngine { get; } = new()
    {
        Name = "OcrEngine",
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList
    };
    public Label State { get; } = Ui.Label("");

    public SettingsForm()
    {
        Ui.Style(this, "設定", new Size(760, 470));
        var root = Ui.Table(1, 8);
        foreach (int height in new[]
        {
            38,
            38,
            34,
            38,
            34,
            36,
            40
        }

        )
            root.RowStyles.Add(new(SizeType.Absolute, height));
        root.RowStyles.Add(new(SizeType.Percent, 100));
        root.Controls.Add(EnabledBox, 0, 0);
        root.Controls.Add(ReviewBox, 0, 1);
        root.Controls.Add(Ui.Label("Codex CLI"), 0, 2);
        root.Controls.Add(CodexPath, 0, 3);
        root.Controls.Add(Ui.Label("GPT-5.6 Luna / high"), 0, 4);
        root.Controls.Add(State, 0, 5);
        var engine = Ui.Table(2, 1);
        engine.Padding = Padding.Empty;
        engine.ColumnStyles.Add(new(SizeType.Absolute, 110));
        engine.ColumnStyles.Add(new(SizeType.Percent, 100));
        OcrEngine.Items.AddRange(["OneOCR (Snipping Tool)", "Windows.Media.Ocr"]);
        OcrEngine.SelectedIndex = 0;
        engine.Controls.Add(Ui.Label("OCRエンジン"), 0, 0);
        engine.Controls.Add(OcrEngine, 1, 0);
        root.Controls.Add(engine, 0, 6);
        root.Controls.Add(History, 0, 7);
        Controls.Add(root);
    }
}
