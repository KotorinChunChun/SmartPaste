namespace SmartPaste;

internal sealed partial class TrayApp
{
    private async Task PasteFiles(PasteSnapshot context)
    {
        IReadOnlyList<CopyChoice> choices = context.Files.Select(p => new CopyChoice(p)).ToArray();
        if (CopyReviewPolicy.NeedsReview(settings.AlwaysReview, choices))
        {
            using var dialog = new CopyForm(choices, context.Directory);
            activeDialog = dialog;
            if (dialog.ShowDialog(new ExplorerOwner(context.Window)) != DialogResult.OK) return;
            choices = dialog.Choices;
        }
        using var cancellation = new CancellationTokenSource();
        copyCancellation = cancellation;
        using var progress = new ProgressForm(cancellation);
        activeDialog = progress;
        var status = new Progress<string>(name =>
        {
            if (!progress.IsDisposed) progress.State.Text = name;
        });
        // 小さい自動コピーでは、従来と同じ500ms後まで進捗画面を表示しない。
        var copying = Task.Run(() => CopyService.CopyAsync(choices, context.Directory, status, cancellation.Token));
        if (await Task.WhenAny(copying, Task.Delay(500)) != copying) progress.Show(new ExplorerOwner(context.Window));
        var results = await copying;
        progress.Finished = true;
        progress.Close();
        copyCancellation = null;
        foreach (var result in results)
            Report(result.Error == null ? $"コピー：{result.Destination}" : $"{Path.GetFileName(result.Source)}：{result.Error}", result.Error != null);
        await Explorer.SelectCreatedAsync(context.Window, context.Directory, results.Where(r => r.Error == null && r.Destination != null).Select(r => r.Destination!).ToArray());
    }
}
