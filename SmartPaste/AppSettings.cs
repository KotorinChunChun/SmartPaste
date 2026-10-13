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
