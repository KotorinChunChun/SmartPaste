param([switch]$UseRunning, [string]$Toolkit = 'C:\Users\k_watanabe\.codex\skills\windows-ui-automation\scripts\ui-toolkit.ps1')
$ErrorActionPreference = 'Stop'
. $Toolkit
$appRoot = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $appRoot 'SmartPaste\bin\Release\net8.0-windows10.0.19041.0\SmartPaste.exe'
$testExe = Join-Path $appRoot 'SmartPaste.Tests\bin\Release\net8.0-windows10.0.19041.0\SmartPaste.Tests.exe'
$testDir = Join-Path $appRoot 'artifacts\explorer-fixture'
$source = Join-Path $testDir 'R01報告書.txt'
$clipboardProxy = [System.Windows.Forms.Clipboard]::GetDataObject()
$originalClipboard = New-Object System.Windows.Forms.DataObject
if ($clipboardProxy) {
    foreach ($format in $clipboardProxy.GetFormats($false)) {
        $data = $clipboardProxy.GetData($format, $false)
        if ($data -is [System.IO.Stream]) { $copy = New-Object System.IO.MemoryStream; if ($data.CanSeek) { $data.Position = 0 }; $data.CopyTo($copy); $copy.Position = 0; $data = $copy }
        if ($data -is [System.Drawing.Image]) { $data = New-Object System.Drawing.Bitmap($data) }
        if ($null -ne $data) { $originalClipboard.SetData($format, $false, $data) }
    }
}
$shell = New-Object -ComObject Shell.Application
$previousWindows = @($shell.Windows() | ForEach-Object { [long]$_.HWND })
$browser = $null
$app = $null
try {
    New-Item -ItemType Directory -Path $testDir -Force | Out-Null
    Set-Content -LiteralPath $source -Value 'SmartPaste sample' -Encoding utf8
    if (-not $UseRunning) {
        if (Get-Process -Name SmartPaste -ErrorAction SilentlyContinue) { throw '既に起動中です。-UseRunning を指定してください。' }
        $app = Start-Process -FilePath $exe -ArgumentList '--tray' -WindowStyle Hidden -PassThru
    }
    Start-Process -FilePath explorer.exe -ArgumentList ('/n,"{0}"' -f $testDir) -WindowStyle Hidden | Out-Null
    for ($i = 0; $i -lt 60; $i++) {
        foreach ($item in $shell.Windows()) {
            try { if ($item.Document.Folder.Self.Path -eq $testDir -and [long]$item.HWND -notin $previousWindows) { $browser = $item; break } } catch { }
        }
        if ($browser) { break }
        Start-Sleep -Milliseconds 200
    }
    if (-not $browser) { throw '専用の Explorer ウィンドウを確認できません。' }
    $window = [IntPtr][long]$browser.HWND
    [WUA.Win]::ShowWindow($window, 9) | Out-Null
    if (-not (Save-WindowScreenshot -WindowHandle $window -Path (Join-Path $appRoot 'artifacts\explorer-before.png'))) { throw 'Screenshot failed' }
    $file = Find-InWindow -WindowHandle $window -Name 'R01報告書.txt' -AllowMissing
    if (-not $file) { $file = Find-InWindow -WindowHandle $window -Name 'R01報告書' }
    Invoke-ElementAction -Element $file -Action Select
    $file.SetFocus()
    [WUA.Win]::SetForegroundWindow($window) | Out-Null
    & $testExe --probe-explorer $window.ToInt64()
    if ($LASTEXITCODE -ne 0) { throw 'Explorer probe failed' }
    # Real Shell copy data: unlike the synthetic helper, Explorer sets Preferred DropEffect=5.
    $sourceItem = $shell.Namespace($testDir).ParseName('R01報告書.txt')
    $copyVerbs = @($sourceItem.Verbs() | Where-Object { $_.Name -match '^コピー' })
    if ($copyVerbs.Count -ne 1) { throw 'Shellのコピーコマンドを一意に確認できません。' }
    $copyVerbs[0].DoIt()
    $effectStream = [System.Windows.Forms.Clipboard]::GetData('Preferred DropEffect')
    $effectStream.Position = 0
    $effectBytes = New-Object byte[] 4
    $null = $effectStream.Read($effectBytes, 0, 4)
    Write-Output ('Shell copy Preferred DropEffect: ' + [BitConverter]::ToUInt32($effectBytes, 0))
    Start-Sleep -Milliseconds 900
    $file.SetFocus()
    if (-not (Save-WindowScreenshot -WindowHandle $window -Path (Join-Path $appRoot 'artifacts\explorer-ready.png'))) { throw 'Target Explorer is not foreground' }
    Send-TextInput -WindowHandle $window -Text '^v' -Raw
    for ($i = 0; $i -lt 40; $i++) { if (Test-Path -LiteralPath (Join-Path $testDir 'R02報告書.txt')) { break }; Start-Sleep -Milliseconds 150 }
    if (-not (Test-Path -LiteralPath (Join-Path $testDir 'R02報告書.txt'))) {
        $settingsWindow = Wait-AppWindow -Title '賢い名付け — 設定' -AllowMissing
        if ($settingsWindow -ne [IntPtr]::Zero) {
            [WUA.Win]::ShowWindow($settingsWindow, 9) | Out-Null
            Save-WindowScreenshot -WindowHandle $settingsWindow -Path (Join-Path $appRoot 'artifacts\failure.png') | Out-Null
        }
        Get-ChildItem -LiteralPath $testDir -Name | Write-Output
        throw 'Ctrl+V interception failed'
    }
    if (-not (Save-WindowScreenshot -WindowHandle $window -Path (Join-Path $appRoot 'artifacts\explorer-after.png'))) { throw 'Screenshot failed' }
    Start-Sleep -Milliseconds 700
    $probe = (& $testExe --probe-explorer $window.ToInt64()) | ConvertFrom-Json
    if ($probe.FocusedPath -ne (Join-Path $testDir 'R02報告書.txt')) { throw ('New file focus failed: ' + $probe.FocusedPath) }
    $newFile = Find-InWindow -WindowHandle $window -Name 'R02報告書.txt' -AllowMissing
    if (-not $newFile) { $newFile = Find-InWindow -WindowHandle $window -Name 'R02報告書' }
    $selected = [System.Windows.Automation.SelectionItemPattern]$newFile.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if (-not $selected.Current.IsSelected) { throw 'New file selection failed' }
    # A different source directory must use Explorer's native paste, preserving its name.
    $foreignDir = Join-Path $testDir 'foreign'
    New-Item -ItemType Directory -Path $foreignDir -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $foreignDir '202612別フォルダ.txt') -Value 'Foreign sample' -Encoding utf8
    $foreignItem = $shell.Namespace($foreignDir).ParseName('202612別フォルダ.txt')
    $foreignVerbs = @($foreignItem.Verbs() | Where-Object { $_.Name -match '^コピー' })
    if ($foreignVerbs.Count -ne 1) { throw 'Foreign copy command missing' }
    $foreignVerbs[0].DoIt()
    $newFile.SetFocus()
    Start-Sleep -Milliseconds 900
    if (-not (Save-WindowScreenshot -WindowHandle $window -Path (Join-Path $appRoot 'artifacts\explorer-foreign-ready.png'))) { throw 'Target Explorer is not foreground' }
    Send-TextInput -WindowHandle $window -Text '^v' -Raw
    for ($i = 0; $i -lt 40; $i++) { if (Test-Path -LiteralPath (Join-Path $testDir '202612別フォルダ.txt')) { break }; Start-Sleep -Milliseconds 150 }
    if (-not (Test-Path -LiteralPath (Join-Path $testDir '202612別フォルダ.txt'))) { throw 'Native paste into different folder failed' }
    if (Test-Path -LiteralPath (Join-Path $testDir '202701別フォルダ.txt')) { throw 'Foreign paste was incorrectly incremented' }
    Start-Sleep -Milliseconds 400
    $probe = (& $testExe --probe-explorer $window.ToInt64()) | ConvertFrom-Json
    if ($probe.FocusedPath -ne (Join-Path $testDir '202612別フォルダ.txt')) { throw 'Native paste focus failed' }
    & $testExe --clipboard-image
    $file = Find-InWindow -WindowHandle $window -Name 'R01報告書.txt' -AllowMissing
    if (-not $file) { $file = Find-InWindow -WindowHandle $window -Name 'R01報告書' }
    $file.SetFocus()
    Start-Sleep -Milliseconds 900
    if (-not (Save-WindowScreenshot -WindowHandle $window -Path (Join-Path $appRoot 'artifacts\explorer-image-ready.png'))) { throw 'Target Explorer is not foreground' }
    Send-TextInput -WindowHandle $window -Text '+{INSERT}' -Raw
    $dialog = Wait-AppWindow -Title '賢い名付け — 画像を保存' -TimeoutMs 10000
    [WUA.Win]::ShowWindow($dialog, 9) | Out-Null
    if (-not (Save-WindowScreenshot -WindowHandle $dialog -Path (Join-Path $appRoot 'artifacts\explorer-image.png'))) { throw 'Screenshot failed' }
    Set-ElementValue -Element (Find-ByAutomationId -WindowHandle $dialog -AutomationId 'ImageName') -Value 'image1'
    Invoke-ElementAction -Element (Find-ByAutomationId -WindowHandle $dialog -AutomationId 'SaveImageButton') -Action Invoke
    Start-Sleep -Milliseconds 300
    if (-not (Test-Path -LiteralPath (Join-Path $testDir 'image1.png'))) { throw 'Shift+Insert image interception failed' }
    Start-Sleep -Milliseconds 700
    $probe = (& $testExe --probe-explorer $window.ToInt64()) | ConvertFrom-Json
    if ($probe.FocusedPath -ne (Join-Path $testDir 'image1.png')) { throw ('Saved image focus failed: ' + $probe.FocusedPath) }
    Write-Output 'PASS: 同じフォルダでR02へコピー・選択、別フォルダは元の名前でOS標準貼り付け、PNG保存後の選択・フォーカス'
}
finally {
    if ($app -and -not $app.HasExited) { Stop-Process -Id $app.Id }
    if ($browser) { $browser.Quit() }
    [System.Windows.Forms.Clipboard]::SetDataObject($originalClipboard, $true)
    $resolved = [System.IO.Path]::GetFullPath($testDir)
    if ($resolved -ne [System.IO.Path]::GetFullPath((Join-Path $appRoot 'artifacts\explorer-fixture'))) { throw 'Unexpected fixture path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
