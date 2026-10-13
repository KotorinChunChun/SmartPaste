namespace SmartPaste;
internal sealed class BusySpinner : Control
{
    private readonly System.Windows.Forms.Timer animation = new()
    {
        Interval = 75
    };
    private int step;
    public bool Running
    {
        get => animation.Enabled;
        set
        {
            Visible = value;
            animation.Enabled = value;
            if (value)
                Invalidate();
        }
    }

    public BusySpinner()
    {
        Name = "AiSpinner";
        AccessibleName = "AI処理中";
        Size = new Size(30, 30);
        Margin = new Padding(6, 3, 0, 0);
        AccessibleRole = AccessibleRole.ProgressBar;
        TabStop = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        Visible = false;
        animation.Tick += (_, _) =>
        {
            step = (step + 1) % 12;
            Invalidate();
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float radius = Math.Min(Width, Height) * .33f, dot = Math.Max(3, Math.Min(Width, Height) * .13f);
        for (int i = 0; i < 12; i++)
        {
            double angle = (i - 3) * Math.PI / 6;
            using var brush = new SolidBrush(Color.FromArgb(40 + ((i - step + 12) % 12) * 18, 30, 105, 190));
            e.Graphics.FillEllipse(brush, Width / 2f + (float)Math.Cos(angle) * radius - dot / 2, Height / 2f + (float)Math.Sin(angle) * radius - dot / 2, dot, dot);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            animation.Dispose();
        base.Dispose(disposing);
    }
}
