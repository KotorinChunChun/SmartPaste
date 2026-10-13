namespace SmartPaste;
internal sealed class CopyForm : Form
{
    private readonly List<CopyChoice> choices;
    private readonly string directory;
    private readonly DataGridView grid = new()
    {
        Name = "FileList",
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle
    };
    private readonly ComboBox token = new()
    {
        Name = "TokenSelector",
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList
    };
    private readonly TextBox value = Ui.Text("NewValue");
    private readonly Label error = Ui.Label("");
    private readonly Button accept;
    private bool updating;
    public IReadOnlyList<CopyChoice> Choices => choices.Select(c => c.Clone()).ToArray();

    public CopyForm(IEnumerable<CopyChoice> choices, string directory)
    {
        this.choices = choices.Select(c => c.Clone()).ToList();
        this.directory = directory;
        Ui.Style(this, "コピー名", new Size(920, 540));
        var root = Ui.Table(1, 4);
        root.RowStyles.Add(new(SizeType.Absolute, 38));
        root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.Absolute, 42));
        root.RowStyles.Add(new(SizeType.Absolute, 80));
        root.Controls.Add(Ui.Label(directory), 0, 0);
        grid.Columns.Add("Original", "コピー元");
        grid.Columns.Add("Proposed", "貼り付け後");
        grid.RowTemplate.Height = 32;
        foreach (var choice in this.choices)
            grid.Rows.Add(choice.OriginalName, "");
        root.Controls.Add(grid, 0, 1);
        var fields = Ui.Table(4, 1);
        fields.ColumnStyles.Add(new(SizeType.Absolute, 86));
        fields.ColumnStyles.Add(new(SizeType.Percent, 55));
        fields.ColumnStyles.Add(new(SizeType.Absolute, 86));
        fields.ColumnStyles.Add(new(SizeType.Percent, 45));
        fields.Controls.Add(Ui.Label("変更箇所"), 0, 0);
        fields.Controls.Add(token, 1, 0);
        fields.Controls.Add(Ui.Label("変更後"), 2, 0);
        fields.Controls.Add(value, 3, 0);
        root.Controls.Add(fields, 0, 2);
        var bottom = Ui.Table(1, 2);
        bottom.RowStyles.Add(new(SizeType.Percent, 100));
        bottom.RowStyles.Add(new(SizeType.Absolute, 42));
        error.ForeColor = Color.Firebrick;
        bottom.Controls.Add(error, 0, 0);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };
        accept = Ui.Button("CopyButton", "コピー", (_, _) =>
        {
            RefreshPlan();
            if (accept!.Enabled)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        });
        var cancel = Ui.Button("CancelButton", "キャンセル", (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        });
        actions.Controls.Add(accept);
        actions.Controls.Add(cancel);
        bottom.Controls.Add(actions, 0, 1);
        root.Controls.Add(bottom, 0, 3);
        Controls.Add(root);
        AcceptButton = accept;
        CancelButton = cancel;
        grid.SelectionChanged += (_, _) => BindChoice();
        token.SelectedIndexChanged += (_, _) =>
        {
            if (updating || Selected is not { } choice || token.SelectedIndex < 0)
                return;
            choice.TokenIndex = token.SelectedIndex;
            choice.Edited = false;
            try
            {
                choice.Value = choice.Token!.InitialValue();
            }
            catch (ArgumentException ex)
            {
                error.Text = ex.Message;
                accept.Enabled = false;
                return;
            }

            updating = true;
            value.Text = choice.Value;
            updating = false;
            RefreshPlan();
        };
        value.TextChanged += (_, _) =>
        {
            if (!updating && Selected is { } choice)
            {
                choice.Value = value.Text;
                choice.Edited = true;
                RefreshPlan();
            }
        };
        if (grid.Rows.Count > 0)
            grid.CurrentCell = grid.Rows[0].Cells[0];
        BindChoice();
        RefreshPlan();
    }

    private CopyChoice? Selected => grid.CurrentRow is { } row ? choices[row.Index] : null;

    private void BindChoice()
    {
        if (Selected is not { } choice)
            return;
        updating = true;
        token.Items.Clear();
        foreach (var candidate in choice.Tokens)
            token.Items.Add(candidate);
        token.Enabled = choice.Tokens.Count > 1;
        if (choice.Tokens.Count > 0)
            token.SelectedIndex = choice.TokenIndex;
        value.Enabled = choice.Tokens.Count > 0;
        value.Text = choice.Value;
        updating = false;
    }

    private void RefreshPlan()
    {
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? failure = null;
        for (int i = 0; i < choices.Count; i++)
        {
            try
            {
                var name = Planner.Available(choices[i], directory, reserved);
                reserved.Add(name);
                grid.Rows[i].Cells[1].Value = name;
            }
            catch (Exception ex)when (ex is ArgumentException or IOException or ArgumentOutOfRangeException)
            {
                grid.Rows[i].Cells[1].Value = "";
                failure ??= $"{choices[i].OriginalName}：{ex.Message}";
            }
        }

        error.Text = failure ?? "";
        accept.Enabled = failure == null;
    }
}
