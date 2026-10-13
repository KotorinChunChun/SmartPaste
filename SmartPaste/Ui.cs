namespace SmartPaste;
internal static class Ui
{
    public static void Style(Form form, string title, Size size)
    {
        form.Text = WindowTitle.Create(title);
        form.Font = new Font("Yu Gothic UI", 12);
        form.ClientSize = size;
        form.MinimumSize = new Size(700, 440);
        form.StartPosition = FormStartPosition.CenterScreen;
        form.BackColor = Color.White;
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.Shown += (_, _) =>
        {
            Native.ShowWindow(form.Handle, 9);
            Native.SetForegroundWindow(form.Handle);
        };
    }

    public static Button Button(string name, string text, EventHandler? clicked = null)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(100, 36),
            Margin = new Padding(3)
        };
        if (clicked != null)
            button.Click += clicked;
        return button;
    }

    public static Label Label(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true
    };
    public static TableLayoutPanel Table(int columns, int rows) => new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = columns,
        RowCount = rows,
        Padding = new Padding(6),
        Margin = Padding.Empty
    };
    public static TextBox Text(string name, bool readOnly = false) => new()
    {
        Name = name,
        Dock = DockStyle.Fill,
        ReadOnly = readOnly,
        Margin = new Padding(3)
    };
}
