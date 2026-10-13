namespace SmartPaste;
internal sealed class ImageForm : Form
{
    private readonly Bitmap image;
    private readonly string directory, executable;
    private readonly OcrEngineKind ocrEngine;
    private readonly TextBox name = Ui.Text("ImageName");
    private readonly TextBox text = new()
    {
        Name = "ImageDescription",
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical
    };
    private readonly FlowLayoutPanel candidates = new()
    {
        Name = "NameCandidates",
        Dock = DockStyle.Fill,
        AutoScroll = true,
        WrapContents = true
    };
    private readonly CandidateSelection selection = new();
    private readonly TextBox separator = Ui.Text("NameSeparator");
    private readonly Label selectionCount = Ui.Label("0/3");
    private readonly TableLayoutPanel selected = Ui.Table(4, 3);
    private readonly RowStyle selectedHeight = new(SizeType.Absolute, 0);
    private readonly string initialName;
    private readonly Label error = Ui.Label("");
    private readonly Button save, ocr, ai;
    private readonly BusySpinner spinner = new();
    private readonly CancellationTokenSource lifetime = new();
    public string? SavedPath { get; private set; }

    public event Action<string>? SeparatorChanged;
    public ImageForm(Bitmap image, string directory, string executable, OcrEngineKind ocrEngine = OcrEngineKind.OneOcr, string nameSeparator = "_")
    {
        this.image = image;
        this.directory = directory;
        this.executable = executable;
        this.ocrEngine = ocrEngine;
        Ui.Style(this, "画像を保存", new Size(1120, 690));
        MinimumSize = new Size(900, 680);
        var root = Ui.Table(2, 1);
        root.ColumnStyles.Add(new(SizeType.Percent, 57));
        root.ColumnStyles.Add(new(SizeType.Percent, 43));
        var preview = new PictureBox
        {
            Name = "ImagePreview",
            Dock = DockStyle.Fill,
            Image = image,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(242, 244, 247)
        };
        root.Controls.Add(preview, 0, 0);
        var right = Ui.Table(1, 10);
        right.RowStyles.Add(new(SizeType.Absolute, 34));
        right.RowStyles.Add(new(SizeType.Absolute, 34));
        right.RowStyles.Add(new(SizeType.Absolute, 40));
        right.RowStyles.Add(new(SizeType.Absolute, 44));
        right.RowStyles.Add(new(SizeType.Absolute, 84));
        right.RowStyles.Add(new(SizeType.Percent, 100));
        right.RowStyles.Add(new(SizeType.Absolute, 40));
        right.RowStyles.Add(selectedHeight);
        right.RowStyles.Add(new(SizeType.Absolute, 44));
        right.RowStyles.Add(new(SizeType.Absolute, 44));
        right.Controls.Add(Ui.Label(directory), 0, 0);
        right.Controls.Add(Ui.Label("ファイル名"), 0, 1);
        initialName = Images.NameStem(Images.NextName(directory));
        name.Text = initialName;
        var filename = Ui.Table(2, 1);
        filename.Padding = Padding.Empty;
        filename.ColumnStyles.Add(new(SizeType.Percent, 100));
        filename.ColumnStyles.Add(new(SizeType.Absolute, 58));
        var extension = Ui.Label(".png");
        extension.Name = "ImageExtension";
        filename.Controls.Add(name, 0, 0);
        filename.Controls.Add(extension, 1, 0);
        right.Controls.Add(filename, 0, 2);
        var tools = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill
        };
        ocr = Ui.Button("OcrButton", ocrEngine == OcrEngineKind.OneOcr ? "OCR (OneOCR)" : "OCR (Windows)", async (_, _) => await Generate(false));
        ai = Ui.Button("AiButton", "AIに依頼", async (_, _) => await Generate(true));
        tools.Controls.Add(ocr);
        tools.Controls.Add(ai);
        tools.Controls.Add(spinner);
        right.Controls.Add(tools, 0, 3);
        right.Controls.Add(text, 0, 4);
        right.Controls.Add(candidates, 0, 5);
        var joinFields = Ui.Table(3, 1);
        joinFields.Padding = Padding.Empty;
        joinFields.ColumnStyles.Add(new(SizeType.Absolute, 94));
        joinFields.ColumnStyles.Add(new(SizeType.Percent, 100));
        joinFields.ColumnStyles.Add(new(SizeType.Absolute, 44));
        separator.Text = nameSeparator;
        selectionCount.Name = "SelectionCount";
        joinFields.Controls.Add(Ui.Label("区切り文字"), 0, 0);
        joinFields.Controls.Add(separator, 1, 0);
        joinFields.Controls.Add(selectionCount, 2, 0);
        right.Controls.Add(joinFields, 0, 6);
        selected.Name = "SelectedNames";
        selected.Padding = Padding.Empty;
        selected.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++)
        {
            selected.ColumnStyles.Add(new(SizeType.Absolute, 40));
            selected.RowStyles.Add(new(SizeType.Absolute, 36));
        }

        right.Controls.Add(selected, 0, 7);
        error.ForeColor = Color.Firebrick;
        right.Controls.Add(error, 0, 8);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };
        save = Ui.Button("SaveImageButton", "保存", (_, _) =>
        {
            SaveImage();
        });
        var cancel = Ui.Button("CancelImageButton", "キャンセル", (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        });
        actions.Controls.Add(save);
        actions.Controls.Add(cancel);
        right.Controls.Add(actions, 0, 9);
        root.Controls.Add(right, 1, 0);
        Controls.Add(root);
        CancelButton = cancel;
        name.TextChanged += (_, _) => ValidateName();
        FormClosed += (_, _) => lifetime.Cancel();
        name.KeyDown += (_, key) =>
        {
            if (key.KeyCode != Keys.Enter || key.Modifiers != Keys.None)
                return;
            key.Handled = true;
            key.SuppressKeyPress = true;
            if (save.Enabled)
                SaveImage();
        };
        separator.TextChanged += (_, _) =>
        {
            UpdateComposition(selection.Items.Count > 0);
            SeparatorChanged?.Invoke(separator.Text);
        };
        ValidateName();
    }

    private void SaveImage()
    {
        ValidateName();
        if (!save.Enabled)
            return;
        try
        {
            SavedPath = Images.Save(image, directory, name.Text);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            error.Text = ex is IOException && File.Exists(Path.Combine(directory, Images.PngName(name.Text))) ? "同じ名前のファイルが存在します。" : ex.Message;
        }
    }

    private void ShowCandidates(IEnumerable<string> names)
    {
        foreach (Control old in candidates.Controls.Cast<Control>().ToArray())
            old.Dispose();
        int index = 0;
        foreach (var candidate in names.Select(Images.NameStem).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string part = candidate;
            var button = Ui.Button($"NameCandidate{++index}", part, (_, _) =>
            {
                if (selection.Toggle(part))
                    UpdateComposition();
            });
            button.Tag = part;
            candidates.Controls.Add(button);
        }

        UpdateComposition(false);
    }

    private void UpdateComposition(bool selectionChanged = true)
    {
        if (selectionChanged)
            name.Text = selection.Items.Count > 0 ? selection.Join(separator.Text) : initialName;
        selectionCount.Text = $"{selection.Items.Count}/3";
        foreach (Button button in candidates.Controls.OfType<Button>())
        {
            string part = (string)button.Tag!;
            int index = selection.IndexOf(part);
            button.Text = index >= 0 ? $"{index + 1}. {part}" : part;
            button.Enabled = index >= 0 || selection.Items.Count < CandidateSelection.Maximum;
            button.BackColor = index >= 0 ? Color.FromArgb(214, 232, 250) : SystemColors.Control;
        }

        selected.SuspendLayout();
        foreach (Control old in selected.Controls.Cast<Control>().ToArray())
            old.Dispose();
        selectedHeight.Height = selected.RowStyles.Cast<RowStyle>().Take(selection.Items.Count).Sum(row => row.Height);
        for (int i = 0; i < selection.Items.Count; i++)
        {
            int row = i;
            var label = Ui.Label($"{i + 1}. {selection.Items[i]}");
            label.Name = $"SelectedName{i + 1}";
            selected.Controls.Add(label, 0, i);
            var up = OrderButton($"MoveCandidateUp{i + 1}", "↑", () =>
            {
                selection.Move(row, -1);
                UpdateComposition();
            });
            up.Enabled = i > 0;
            var down = OrderButton($"MoveCandidateDown{i + 1}", "↓", () =>
            {
                selection.Move(row, 1);
                UpdateComposition();
            });
            down.Enabled = i < selection.Items.Count - 1;
            var remove = OrderButton($"RemoveCandidate{i + 1}", "×", () =>
            {
                selection.Remove(row);
                UpdateComposition();
            });
            selected.Controls.Add(up, 1, i);
            selected.Controls.Add(down, 2, i);
            selected.Controls.Add(remove, 3, i);
        }

        selected.ResumeLayout(true);
        ValidateName();
    }

    private static Button OrderButton(string name, string label, Action action)
    {
        var button = Ui.Button(name, label, (_, _) => action());
        button.AutoSize = false;
        button.MinimumSize = Size.Empty;
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(2);
        return button;
    }

    private void ValidateName()
    {
        try
        {
            var png = Images.PngName(name.Text);
            if (File.Exists(Path.Combine(directory, png)) || Directory.Exists(Path.Combine(directory, png)))
                throw new IOException("同じ名前が存在します。");
            error.Text = "";
            save.Enabled = true;
        }
        catch (Exception ex)when (ex is ArgumentException or IOException)
        {
            error.Text = ex.Message;
            save.Enabled = false;
        }
    }

    private async Task Generate(bool useAi)
    {
        ocr.Enabled = ai.Enabled = false;
        spinner.Running = true;
        error.Text = useAi ? "AIに依頼中…" : "文字を認識中…";
        try
        {
            string[] names;
            // Own clone remains alive until the asynchronous operation finishes, even if the dialog closes.
            using var clone = new Bitmap(image);
            if (useAi)
            {
                if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
                    throw new FileNotFoundException("Codex CLI が見つかりません。設定で実行ファイルを指定してください。");
                var result = await CodexNames.RequestAsync(clone, executable, lifetime.Token);
                if (IsDisposed || lifetime.IsCancellationRequested)
                    return;
                text.Text = result.Description;
                names = result.Names;
            }
            else
            {
                var lines = await Images.OcrAsync(clone, lifetime.Token, ocrEngine);
                if (IsDisposed || lifetime.IsCancellationRequested)
                    return;
                text.Text = string.Join(Environment.NewLine, lines);
                names = lines.Select(Images.Suggestion).Where(n => n.Length > 0).Distinct().Take(40).ToArray();
            }

            if (IsDisposed)
                return;
            ShowCandidates(names);
            error.Text = names.Length == 0 ? "文字を検出できませんでした。" : "";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                error.Text = "処理に失敗しました。";
                text.Text = ex.Message;
            }
        }
        finally
        {
            if (!IsDisposed)
            {
                spinner.Running = false;
                ocr.Enabled = ai.Enabled = true;
            }
        }
    }
}
