using SmartPaste;
using System.Drawing.Imaging;
using System.Text.Json;

internal static class Tests
{
    private static int passed;
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (var icon = AppIcon.Create())
        {
            Check(icon.Size == new Size(32, 32), "トレイの専用アイコン寸法");
            using var bitmap = icon.ToBitmap();
            Check(bitmap.GetPixel(16, 3).R > bitmap.GetPixel(16, 3).G, "トレイの紫の専用アイコン");
        }
        if (args.Length > 0 && args[0] == "--probe-explorer")
        {
            var hwnd = new nint(long.Parse(args[1]));
            Console.WriteLine(JsonSerializer.Serialize(new { Window = Native.Class(hwnd), Focus = Native.Class(Native.Focus(hwnd)), Directory = Explorer.DirectoryFor(hwnd), FocusedPath = Explorer.FocusedPath(hwnd) })); return 0;
        }
        if (args.Length > 0 && args[0] == "--clipboard-file")
        {
            var data = new DataObject(); data.SetData(DataFormats.FileDrop, args.Skip(1).ToArray());
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1u))); Clipboard.SetDataObject(data, true); return 0;
        }
        if (args.Length > 0 && args[0] == "--clipboard-image")
        {
            using var image = SmartPaste.Program.DemoImage(); Clipboard.SetImage(image); return 0;
        }
        var root = Path.Combine(Path.GetTempPath(), "SmartPaste-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var plainChoice = new CopyChoice(Path.Combine(root, "資料.txt"));
            var numericChoice = new CopyChoice(Path.Combine(root, "R01報告書.txt"));
            var combinedChoice = new CopyChoice(Path.Combine(root, "R01報告書_20261009.txt"));
            Check(!CopyReviewPolicy.NeedsReview(false, []), "確認方針：空対象は自動確認しない");
            Check(!CopyReviewPolicy.NeedsReview(false, [plainChoice]), "確認方針：単一の通常名は自動処理");
            Check(numericChoice.Tokens.Count == 1 && !CopyReviewPolicy.NeedsReview(false, [numericChoice]), "確認方針：単一数字候補は自動処理");
            Check(CopyReviewPolicy.NeedsReview(false, [plainChoice, numericChoice]), "確認方針：複数ファイルは確認する");
            Check(combinedChoice.Tokens.Count > 1 && CopyReviewPolicy.NeedsReview(false, [combinedChoice]), "確認方針：複数候補は確認する");
            Check(CopyReviewPolicy.NeedsReview(true, [plainChoice]), "確認方針：常に確認する設定を優先");
            var cases = new Dictionary<string, string>
            {
                ["2026年1月_月報.txt"] = "2026年2月_月報.txt", ["2026年12月_月報.txt"] = "2027年1月_月報.txt",
                ["2026年01月_月報.txt"] = "2026年02月_月報.txt", ["２０２６年１２月_月報.txt"] = "２０２７年１月_月報.txt",
                ["2026年1月31日_日報.txt"] = "2026年2月1日_日報.txt", ["2026.01.31_日報.txt"] = "2026.02.01_日報.txt",
                ["2026年12月31日_日報.txt"] = "2027年1月1日_日報.txt", ["2026年01月31日_日報.txt"] = "2026年02月1日_日報.txt",
                ["2026.12_月報.txt"] = "2027.01_月報.txt", ["2026-12_月報.txt"] = "2027-01_月報.txt",
                ["2026.01.31"] = "2026.02.01", ["月報2026.12"] = "月報2027.01",
                ["202611_報告.txt"] = "202612_報告.txt", ["202612_報告.txt"] = "202701_報告.txt",
                ["199912_報告.txt"] = "200001_報告.txt", ["209912_報告.txt"] = "210001_報告.txt",
                ["２０２６１２_報告.txt"] = "２０２７０１_報告.txt", ["202600_番号.txt"] = "202601_番号.txt",
                ["202613_番号.txt"] = "202614_番号.txt", ["1202612_番号.txt"] = "1202613_番号.txt",
                ["R01報告書.txt"] = "R02報告書.txt", ["報告009.txt"] = "報告010.txt", ["報告99.txt"] = "報告100.txt",
                ["報告９９.txt"] = "報告１００.txt", ["20240228_報告.txt"] = "20240229_報告.txt", ["20240229_報告.txt"] = "20240301_報告.txt",
                ["2023-02-28.txt"] = "2023-03-01.txt", ["2026_12_31.txt"] = "2027_01_01.txt", ["2026.10.09.txt"] = "2026.10.10.txt",
                ["2026年9月30日.txt"] = "2026年10月1日.txt", ["２０２６年１０月０９日.txt"] = "２０２６年１０月１０日.txt",
                ["version999999999999999999999999.txt"] = "version1000000000000000000000000.txt"
            };
            foreach (var test in cases) Check(new CopyChoice(test.Key).ProposedName == test.Value, $"繰り上げ {test.Key}");
            var templateDate = new DateTime(2026, 10, 9);
            foreach (var template in new[] { "yyyymmdd.txt", "yyyymmdd_報告書.xlsx", "報告書_YYYYMMDD.pdf", "yyyymmdd" })
                Check(new CopyChoice(template, templateDate).ProposedName == template.Replace("yyyymmdd", "20261009", StringComparison.OrdinalIgnoreCase), "今日の日付テンプレート " + template);
            Check(new CopyChoice("yyyymmdd.txt").ProposedName == DateTime.Today.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) + ".txt", "実行日のローカル日付を使用");
            Check(new CopyChoice("yyyymmdd.txt", new DateTime(2024, 2, 29)).ProposedName == "20240229.txt", "うるう日の今日の日付");
            Check(Naming.Detect("報告書.yyyymmdd").Count == 0, "日付テンプレートも拡張子は対象外");
            var templateToken = Naming.Detect("R01_yyyymmdd.txt")[1];
            Check(templateToken.IsTodayPlaceholder && templateToken.Label.StartsWith("今日の日付：") && Naming.Detect("R01_yyyymmdd.txt").Count == 2, "数字と今日の日付を別候補にする");
            Throws(() => templateToken.Validate("20260230"), "今日の日付の手動修正も実在する日付に制限");
            var monthToken = Naming.Detect("202612_報告.txt").Single();
            Check(monthToken.IsYearMonth && monthToken.Label.StartsWith("年月："), "年月は1候補として表示");
            Check(!Naming.Detect("202613_番号.txt").Single().IsYearMonth, "無効な月は通常数字");
            Check(!Naming.Detect("1202612_番号.txt").Single().IsYearMonth, "長い連番の一部を年月と判定しない");
            Check(Naming.Detect("20261231_報告.txt").Single().IsDate, "8桁年月日は翌日処理を維持");
            Check(Naming.Detect("2026年1月31日_日報.txt").Single().IsDate, "日本語年月日の月を別候補にしない");
            Check(Naming.Detect("2026.01.31_日報.txt").Single().IsDate, "ドット年月日の月を別候補にしない");
            Check(Naming.Detect("2026.02.30_日報.txt").All(t => !t.IsDate && !t.IsYearMonth), "不正日付の一部を年月と解釈しない");
            Check(Naming.Detect("202612_202701_版01.txt").Count == 3, "複数年月と連番は別候補");
            Check(Naming.Replace("202612_報告.txt", monthToken, "202702") == "202702_報告.txt", "年月のテキスト修正");
            Throws(() => monthToken.Validate("202713"), "月13の手動入力を拒否");
            Throws(() => monthToken.Validate("202700"), "月00の手動入力を拒否");
            Throws(() => monthToken.Validate("000001"), "年0000を拒否");
            Check(Naming.Detect("R01_2026-10-09_版02.txt").Count == 3, "日付の年月日を個別数字と重複検出しない");
            Check(Naming.Detect("readme.txt").Count == 0, "数字なし");
            Check(Naming.Detect("readme.mp4").Count == 0, "拡張子は対象外");
            Check(Naming.Detect("readme.202612").Count == 0 && Naming.Detect("readme.20261231").Count == 0, "数字だけの拡張子も年月・日付にしない");
            Check(!Naming.TryDate("20260230", out _), "不正日付");
            foreach (var bad in new[] { "CON.txt", "nul.PNG", "COM1.png", "LPT².png", "a/b.txt", "a.txt ", "a.", "" })
                Throws(() => Naming.ValidateFileName(bad), "禁止名 " + bad);
            Throws(() => Images.PngName(""), "空画像名");
            Check(Images.PngName("テスト") == "テスト.png", "PNG拡張子補完");
            Check(Images.Suggestion("売上:比較/図") == "売上比較図.png", "OCR候補の禁止文字除去");
            Check(Images.CleanText("月 次 報 告\r\nA B C") == "月次報告\r\nABC", "文章の半角スペースを除去し改行を維持");
            Check(Images.NameStem("image1.png") == "image1" && Images.NameStem("report.PNG") == "report", "入力用の拡張子除外");
            var composition = new CandidateSelection();
            Check(composition.Toggle("月次報告") && composition.Toggle("2026年10月") && composition.Toggle("売上推移"), "候補3つを選択");
            Check(!composition.Toggle("4つ目") && composition.Items.Count == 3, "4つ目の候補を拒否");
            Check(composition.Join("_") == "月次報告_2026年10月_売上推移", "選択順と指定区切りで連結");
            composition.Move(2, -1); Check(composition.Join("") == "月次報告売上推移2026年10月", "順番入れ替えと空の区切り");
            composition.Move(0, -1); composition.Move(2, 1); Check(composition.Items[0] == "月次報告" && composition.Items[2] == "2026年10月", "境界外の並べ替えを行わない");
            Check(composition.Toggle("売上推移") && composition.Toggle("新候補"), "再クリックで解除し別候補を追加");
            Check(new AppSettings().OcrEngine == OcrEngineKind.OneOcr, "OCRの初期値はOneOCR");
            var engineSettings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings { OcrEngine = OcrEngineKind.WindowsMedia }));
            Check(engineSettings!.OcrEngine == OcrEngineKind.WindowsMedia, "OCR切り替え設定を保存・復元");
            var source = Path.Combine(root, "R01報告書.txt"); File.WriteAllText(source, "元の内容");
            Check(ClipboardFiles.IsSameFolderCopy([source], root), "同じフォルダのコピーを対象にする");
            Check(ClipboardFiles.IsSameFolderCopy([source], root.ToUpperInvariant() + "\\."), "大文字小文字と末尾区切りを正規化");
            Check(!ClipboardFiles.IsSameFolderCopy([source], Path.Combine(root, "other")), "別フォルダへのコピーは対象外");
            Check(!ClipboardFiles.IsSameFolderCopy([source, Path.Combine(root, "other", "R03.txt")], root), "別フォルダ由来の混在はすべて標準処理");
            Check(!ClipboardFiles.IsSameFolderCopy([], root), "空のコピーを対象にしない");
            var monthSource = Path.Combine(root, "202612_月報.txt"); File.WriteAllText(monthSource, "月報");
            File.WriteAllText(Path.Combine(root, "202701_月報.txt"), "既存月報");
            File.WriteAllText(Path.Combine(root, "202702_月報.txt"), "既存月報2");
            var monthChoice = new CopyChoice(monthSource);
            Check(Planner.Available(monthChoice, root) == "202703_月報.txt", "年月の衝突は年越し後も月単位で進める");
            var monthResult = CopyService.CopyAsync([monthChoice], root).GetAwaiter().GetResult().Single();
            Check(monthResult.Error == null && Path.GetFileName(monthResult.Destination) == "202703_月報.txt", "年越しと衝突を含む年月コピー");
            var clipboard = new DataObject(); clipboard.SetData(DataFormats.FileDrop, new[] { source });
            clipboard.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1u)));
            Check(ClipboardFiles.FromData(clipboard)?.Single() == source, "ファイルコピーを判定");
            var templateSource = Path.Combine(root, "yyyymmdd_報告.txt"); File.WriteAllText(templateSource, "日付テンプレートの内容");
            clipboard.SetData(DataFormats.FileDrop, new[] { templateSource });
            Check(ClipboardFiles.FromData(clipboard)?.Single() == templateSource && ClipboardFiles.IsSameFolderCopy([templateSource], root), "数字なしの日付テンプレートコピーを対象にする");
            Check(!ClipboardFiles.IsSameFolderCopy([templateSource], Path.Combine(root, "other")), "日付テンプレートも別フォルダ貼り付けは対象外");
            var templateChoice = new CopyChoice(templateSource, templateDate);
            var firstTemplateCopy = CopyService.CopyAsync([templateChoice], root).GetAwaiter().GetResult().Single();
            Check(firstTemplateCopy.Error == null && Path.GetFileName(firstTemplateCopy.Destination) == "20261009_報告.txt" && File.ReadAllText(firstTemplateCopy.Destination!) == "日付テンプレートの内容", "今日の日付で実際にコピー");
            Directory.CreateDirectory(Path.Combine(root, "20261009_報告 (コピー).txt"));
            var secondTemplateCopy = CopyService.CopyAsync([templateChoice], root).GetAwaiter().GetResult().Single();
            Check(secondTemplateCopy.Error == null && Path.GetFileName(secondTemplateCopy.Destination) == "20261009_報告 (コピー 2).txt" && !File.Exists(Path.Combine(root, "20261010_報告.txt")), "衝突時も今日の日付を保ちファイル・フォルダを上書きしない");
            Check(Planner.Available(templateChoice, root, new HashSet<string> { "20261009_報告 (コピー 3).txt" }) == "20261009_報告 (コピー 4).txt", "同じ日付のバッチ予約をスキップ");
            templateChoice.Edited = true; Throws(() => Planner.Available(templateChoice, root), "手動修正した日付の衝突を拒否");
            clipboard.SetData(DataFormats.FileDrop, new[] { source });
            clipboard.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(5u)));
            Check(ClipboardFiles.FromData(clipboard)?.Single() == source, "ExplorerのCOPY|LINKをコピーと判定");
            clipboard.SetData("Preferred DropEffect", BitConverter.GetBytes(0x80000005u));
            Check(ClipboardFiles.FromData(clipboard)?.Single() == source, "付加フラグ付きのExplorerコピーを判定");
            foreach (uint effect in new uint[] { 0, 3, 4, 6, 7 })
            {
                clipboard.SetData("Preferred DropEffect", BitConverter.GetBytes(effect));
                Check(ClipboardFiles.FromData(clipboard) == null, $"コピー以外・移動を含む種別{effect}へ割り込まない");
            }
            clipboard.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(2u)));
            Check(ClipboardFiles.FromData(clipboard) == null, "切り取りは通常貼り付けへ渡す");
            clipboard.SetData("Preferred DropEffect", new byte[] { 1 });
            Check(ClipboardFiles.FromData(clipboard) == null, "不正な貼り付け種別へ割り込まない");
            clipboard.SetData("Preferred DropEffect", BitConverter.GetBytes(1u));
            clipboard.SetData(DataFormats.FileDrop, new[] { root });
            Check(ClipboardFiles.FromData(clipboard) == null, "フォルダへ割り込まない");
            var plain = Path.Combine(root, "readme.txt"); File.WriteAllText(plain, "普通のファイル");
            clipboard.SetData(DataFormats.FileDrop, new[] { plain });
            Check(ClipboardFiles.FromData(clipboard) == null, "数字なしだけのコピーへ割り込まない");
            File.WriteAllText(Path.Combine(root, "R02報告書.txt"), "既存の内容");
            var choice = new CopyChoice(source);
            Check(Planner.Available(choice, root) == "R03報告書.txt", "既存番号をスキップ");
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "R03報告書.txt" };
            Check(Planner.Available(choice, root, reserved) == "R04報告書.txt", "バッチ内の名前予約");
            choice.Edited = true; Throws(() => Planner.Available(choice, root), "手動変更名の衝突を拒否"); choice.Edited = false;
            var results = CopyService.CopyAsync([choice, choice.Clone()], root).GetAwaiter().GetResult();
            Check(results.Count == 2 && results.All(r => r.Error == null), "同じコピー元から2件コピー");
            Check(File.ReadAllText(source) == "元の内容" && File.ReadAllText(Path.Combine(root, "R02報告書.txt")) == "既存の内容", "コピー元・既存ファイルを保持");
            Check(File.Exists(Path.Combine(root, "R03報告書.txt")) && File.Exists(Path.Combine(root, "R04報告書.txt")), "連続した異なる名前");
            var concurrent = Task.WhenAll(CopyService.CopyAsync([choice], root), CopyService.CopyAsync([choice.Clone()], root)).GetAwaiter().GetResult().SelectMany(r => r).ToArray();
            Check(concurrent.All(r => r.Error == null) && concurrent.Select(r => r.Destination).Distinct().Count() == 2, "同時コピーでも同名を上書きしない");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Check(CopyService.CopyAsync([choice], root, cancellationToken: cancelled.Token).GetAwaiter().GetResult().Count == 0, "キャンセル時に作成しない");
            var missing = CopyService.CopyAsync([new CopyChoice(Path.Combine(root, "ない01.txt"))], root).GetAwaiter().GetResult();
            Check(missing.Single().Error != null && Directory.GetFiles(root, ".smartpaste-*.tmp").Length == 0, "失敗時の一時ファイル回収");
            using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb); bitmap.SetPixel(2, 3, Color.FromArgb(100, 10, 20, 30));
            var png = Images.Save(bitmap, root, "image1"); using (var roundtrip = new Bitmap(png)) Check(roundtrip.GetPixel(2, 3).A == 100, "PNG透明度を保持");
            Check(Images.NextName(root) == "image2.png", "image番号の繰り上げ");
            Directory.CreateDirectory(Path.Combine(root, "image10.png"));
            Check(Images.NextName(root) == "image11.png", "フォルダを含む最大番号の次を提案");
            Throws(() => Images.Save(bitmap, root, "image1"), "画像上書き防止");
            var json = JsonSerializer.Serialize(new { description = "テスト画像", names = Enumerable.Range(1, 10).Select(n => $"候補{n}.png").ToArray() });
            Check(CodexNames.Parse(json).Names.Length == 10, "AI候補10件");
            Throws(() => CodexNames.Parse("{\"description\":\"x\",\"names\":[\"a\"]}"), "AI候補不足");
            var start = CodexNames.StartInfo("C:/space dir/codex.exe", root, "x.png", "schema.json", "out.json");
            Check(start.ArgumentList.Contains("gpt-5.6-luna") && start.ArgumentList.Contains("model_reasoning_effort=\"high\"") && start.ArgumentList.Contains("read-only"), "指定モデル・high・read-only");
            Check(!start.UseShellExecute && start.CreateNoWindow, "シェル展開を使わないCLI起動");
            var fakeCli = Path.Combine(root, "fake-codex.ps1");
            File.WriteAllText(fakeCli, "$outputIndex = 0; for ($i = 0; $i -lt $args.Count; $i++) { if ($args[$i] -eq '--output-last-message') { $outputIndex = $i + 1 } }; [System.IO.File]::WriteAllText($args[$outputIndex], '" + json.Replace("'", "''") + "')");
            Check(CodexNames.RequestAsync(bitmap, fakeCli, default).GetAwaiter().GetResult().Names.Length == 10, "CLIプロセスの起動と応答読み取り");
            File.WriteAllText(fakeCli, "Start-Sleep -Seconds 30");
            using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(400)))
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                try { CodexNames.RequestAsync(bitmap, fakeCli, cancellation.Token).GetAwaiter().GetResult(); throw new Exception("キャンセルされませんでした。"); }
                catch (OperationCanceledException) { Check(watch.Elapsed < TimeSpan.FromSeconds(5), "AIキャンセルで子プロセスを終了"); }
            }
            using (var form = new CopyForm([new CopyChoice("R01報告書_20261009.txt")], root))
            {
                Layout(form, new Size(920, 540)); var grid = Find(form, "FileList");
                if (args.Contains("--mutation-copy")) { grid.Dock = DockStyle.None; grid.Size = new Size(240, 150); form.PerformLayout(); }
                Check(grid.Width > 750 && grid.Height > 300, "コピー一覧のレイアウト");
                var list = (DataGridView)grid; list.CurrentCell = list.Rows[0].Cells[0];
                var selector = (ComboBox)Find(form, "TokenSelector"); selector.SelectedIndex = 1;
                Check(form.Choices[0].ProposedName == "R01報告書_20261010.txt", "変更する日付候補を選択");
                ((TextBox)Find(form, "NewValue")).Text = "20261015";
                Check(form.Choices[0].ProposedName == "R01報告書_20261015.txt", "選択した候補をテキストで修正");
            }
            using (var form = new CopyForm([new CopyChoice("R01_yyyymmdd.txt")], root))
            {
                Layout(form, new Size(920, 540)); var list = (DataGridView)Find(form, "FileList"); list.CurrentCell = list.Rows[0].Cells[0];
                ((ComboBox)Find(form, "TokenSelector")).SelectedIndex = 1;
                Check(form.Choices[0].ProposedName == "R01_" + DateTime.Today.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) + ".txt", "確認画面で今日の日付候補を選択");
                ((TextBox)Find(form, "NewValue")).Text = "20261231";
                Check(form.Choices[0].ProposedName == "R01_20261231.txt", "今日の日付をテキストで修正");
            }
            using (var form = new ImageForm(bitmap, root, ""))
            {
                Layout(form, new Size(1120, 690)); var preview = Find(form, "ImagePreview"); var panel = Find(form, "NameCandidates");
                if (args.Contains("--mutation-image")) { preview.Dock = DockStyle.None; preview.Size = new Size(100, 50); form.PerformLayout(); }
                Check(preview.Width > 500 && preview.Height > 550, "画像プレビューのレイアウト");
                Check(panel.Width > 350 && panel.Height > 150, "候補ボタン領域のレイアウト");
                var input = (TextBox)Find(form, "ImageName"); var extension = Find(form, "ImageExtension");
                Check(input.Text == "image11" && extension.Text == ".png", "名前の初期値と固定PNG拡張子を分離");
                Check(input.Width > 300 && extension.Width >= 50 && extension.Left >= input.Right, "固定拡張子が入力欄と重ならない");
                var spinner = (BusySpinner)Find(form, "AiSpinner"); spinner.Running = true;
                Check(spinner.Running && spinner.Width >= 28 && spinner.Bottom <= spinner.Parent!.ClientSize.Height, "処理中の回転表示を配置");
                spinner.Running = false; Check(!spinner.Running, "完了時に回転表示を停止");
                var showCandidates = typeof(ImageForm).GetMethod("ShowCandidates", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                showCandidates.Invoke(form, [new[] { "月次報告.png", "2026年10月.png", "売上推移.png", "予備.png" }]);
                for (int i = 1; i <= 3; i++) Click(Find(form, $"NameCandidate{i}"));
                Layout(form, new Size(1120, 690));
                Check(input.Text == "月次報告_2026年10月_売上推移" && !Find(form, "NameCandidate4").Enabled, "画面で3候補を連結し4候補目を無効化");
                var selected = Find(form, "SelectedNames");
                if (args.Contains("--mutation-selection")) { selected.Dock = DockStyle.None; selected.Size = new Size(200, 30); form.PerformLayout(); }
                Check(selected.Width > 350 && selected.Height >= 105 && panel.Height > 150, "3つの選択欄と候補領域のレイアウト");
                Click(Find(form, "MoveCandidateUp3"));
                Check(input.Text == "月次報告_売上推移_2026年10月", "画面で候補の順序を変更");
                var delimiter = (TextBox)Find(form, "NameSeparator"); delimiter.Text = "-";
                Check(input.Text == "月次報告-売上推移-2026年10月", "区切り編集をファイル名へ反映");
                delimiter.Text = "/"; Check(!Find(form, "SaveImageButton").Enabled, "禁止文字を含む区切りで保存しない"); delimiter.Text = "";
                Click(Find(form, "RemoveCandidate2")); Check(input.Text == "月次報告2026年10月" && Find(form, "NameCandidate4").Enabled, "選択解除で追加可能になる");
                input.Text = "手動で修正"; showCandidates.Invoke(form, [new[] { "新しいAI候補.png" }]);
                Check(input.Text == "手動で修正", "新しい候補取得で手動入力を消さない");
                Click(Find(form, "RemoveCandidate1")); Click(Find(form, "RemoveCandidate1"));
                Check(input.Text == "image11", "全解除で初期名に戻る");
                Check(form.AcceptButton == null, "画像保存をフォーム全体のEnterに割り当てない");
                KeyDown(delimiter, Keys.Enter); Check(form.SavedPath == null, "区切り欄のEnterでは保存しない");
                input.Text = "禁止/名"; KeyDown(input, Keys.Enter); Check(form.SavedPath == null, "禁止名でEnterを押しても保存しない");
                input.Text = "image1"; KeyDown(input, Keys.Enter); Check(form.SavedPath == null, "既存名でEnterを押しても上書きしない");
                input.Text = "Enter保存"; KeyDown(input, Keys.Control | Keys.Enter); Check(form.SavedPath == null, "修飾キー付きEnterでは保存しない");
                var enter = KeyDown(input, Keys.Enter);
                Check(form.SavedPath == Path.Combine(root, "Enter保存.png") && File.Exists(form.SavedPath) && enter.SuppressKeyPress && enter.Handled, "名前欄のEnterでPNG保存しキーを消費");
            }
            using (var form = new SettingsForm())
            {
                Layout(form, new Size(760, 470)); Check(Find(form, "History").Width > 650 && Find(form, "History").Height > 150, "設定画面のレイアウト");
                Check(form.OcrEngine.Items.Count == 2 && form.OcrEngine.Width > 500, "設定のOCRエンジン選択が表示される");
            }
            SynchronizationContext.SetSynchronizationContext(null);
            if (args.Contains("--ocr-live"))
            {
                using var image = SmartPaste.Program.DemoImage();
                foreach (var engine in Enum.GetValues<OcrEngineKind>())
                {
                    var lines = Images.OcrAsync(image, default, engine).GetAwaiter().GetResult();
                    Console.WriteLine(engine + ": " + string.Join(" / ", lines));
                    Check(lines.Any(l => l.Contains("月次報告書")) && lines.All(l => !l.Contains(' ')), engine + " OCR実行と半角スペース除去");
                }
            }
            if (args.Contains("--ai-live"))
            {
                using var image = SmartPaste.Program.DemoImage();
                var answer = CodexNames.RequestAsync(image, CodexNames.FindExecutable() ?? throw new Exception("CLI不明"), default).GetAwaiter().GetResult();
                Console.WriteLine("AI: " + JsonSerializer.Serialize(answer)); Check(answer.Names.Length == 10, "Codex CLI 画像入力の実行");
            }
            Console.WriteLine($"PASS: {passed} checks"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(root, true); }
    }
    private static void Layout(Form form, Size size) { form.ClientSize = size; form.CreateControl(); form.PerformLayout(); foreach (Control child in form.Controls) child.PerformLayout(); }
    private static Control Find(Control root, string name) => root.Controls.Find(name, true).Single();
    // Hidden forms cannot PerformClick; invoke the same event without showing a test window.
    private static void Click(Control control) => typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(control, [EventArgs.Empty]);
    private static KeyEventArgs KeyDown(Control control, Keys key)
    {
        var args = new KeyEventArgs(key);
        typeof(Control).GetMethod("OnKeyDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(control, [args]);
        return args;
    }
    private static void Check(bool condition, string description) { if (!condition) throw new Exception("FAIL: " + description); passed++; }
    private static void Throws(Action action, string description) { try { action(); } catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException) { passed++; return; } throw new Exception("FAIL: " + description); }
}
