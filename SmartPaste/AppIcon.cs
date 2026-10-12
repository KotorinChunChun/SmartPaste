using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SmartPaste;

internal static class AppIcon
{
    public static Icon Create()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var background = new SolidBrush(Color.FromArgb(116, 65, 190));
        graphics.FillEllipse(background, 1, 1, 30, 30);
        using var pen = new Pen(Color.White, 2);
        graphics.DrawRectangle(pen, 9, 9, 14, 16);
        using var white = new SolidBrush(Color.White);
        graphics.FillRectangle(white, 12, 6, 8, 5);
        graphics.DrawLine(pen, 12, 15, 20, 15);
        graphics.DrawLine(pen, 12, 19, 20, 19);
        nint handle = bitmap.GetHicon();
        try { using var borrowed = Icon.FromHandle(handle); return (Icon)borrowed.Clone(); }
        finally { DestroyIcon(handle); }
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint handle);
}
