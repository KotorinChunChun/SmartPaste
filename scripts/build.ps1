param([switch]$Test, [switch]$Live)
$ErrorActionPreference = 'Stop'
$appRoot = Split-Path $PSScriptRoot -Parent
dotnet build (Join-Path $appRoot 'SmartPaste/SmartPaste.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$ocrOutput = [System.IO.Path]::GetFullPath((Join-Path $appRoot 'SmartPaste/bin/Release/net8.0-windows10.0.19041.0/ocr'))
if (-not $ocrOutput.StartsWith([System.IO.Path]::GetFullPath($appRoot) + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid OCR output path' }
if (Test-Path -LiteralPath $ocrOutput) { Remove-Item -LiteralPath $ocrOutput -Recurse -Force }
dotnet publish (Join-Path $appRoot 'SmartPaste.Ocr/SmartPaste.Ocr.csproj') -c Release --self-contained false -o (Join-Path $appRoot 'SmartPaste/bin/Release/net8.0-windows10.0.19041.0/ocr') --nologo
if ($LASTEXITCODE -ne 0) { throw 'OCR build failed' }
if ($Test) {
    $arguments = @()
    if ($Live) { $arguments += '--ocr-live'; $arguments += '--ai-live' }
    dotnet run --project (Join-Path $appRoot 'SmartPaste.Tests/SmartPaste.Tests.csproj') -c Release -- @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
}
