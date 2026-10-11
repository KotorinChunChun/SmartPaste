param([switch]$Ai, [switch]$EnterSave, [string]$Toolkit = 'C:\Users\k_watanabe\.codex\skills\windows-ui-automation\scripts\ui-toolkit.ps1')
$ErrorActionPreference = 'Stop'
. $Toolkit
$appRoot = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $appRoot 'SmartPaste\bin\Release\net8.0-windows10.0.19041.0\SmartPaste.exe'
$artifactDir = Join-Path $appRoot 'artifacts'
$testDir = Join-Path $artifactDir 'ui-fixture'
New-Item -ItemType Directory -Path $testDir -Force | Out-Null
$process = $null
function Wait-TestWindow($testProcess) {
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        $testProcess.Refresh()
        if ($testProcess.HasExited) { throw 'Test app exited before opening its window' }
        if ($testProcess.MainWindowHandle -ne [IntPtr]::Zero) {
            $element = [System.Windows.Automation.AutomationElement]::FromHandle($testProcess.MainWindowHandle)
            if ($element.Current.ProcessId -ne $testProcess.Id) { throw 'Unexpected window owner' }
            return $testProcess.MainWindowHandle
        }
        Start-Sleep -Milliseconds 200
    }
    throw 'Test window did not open'
}
function Send-FocusedEnter($element) {
    $element.SetFocus()
    $focus = [System.Windows.Automation.AutomationElement]::FocusedElement
    if ($focus.Current.ProcessId -ne $process.Id -or $focus.Current.AutomationId -ne $element.Current.AutomationId) { throw 'Unexpected input focus' }
    Send-TextInput -WindowHandle $window -Text '{ENTER}' -Raw
}
try {
    $process = Start-Process -FilePath $exe -ArgumentList '--demo-copy' -WindowStyle Hidden -PassThru
    $window = Wait-TestWindow $process
    [WUA.Win]::ShowWindow($window, 9) | Out-Null
    if (-not (Save-WindowScreenshot -WindowHandle $window -Path (Join-Path $artifactDir 'copy-initial.png'))) { throw 'Screenshot failed' }
    $value = Find-ByAutomationId -WindowHandle $window -AutomationId 'NewValue'
    Set-ElementValue -Element $value -Value '07'
    if (-not (Save-WindowScreenshot -WindowHandle $window -Path (Join-Path $artifactDir 'copy-preview.png'))) { throw 'Screenshot failed' }
    Invoke-ElementAction -Element (Find-ByAutomationId -WindowHandle $window -AutomationId 'CancelButton') -Action Invoke
    $process.WaitForExit(5000) | Out-Null
    $process = Start-Process -FilePath $exe -ArgumentList @('--demo-image', $testDir) -WindowStyle Hidden -PassThru
    $window = Wait-TestWindow $process
    [WUA.Win]::ShowWindow($window, 9) | Out-Null
    if (-not (Save-WindowScreenshot -WindowHandle $window -Path (Join-Path $artifactDir 'image-preview.png'))) { throw 'Screenshot failed' }
    $extension = Find-ByAutomationId -WindowHandle $window -AutomationId 'ImageExtension'
    if ($extension.Current.Name -ne '.png') { throw 'Fixed extension label failed' }
    $nameElement = Find-ByAutomationId -WindowHandle $window -AutomationId 'ImageName'
    $namePattern = [System.Windows.Automation.ValuePattern]$nameElement.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    if ($namePattern.Current.Value -ne 'image1') { throw ('Initial name must exclude extension; actual=' + $namePattern.Current.Value) }
    Invoke-ElementAction -Element (Find-ByAutomationId -WindowHandle $window -AutomationId 'OcrButton') -Action Invoke
    $parts = @()
    for ($i = 1; $i -le 3; $i++) {
        $candidate = Find-ByAutomationId -WindowHandle $window -AutomationId "NameCandidate$i" -TimeoutMs 20000
        $parts += $candidate.Current.Name
        Invoke-ElementAction -Element $candidate -Action Invoke
    }
    if ($namePattern.Current.Value -ne ($parts -join '_')) { throw 'Three candidates were not joined in selection order' }
    Invoke-ElementAction -Element (Find-ByAutomationId -WindowHandle $window -AutomationId 'MoveCandidateUp3') -Action Invoke
    $ordered = @($parts[0], $parts[2], $parts[1])
    $delimiter = Find-ByAutomationId -WindowHandle $window -AutomationId 'NameSeparator'
    Set-ElementValue -Element $delimiter -Value '-'
    if ($namePattern.Current.Value -ne ($ordered -join '-')) { throw 'Reordering or custom separator failed' }
    Invoke-ElementAction -Element (Find-ByAutomationId -WindowHandle $window -AutomationId 'RemoveCandidate2') -Action Invoke
    if ($namePattern.Current.Value -ne (@($parts[0], $parts[1]) -join '-')) { throw 'Removing a selected candidate failed' }
    Invoke-ElementAction -Element (Find-ByAutomationId -WindowHandle $window -AutomationId 'NameCandidate3') -Action Invoke
    if ($namePattern.Current.Value -ne ($parts -join '-')) { throw 'Adding another candidate after removal failed' }
    if (-not (Save-WindowScreenshot -WindowHandle $window -Path (Join-Path $artifactDir 'image-ocr.png'))) { throw 'Screenshot failed' }
    if ($Ai) {
        Invoke-ElementAction -Element (Find-ByAutomationId -WindowHandle $window -AutomationId 'RemoveCandidate1') -Action Invoke
        Invoke-ElementAction -Element (Find-ByAutomationId -WindowHandle $window -AutomationId 'RemoveCandidate1') -Action Invoke
        Invoke-ElementAction -Element (Find-ByAutomationId -WindowHandle $window -AutomationId 'AiButton') -Action Invoke
        if (-not (Save-WindowScreenshot -WindowHandle $window -Path (Join-Path $artifactDir 'image-ai-busy.png'))) { throw 'Screenshot failed' }
        if ((Find-ByAutomationId -WindowHandle $window -AutomationId 'AiButton').Current.IsEnabled) { throw 'AI wait state was not entered' }
        $last = Find-ByAutomationId -WindowHandle $window -AutomationId 'NameCandidate10' -TimeoutMs 180000
        Invoke-ElementAction -Element $last -Action Invoke
        if ($namePattern.Current.Value -match '\.png$') { throw 'Candidate inserted editable extension' }
        $description = Find-ByAutomationId -WindowHandle $window -AutomationId 'ImageDescription'
        $descriptionPattern = [System.Windows.Automation.ValuePattern]$description.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
        if ($descriptionPattern.Current.Value.Contains(' ')) { throw 'AI description contains halfwidth spaces' }
        if (-not (Save-WindowScreenshot -WindowHandle $window -Path (Join-Path $artifactDir 'image-ai.png'))) { throw 'Screenshot failed' }
    }
    $name = Find-ByAutomationId -WindowHandle $window -AutomationId 'ImageName'
    if ($EnterSave) {
        Send-FocusedEnter $delimiter
        $process.Refresh()
        if ($process.HasExited) { throw 'Enter in separator field saved unexpectedly' }
        Set-ElementValue -Element $name -Value '禁止/名'
        Send-FocusedEnter $name
        $process.Refresh()
        if ($process.HasExited) { throw 'Enter saved an invalid file name' }
    }
    Set-ElementValue -Element $name -Value 'OCRテスト'
    if ($EnterSave) { Send-FocusedEnter $name }
    else { Invoke-ElementAction -Element (Find-ByAutomationId -WindowHandle $window -AutomationId 'SaveImageButton') -Action Invoke }
    $process.WaitForExit(5000) | Out-Null
    if (-not (Test-Path -LiteralPath (Join-Path $testDir 'OCRテスト.png'))) { throw 'PNG save failed' }
    Write-Output 'PASS: 数値入力、OneOCR実行、固定拡張子、3候補連結・並べ替え・区切り・解除、PNG保存（-EnterSave時は名前欄のEnter）、AI回転表示・10候補（-Ai時）'
}
finally {
    if ($process -and -not $process.HasExited) {
        $null = $process.CloseMainWindow()
        if (-not $process.WaitForExit(5000)) { Stop-Process -Id $process.Id }
    }
    # Keep screenshots; remove only the explicitly scoped fixture directory.
    $resolved = [System.IO.Path]::GetFullPath($testDir)
    if ($resolved -ne [System.IO.Path]::GetFullPath((Join-Path $appRoot 'artifacts\ui-fixture'))) { throw 'Unexpected fixture path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
