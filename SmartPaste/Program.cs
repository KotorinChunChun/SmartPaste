using System.Text.Json;

namespace SmartPaste;
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length > 0 && args[0] == "--demo-image")
        {
            var folder = args.Length > 1 ? args[1] : Path.GetTempPath();
            Directory.CreateDirectory(folder);
            using var bitmap = DemoImage();
            using var form = new ImageForm(bitmap, folder, CodexNames.FindExecutable() ?? "");
            Application.Run(form);
            return;
        }

        if (args.Length > 0 && args[0] == "--demo-copy")
        {
            using var form = new CopyForm([new CopyChoice("R01報告書_20261009.txt"), new CopyChoice("2024-02-28_会議01.txt")], Path.GetTempPath());
            Application.Run(form);
            return;
        }

        using var mutex = new Mutex(true, "Local\\SmartPaste.User." + Environment.UserName, out bool first);
        if (!first)
        {
            if (!args.Contains("--startup"))
                KotorinToys.Integration.ManagementEndpoint.ShowExisting("smart-paste");
            return;
        }

        try
        {
            using var app = new TrayApp(true);
            Application.Run(app);
        }
        catch (Exception ex)
        {
            using var error = new Form();
            Ui.Style(error, "起動エラー", new Size(760, 440));
            error.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, Text = ex.Message });
            Application.Run(error);
        }
    }

    internal static Bitmap DemoImage()
    {
        var image = new Bitmap(840, 520);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.White);
        using var font = new Font("Yu Gothic UI", 34);
        using var small = new Font("Yu Gothic UI", 23);
        graphics.DrawString("月次報告書", font, Brushes.Black, 44, 40);
        graphics.DrawString("2026年10月", small, Brushes.Black, 44, 116);
        graphics.FillRectangle(Brushes.SteelBlue, 48, 310, 100, 140);
        graphics.FillRectangle(Brushes.MediumSeaGreen, 196, 220, 100, 230);
        graphics.FillRectangle(Brushes.SandyBrown, 344, 170, 100, 280);
        graphics.DrawString("売上推移", small, Brushes.Black, 520, 300);
        return image;
    }
}
