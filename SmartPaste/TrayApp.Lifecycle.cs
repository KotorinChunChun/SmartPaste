using System.Text.Json;

namespace SmartPaste;
internal sealed partial class TrayApp
{
    private void ShowSettings()
    {
        form.Show();
        form.WindowState = FormWindowState.Normal;
        form.Activate();
    }

    private void Report(string message, bool show = false)
    {
        form.History.Text = $"{DateTime.Now:HH:mm:ss} {message}\r\n" + form.History.Text;
        if (form.History.TextLength > 20000)
            form.History.Text = form.History.Text[..20000];
        if (show)
            ShowSettings();
    }

    private void Exit()
    {
        if (busy)
        {
            exitRequested = true;
            copyCancellation?.Cancel();
            activeDialog?.Close();
            return;
        }

        exiting = true;
        management.Dispose();
        timer.Stop();
        timer.Dispose();
        Native.RemoveClipboardFormatListener(dispatch);
        hook.Dispose();
        tray.Visible = false;
        tray.Dispose();
        form.Dispose();
        icon.Dispose();
        ExitThread();
    }
}
