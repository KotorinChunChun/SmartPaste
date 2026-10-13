namespace SmartPaste;

internal sealed partial class TrayApp
{
    private async Task PasteImage(PasteSnapshot context)
    {
        using var bitmap = Images.ReadClipboard();
        if (context.ClipboardSequence != Native.GetClipboardSequenceNumber())
            throw new IOException("画像の読み取り中にクリップボードが変わりました。もう一度貼り付けてください。");
        using var dialog = new ImageForm(bitmap, context.Directory, settings.CodexPath, settings.OcrEngine, settings.ImageSeparator ?? "_");
        dialog.SeparatorChanged += separator =>
        {
            settings.ImageSeparator = separator;
            Persist();
        };
        activeDialog = dialog;
        if (dialog.ShowDialog(new ExplorerOwner(context.Window)) == DialogResult.OK && dialog.SavedPath is { } saved)
        {
            Report($"保存：{saved}");
            await Explorer.SelectCreatedAsync(context.Window, context.Directory, [saved]);
        }
    }
}
