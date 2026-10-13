using System.Text.Json;

namespace SmartPaste;

internal sealed partial class TrayApp
{
    private async Task Paste(PasteSnapshot context)
    {
        if (busy)
            return;
        busy = true;
        try
        {
            if (Native.GetForegroundWindow() != context.Window || Native.Focus(context.Window) != context.Focus || context.ClipboardSequence != Native.GetClipboardSequenceNumber() || !string.Equals(Explorer.DirectoryFor(context.Window), context.Directory, StringComparison.OrdinalIgnoreCase))
                throw new IOException("貼り付け先またはクリップボードが変わりました。もう一度貼り付けてください。");
            if (context.IsImage)
                await PasteImage(context);
            else
                await PasteFiles(context);
        }
        catch (Exception ex)
        {
            Report(ex.Message, true);
        }
        finally
        {
            activeDialog = null;
            copyCancellation = null;
            busy = false;
            hook.Complete();
            if (exitRequested)
                Exit();
        }
    }
}
