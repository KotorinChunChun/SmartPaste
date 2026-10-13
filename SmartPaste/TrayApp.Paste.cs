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
            else
            {
                IReadOnlyList<CopyChoice> choices = context.Files.Select(p => new CopyChoice(p)).ToArray();
                if (settings.AlwaysReview || choices.Count > 1 || choices.Any(c => c.Tokens.Count > 1))
                {
                    using var dialog = new CopyForm(choices, context.Directory);
                    activeDialog = dialog;
                    if (dialog.ShowDialog(new ExplorerOwner(context.Window)) != DialogResult.OK)
                        return;
                    choices = dialog.Choices;
                }

                using var cancellation = new CancellationTokenSource();
                copyCancellation = cancellation;
                using var progress = new ProgressForm(cancellation);
                activeDialog = progress;
                var status = new Progress<string>(name =>
                {
                    if (!progress.IsDisposed)
                        progress.State.Text = name;
                });
                // Delay the progress window to keep small automatic copies unobtrusive.
                var copying = Task.Run(() => CopyService.CopyAsync(choices, context.Directory, status, cancellation.Token));
                if (await Task.WhenAny(copying, Task.Delay(500)) != copying)
                    progress.Show(new ExplorerOwner(context.Window));
                var results = await copying;
                progress.Finished = true;
                progress.Close();
                copyCancellation = null;
                foreach (var result in results)
                    Report(result.Error == null ? $"コピー：{result.Destination}" : $"{Path.GetFileName(result.Source)}：{result.Error}", result.Error != null);
                await Explorer.SelectCreatedAsync(context.Window, context.Directory, results.Where(r => r.Error == null && r.Destination != null).Select(r => r.Destination!).ToArray());
            }
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
