using System.Text.Json;

namespace SmartPaste;

internal sealed class AppSettings
{
    public bool Enabled { get; set; } = true;
    public bool AlwaysReview { get; set; }
    public OcrEngineKind OcrEngine { get; set; } = OcrEngineKind.OneOcr;
    public string ImageSeparator { get; set; } = "_";
    public string CodexPath { get; set; } = CodexNames.FindExecutable() ?? "";
    public static string PathName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartPaste", "settings.json");
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        var temporary = PathName + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, PathName, true);
    }
}

internal sealed class TrayApp : ApplicationContext
{
    private sealed class ExplorerOwner(nint handle) : IWin32Window { public nint Handle => handle; }
    private readonly SettingsForm form = new();
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 180 };
    private readonly PasteHook hook;
    private readonly AppSettings settings;
    private readonly nint dispatch;
    private readonly KotorinToys.Integration.ManagementEndpoint management;
    private uint lastSequence = uint.MaxValue;
    private string[]? clipboardFiles;
    private bool clipboardImage;
    private bool exiting, busy, exitRequested, capturing;
    private CancellationTokenSource? copyCancellation;
    private Form? activeDialog;

    public TrayApp(bool hidden)
    {
        string? loadError = null;
        try { settings = File.Exists(AppSettings.PathName) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppSettings.PathName)) ?? new() : new(); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { settings = new(); loadError = ex.Message; }
        dispatch = form.Handle;
        form.EnabledBox.Checked = settings.Enabled; form.ReviewBox.Checked = settings.AlwaysReview; form.CodexPath.Text = settings.CodexPath;
        if (!Enum.IsDefined(settings.OcrEngine)) settings.OcrEngine = OcrEngineKind.OneOcr;
        form.OcrEngine.SelectedIndex = (int)settings.OcrEngine;
        form.OcrEngine.SelectedIndexChanged += (_, _) => { settings.OcrEngine = (OcrEngineKind)form.OcrEngine.SelectedIndex; Persist(); };
        form.EnabledBox.CheckedChanged += (_, _) => { settings.Enabled = form.EnabledBox.Checked; Persist(); Capture(); };
        form.ReviewBox.CheckedChanged += (_, _) => { settings.AlwaysReview = form.ReviewBox.Checked; Persist(); };
        form.CodexPath.TextChanged += (_, _) => { settings.CodexPath = form.CodexPath.Text; Persist(); };
        form.FormClosing += (_, e) => { if (!exiting) { e.Cancel = true; form.Hide(); } };
        hook = new PasteHook(context => form.BeginInvoke(new Action(() => _ = Paste(context))));
        if (!Native.AddClipboardFormatListener(dispatch))
        {
            var error = new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            hook.Dispose(); throw error;
        }
        form.ClipboardChanged += Capture;
        var menu = new ContextMenuStrip();
        var enabled = new ToolStripMenuItem("有効", null, (_, _) => form.EnabledBox.Checked = !form.EnabledBox.Checked) { Checked = settings.Enabled };
        form.EnabledBox.CheckedChanged += (_, _) => enabled.Checked = settings.Enabled;
        menu.Items.Add(enabled); menu.Items.Add("設定", null, (_, _) => ShowSettings()); menu.Items.Add("終了", null, (_, _) => Exit());
        tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "賢い名付け", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowSettings();
        timer.Tick += (_, _) => Capture(); timer.Start();
        if (loadError != null) Report("設定の読み込みに失敗しました：" + loadError, true);
        if (!hidden) ShowSettings();
        management = new("smart-paste", form, ShowSettings, Exit);
    }
    private void Persist()
    {
        try { settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Report("設定を保存できません：" + ex.Message, true); }
    }
    private void ShowSettings() { form.Show(); form.WindowState = FormWindowState.Normal; form.Activate(); }
    private void Report(string message, bool show = false)
    {
        form.History.Text = $"{DateTime.Now:HH:mm:ss} {message}\r\n" + form.History.Text;
        if (form.History.TextLength > 20000) form.History.Text = form.History.Text[..20000];
        if (show) ShowSettings();
    }
    private void Capture()
    {
        if (capturing) return;
        if (!settings.Enabled) { hook.Snapshot = null; form.State.Text = "無効"; return; }
        capturing = true;
        try
        {
            var window = Native.GetForegroundWindow(); var focus = Native.Focus(window);
            if (!Explorer.IsFileView(window, focus)) { hook.Snapshot = null; form.State.Text = "待機中"; return; }
            uint sequence = Native.GetClipboardSequenceNumber();
            if (sequence != lastSequence)
            {
                clipboardFiles = ClipboardFiles.ReadCopy();
                // File-drop data has priority: image files stay files; cut/folders are passed through.
                clipboardImage = !Clipboard.ContainsFileDropList() && Images.ClipboardHasImage();
                lastSequence = sequence;
            }
            if (clipboardFiles == null && !clipboardImage) { hook.Snapshot = null; form.State.Text = "待機中"; return; }
            var directory = Explorer.DirectoryFor(window);
            if (directory == null) { hook.Snapshot = null; form.State.Text = "貼り付け先を確認できません"; return; }
            if (!clipboardImage && !ClipboardFiles.IsSameFolderCopy(clipboardFiles ?? [], directory))
            { hook.Snapshot = null; form.State.Text = "待機中"; return; }
            if (sequence != Native.GetClipboardSequenceNumber()) { hook.Snapshot = null; return; }
            hook.Snapshot = new(window, focus, sequence, directory, clipboardFiles ?? [], clipboardImage, Environment.TickCount64);
            form.State.Text = "有効";
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or IOException or ArgumentException)
        { hook.Snapshot = null; lastSequence = uint.MaxValue; form.State.Text = "待機中"; }
        finally { capturing = false; }
    }
    private async Task Paste(PasteSnapshot context)
    {
        if (busy) return;
        busy = true;
        try
        {
            if (Native.GetForegroundWindow() != context.Window || Native.Focus(context.Window) != context.Focus
                || context.ClipboardSequence != Native.GetClipboardSequenceNumber()
                || !string.Equals(Explorer.DirectoryFor(context.Window), context.Directory, StringComparison.OrdinalIgnoreCase))
                throw new IOException("貼り付け先またはクリップボードが変わりました。もう一度貼り付けてください。");
            if (context.IsImage)
            {
                using var bitmap = Images.ReadClipboard();
                if (context.ClipboardSequence != Native.GetClipboardSequenceNumber()) throw new IOException("画像の読み取り中にクリップボードが変わりました。もう一度貼り付けてください。");
                using var dialog = new ImageForm(bitmap, context.Directory, settings.CodexPath, settings.OcrEngine, settings.ImageSeparator ?? "_");
                dialog.SeparatorChanged += separator => { settings.ImageSeparator = separator; Persist(); };
                activeDialog = dialog;
                if (dialog.ShowDialog(new ExplorerOwner(context.Window)) == DialogResult.OK && dialog.SavedPath is { } saved)
                {
                    Report($"保存：{saved}");
                    await Explorer.SelectCreatedAsync(context.Window, context.Directory, [saved]);
                }
            }
            else
            {
                IReadOnlyList<CopyChoice> choices = context.Files.Select(p => new CopyChoice(p)).ToArray();
                if (settings.AlwaysReview || choices.Count > 1 || choices.Any(c => c.Tokens.Count > 1))
                {
                    using var dialog = new CopyForm(choices, context.Directory); activeDialog = dialog;
                    if (dialog.ShowDialog(new ExplorerOwner(context.Window)) != DialogResult.OK) return;
                    choices = dialog.Choices;
                }
                using var cancellation = new CancellationTokenSource(); copyCancellation = cancellation;
                using var progress = new ProgressForm(cancellation); activeDialog = progress;
                var status = new Progress<string>(name => { if (!progress.IsDisposed) progress.State.Text = name; });
                // Delay the progress window to keep small automatic copies unobtrusive.
                var copying = Task.Run(() => CopyService.CopyAsync(choices, context.Directory, status, cancellation.Token));
                if (await Task.WhenAny(copying, Task.Delay(500)) != copying) progress.Show(new ExplorerOwner(context.Window));
                var results = await copying;
                progress.Finished = true; progress.Close(); copyCancellation = null;
                foreach (var result in results) Report(result.Error == null ? $"コピー：{result.Destination}" : $"{Path.GetFileName(result.Source)}：{result.Error}", result.Error != null);
                await Explorer.SelectCreatedAsync(context.Window, context.Directory,
                    results.Where(r => r.Error == null && r.Destination != null).Select(r => r.Destination!).ToArray());
            }
        }
        catch (Exception ex) { Report(ex.Message, true); }
        finally { activeDialog = null; copyCancellation = null; busy = false; hook.Complete(); if (exitRequested) Exit(); }
    }
    private void Exit()
    {
        if (busy) { exitRequested = true; copyCancellation?.Cancel(); activeDialog?.Close(); return; }
        exiting = true; management.Dispose(); timer.Stop(); timer.Dispose(); Native.RemoveClipboardFormatListener(dispatch); hook.Dispose(); tray.Visible = false; tray.Dispose(); form.Dispose(); ExitThread();
    }
}

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length > 0 && args[0] == "--demo-image")
        {
            var folder = args.Length > 1 ? args[1] : Path.GetTempPath(); Directory.CreateDirectory(folder);
            using var bitmap = DemoImage(); using var form = new ImageForm(bitmap, folder, CodexNames.FindExecutable() ?? ""); Application.Run(form); return;
        }
        if (args.Length > 0 && args[0] == "--demo-copy")
        {
            using var form = new CopyForm([new CopyChoice("R01報告書_20261009.txt"), new CopyChoice("2024-02-28_会議01.txt")], Path.GetTempPath()); Application.Run(form); return;
        }
        using var mutex = new Mutex(true, "Local\\SmartPaste.User." + Environment.UserName, out bool first);
        if (!first) return;
        try { using var app = new TrayApp(true); Application.Run(app); }
        catch (Exception ex)
        {
            using var error = new Form(); Ui.Style(error, "起動エラー", new Size(760, 440));
            error.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, Text = ex.Message }); Application.Run(error);
        }
    }
    internal static Bitmap DemoImage()
    {
        var image = new Bitmap(840, 520);
        using var graphics = Graphics.FromImage(image); graphics.Clear(Color.White);
        using var font = new Font("Yu Gothic UI", 34); using var small = new Font("Yu Gothic UI", 23);
        graphics.DrawString("月次報告書", font, Brushes.Black, 44, 40);
        graphics.DrawString("2026年10月", small, Brushes.Black, 44, 116);
        graphics.FillRectangle(Brushes.SteelBlue, 48, 310, 100, 140);
        graphics.FillRectangle(Brushes.MediumSeaGreen, 196, 220, 100, 230);
        graphics.FillRectangle(Brushes.SandyBrown, 344, 170, 100, 280);
        graphics.DrawString("売上推移", small, Brushes.Black, 520, 300);
        return image;
    }
}
