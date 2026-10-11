using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using Windows.Management.Deployment;

// Native ABI checked against ksasao/SnippingToolOcrSharp and b1tg/win11-oneocr.
// Microsoft binaries are loaded from the user's installed Snipping Tool.

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var folder = PrepareEngine();
            if (args is ["--status"])
            { Console.WriteLine(JsonSerializer.Serialize(new { Engine = "OneOCR", Directory = folder })); return 0; }
            if (args.Length != 1) throw new ArgumentException("画像のパスを指定してください。");
            nint library = NativeLibrary.Load(Path.Combine(folder, "oneocr.dll"), typeof(Program).Assembly,
                DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.SafeDirectories);
            NativeLibrary.SetDllImportResolver(typeof(Program).Assembly, (name, _, _) => name == "oneocr.dll" ? library : 0);
            // The short-lived helper isolates native failures and reclaims all resources.
            using var source = new Bitmap(args[0]);
            using var image = new Bitmap(source.Width + 100, source.Height + 100, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(image)) { graphics.Clear(Color.White); graphics.DrawImageUnscaled(source, 50, 50); }
            var bits = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var input = new OcrImage { Type = 3, Width = image.Width, Height = image.Height, Stride = bits.Stride, Data = bits.Scan0 };
                Console.WriteLine(JsonSerializer.Serialize(new { Lines = Recognize(folder, ref input) }));
            }
            finally { image.UnlockBits(bits); }
            return 0;
        }
        catch (Exception ex)
        { Console.WriteLine(JsonSerializer.Serialize(new { Error = ex.Message })); return 1; }
    }

    private static string PrepareEngine()
    {
        var package = new PackageManager().FindPackagesForUser("").Where(p => p.Id.Name == "Microsoft.ScreenSketch" && p.Id.Architecture.ToString() == "X64")
            .OrderByDescending(p => p.Id.Version.Major).ThenByDescending(p => p.Id.Version.Minor).ThenByDescending(p => p.Id.Version.Build).FirstOrDefault()
            ?? throw new FileNotFoundException("OneOCR を含む Snipping Tool が見つかりません。Snipping Tool を更新してください。");
        var installed = Path.Combine(package.InstalledLocation.Path, "SnippingTool");
        var destination = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartPaste", "OneOCR", package.Id.FullName);
        Directory.CreateDirectory(destination);
        foreach (var asset in new[] { "oneocr.dll", "onnxruntime.dll", "oneocr.onemodel" })
        {
            var from = Path.Combine(installed, asset); var to = Path.Combine(destination, asset);
            if (!File.Exists(from)) throw new FileNotFoundException("Snipping Tool の OneOCR コンポーネントが見つかりません。", from);
            if (File.Exists(to) && new FileInfo(to).Length == new FileInfo(from).Length) continue;
            var temporary = to + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.Copy(from, temporary); File.Move(temporary, to, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return destination;
    }

    private static string[] Recognize(string folder, ref OcrImage image)
    {
        long init = 0, pipeline = 0, options = 0, result = 0;
        try
        {
            Verify(OneApi.CreateOcrInitOptions(out init), "初期化");
            Verify(OneApi.OcrInitOptionsSetUseModelDelayLoad(init, 0), "モデル読込設定");
            // Fixed key for the installed model format, not an account credential.
            var modelKey = "kj)TGtrK>f]b[Piow.gU+nC@s" + new string('"', 6) + "4";
            Verify(OneApi.CreateOcrPipeline(Path.Combine(folder, "oneocr.onemodel"), modelKey, init, out pipeline), "モデル読込");
            Verify(OneApi.CreateOcrProcessOptions(out options), "認識設定");
            Verify(OneApi.OcrProcessOptionsSetMaxRecognitionLineCount(options, 1000), "行数設定");
            Verify(OneApi.RunOcrPipeline(pipeline, ref image, options, out result), "文字認識");
            Verify(OneApi.GetOcrLineCount(result, out long count), "行数取得");
            if (count < 0 || count > 1000) throw new InvalidDataException("OneOCR の行数が不正です。");
            var lines = new List<string>();
            for (long index = 0; index < count; index++)
            {
                Verify(OneApi.GetOcrLine(result, index, out long line), "行取得");
                Verify(OneApi.GetOcrLineContent(line, out nint content), "テキスト取得");
                var text = Marshal.PtrToStringUTF8(content);
                if (!string.IsNullOrWhiteSpace(text)) lines.Add(text);
            }
            return lines.ToArray();
        }
        finally
        {
            if (result != 0) OneApi.ReleaseOcrResult(result);
            if (options != 0) OneApi.ReleaseOcrProcessOptions(options);
            if (pipeline != 0) OneApi.ReleaseOcrPipeline(pipeline);
            if (init != 0) OneApi.ReleaseOcrInitOptions(init);
        }
    }
    private static void Verify(long status, string action) { if (status != 0) throw new IOException($"OneOCR の{action}に失敗しました（{status}）。"); }
}

[StructLayout(LayoutKind.Sequential)]
internal struct OcrImage { public int Type, Width, Height, Reserved; public long Stride; public nint Data; }

internal static class OneApi
{
    [DllImport("oneocr.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern long CreateOcrInitOptions(out long options);
    [DllImport("oneocr.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern long OcrInitOptionsSetUseModelDelayLoad(long options, byte value);
    [DllImport("oneocr.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern long CreateOcrPipeline([MarshalAs(UnmanagedType.LPUTF8Str)] string model, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, long options, out long pipeline);
    [DllImport("oneocr.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern long CreateOcrProcessOptions(out long options);
    [DllImport("oneocr.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern long OcrProcessOptionsSetMaxRecognitionLineCount(long options, long maximum);
    [DllImport("oneocr.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern long RunOcrPipeline(long pipeline, ref OcrImage image, long options, out long result);
    [DllImport("oneocr.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern long GetOcrLineCount(long result, out long count);
    [DllImport("oneocr.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern long GetOcrLine(long result, long index, out long line);
    [DllImport("oneocr.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern long GetOcrLineContent(long line, out nint content);
    [DllImport("oneocr.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern void ReleaseOcrResult(long result);
    [DllImport("oneocr.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern void ReleaseOcrProcessOptions(long options);
    [DllImport("oneocr.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern void ReleaseOcrPipeline(long pipeline);
    [DllImport("oneocr.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern void ReleaseOcrInitOptions(long options);
}
