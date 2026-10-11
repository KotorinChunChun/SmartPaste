namespace SmartPaste;

internal sealed record CopyResult(string Source, string? Destination, string? Error);

internal static class CopyService
{
    public static async Task<IReadOnlyList<CopyResult>> CopyAsync(IEnumerable<CopyChoice> choices, string directory,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var results = new List<CopyResult>();
        foreach (var choice in choices)
        {
            if (cancellationToken.IsCancellationRequested) break;
            string? temporary = null;
            try
            {
                if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("貼り付け先が見つかりません。");
                progress?.Report(choice.OriginalName);
                // CreateNew and final Move(overwrite:false) protect both temporary and destination files.
                temporary = Path.Combine(directory, $".smartpaste-{Guid.NewGuid():N}.tmp");
                using (var source = new FileStream(choice.Source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous))
                {
                    await source.CopyToAsync(target, cancellationToken);
                    await target.FlushAsync(cancellationToken);
                }
                File.SetLastWriteTimeUtc(temporary, File.GetLastWriteTimeUtc(choice.Source));
                bool moved = false;
                for (int retry = 0; retry < 100; retry++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var destination = Path.Combine(directory, Planner.Available(choice, directory));
                    try
                    {
                        File.Move(temporary!, destination, false);
                        temporary = null;
                        results.Add(new(choice.Source, destination, null));
                        moved = true;
                        break;
                    }
                    catch (IOException) when (!choice.Edited && (File.Exists(destination) || Directory.Exists(destination))) { }
                }
                if (!moved) throw new IOException("同名ファイルが追加され続けているためコピーを中止しました。");
            }
            catch (OperationCanceledException) { results.Add(new(choice.Source, null, "キャンセル")); break; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { results.Add(new(choice.Source, null, ex.Message)); }
            finally
            {
                if (temporary != null)
                {
                    try { File.Delete(temporary); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { results.Add(new(temporary, null, $"一時ファイルを削除できませんでした：{ex.Message}")); }
                }
            }
        }
        return results;
    }
}
