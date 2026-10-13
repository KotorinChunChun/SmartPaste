using System.Text.Json;

namespace SmartPaste;
internal sealed partial class TrayApp : ApplicationContext
{
    private sealed class ExplorerOwner(nint handle) : IWin32Window
    {
        public nint Handle => handle;
    }

    private readonly SettingsForm form = new();
    private readonly NotifyIcon tray;
    private readonly Icon icon = AppIcon.Create();
    private readonly System.Windows.Forms.Timer timer = new()
    {
        Interval = 180
    };
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
        try
        {
            settings = File.Exists(AppSettings.PathName) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppSettings.PathName)) ?? new() : new();
        }
        catch (Exception ex)when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            settings = new();
            loadError = ex.Message;
        }

        dispatch = form.Handle;
        form.EnabledBox.Checked = settings.Enabled;
        form.ReviewBox.Checked = settings.AlwaysReview;
        form.CodexPath.Text = settings.CodexPath;
        if (!Enum.IsDefined(settings.OcrEngine))
            settings.OcrEngine = OcrEngineKind.OneOcr;
        form.OcrEngine.SelectedIndex = (int)settings.OcrEngine;
        form.OcrEngine.SelectedIndexChanged += (_, _) =>
        {
            settings.OcrEngine = (OcrEngineKind)form.OcrEngine.SelectedIndex;
            Persist();
        };
        form.EnabledBox.CheckedChanged += (_, _) =>
        {
            settings.Enabled = form.EnabledBox.Checked;
            Persist();
            Capture();
        };
        form.ReviewBox.CheckedChanged += (_, _) =>
        {
            settings.AlwaysReview = form.ReviewBox.Checked;
            Persist();
        };
        form.CodexPath.TextChanged += (_, _) =>
        {
            settings.CodexPath = form.CodexPath.Text;
            Persist();
        };
        form.FormClosing += (_, e) =>
        {
            if (!exiting)
            {
                e.Cancel = true;
                form.Hide();
            }
        };
        hook = new PasteHook(context => form.BeginInvoke(new Action(() => _ = Paste(context))));
        if (!Native.AddClipboardFormatListener(dispatch))
        {
            var error = new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            hook.Dispose();
            throw error;
        }

        form.ClipboardChanged += Capture;
        var menu = new ContextMenuStrip();
        var enabled = new ToolStripMenuItem("有効", null, (_, _) => form.EnabledBox.Checked = !form.EnabledBox.Checked)
        {
            Checked = settings.Enabled
        };
        form.EnabledBox.CheckedChanged += (_, _) => enabled.Checked = settings.Enabled;
        menu.Items.Add(enabled);
        menu.Items.Add("設定", null, (_, _) => ShowSettings());
        menu.Items.Add("終了", null, (_, _) => Exit());
        form.Icon = icon;
        tray = new NotifyIcon
        {
            Icon = icon,
            Text = "SmartPaste",
            ContextMenuStrip = menu,
            Visible = true
        };
        tray.DoubleClick += (_, _) => ShowSettings();
        timer.Tick += (_, _) => Capture();
        timer.Start();
        if (loadError != null)
            Report("設定の読み込みに失敗しました：" + loadError, true);
        if (!hidden)
            ShowSettings();
        management = new("smart-paste", form, ShowSettings, Exit);
    }

    private void Persist()
    {
        try
        {
            settings.Save();
        }
        catch (Exception ex)when (ex is IOException or UnauthorizedAccessException)
        {
            Report("設定を保存できません：" + ex.Message, true);
        }
    }
}
