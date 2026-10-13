namespace SmartPaste;
internal sealed class ProgressForm : Form
{
    public Label State { get; } = Ui.Label("コピー中…");
    public bool Finished { get; set; }

    public ProgressForm(CancellationTokenSource cancellation)
    {
        Ui.Style(this, "コピー中", new Size(700, 150));
        MinimumSize = new Size(520, 190);
        var root = Ui.Table(1, 2);
        root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.Absolute, 44));
        root.Controls.Add(State, 0, 0);
        root.Controls.Add(Ui.Button("StopCopy", "中止", (_, _) => cancellation.Cancel()), 0, 1);
        Controls.Add(root);
        FormClosing += (_, e) =>
        {
            if (!Finished)
            {
                cancellation.Cancel();
                e.Cancel = true;
            }
        };
    }
}
