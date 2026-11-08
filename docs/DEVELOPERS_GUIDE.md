# 開発者ガイド

リポジトリのルートで実行します。

## 開発と検証

このフォルダを独立した Git リポジトリとして管理します。

```powershell
pwsh -File scripts/build.ps1 -Test
pwsh -File scripts/publish.ps1
```

実 OCR と AI を合成画像で検証する場合は、`build.ps1 -Test -Live` を使います。AI 呼び出しは Codex の利用枠を消費します。

READMEの画像説明に使う実ファイル出力は、既存のテスト入口へ出力先を渡して実行します。指定先だけを新規作成し、設定・履歴・クリップボード・Explorerは変更しません。

```powershell
$output = 'C:\path\to\SmartPaste\dev\history\v0.1.6\logs\readme-cli-sample-YYYYMMDD-HHMMSS\output'
dotnet publish SmartPaste.Tests/SmartPaste.Tests.csproj -c Release -r win-x64 --self-contained true -o $output
& "$output\SmartPaste.Tests.exe" --sample-output "$output\sample"
```

`--sample-output` は連番（`R01`の衝突後）、年月（`202612`から既存の`202701`・`202702`を越えた名前）、実OneOCRの候補連結によるPNGを作成し、コピー元・保存先・認識行・出力ファイルを `sample-output.json` に記録します。Explorerへの物理貼り付けはこのCLI検証に含みません。

UI と Explorer の実機検証スクリプトは `scripts/ui-smoke.ps1` と `scripts/explorer-smoke.ps1` です。`ui-smoke.ps1 -Ai` で AI ボタンと候補10件の選択も検証します。`-EnterSave` で実際に Enter を送り、名前欄では保存・区切り欄や無効名では保存しないことを確認します。これらは `windows-ui-automation` スキルのヘルパーを使うため、別 PC では `-Toolkit` にヘルパーのパスを指定してください。実機入力を使う検証の間は PC 操作を控えてください。

| 構成 | 内容 |
| --- | --- |
| `SmartPaste/Naming.cs` | 数字・日付の検出、繰り上げ、空き名の選択 |
| `SmartPaste/CopyService.cs` | 上書きしないファイルコピー、一時ファイルの回収 |
| `SmartPaste/Explorer.cs` | キーボードフック、Explorer の貼り付け先・クリップボード判定 |
| `SmartPaste/Images.cs` | PNG 保存、Windows OCR、Codex CLI 呼び出し |
| `SmartPaste/CandidateSelection.cs` | 最大3候補の選択、連結、並べ替え |
| `SmartPaste.Ocr/` | Snipping Tool の OneOCR 自動検出と専用プロセスでの認識 |
| `SmartPaste/Ui.cs`、各`*Form.cs` | 共通UI部品とコピー候補、画像保存、設定、進捗の個別画面 |
| `SmartPaste/Program.cs`、`AppSettings.cs` | 起動入口と設定の保存 |
| `SmartPaste/TrayApp*.cs`、`CopyReviewPolicy.cs` | 常駐初期化、監視、画像／ファイル貼付、終了、確認要否の方針 |
| `SmartPaste.Tests/` | 命名・コピー・PNG・CLI 引数・UI レイアウトの検証 |

設定は `%LOCALAPPDATA%\SmartPaste\settings.json` に保存します。コピー履歴とエラーは設定画面に表示し、常駐中だけ保持します。OneOCR の API 定義は [SnippingToolOcrSharp](https://github.com/ksasao/SnippingToolOcrSharp) と [win11-oneocr](https://github.com/b1tg/win11-oneocr) を参照して確認しました。


## 説明用の画面

Debug版の --demo-settings は実設定を読み書きせず、入力監視や外部CLIを開始しません。--demo-copy <フォルダー> は合成ファイル名の確認画面を開き、ファイルコピーは行いません。--demo-image <フォルダー> は合成画像を開き、選択した場所へのPNG保存とOCRを確認できます。AIボタンは外部送信を伴うため、撮影では使用しません。

今回の撮影では .NET 8ランタイム未導入のため、プロセス限定で DOTNET_ROLL_FORWARD=Major を指定し、.NET 10で検証しました。公開版の動作条件を変更するものではありません。
