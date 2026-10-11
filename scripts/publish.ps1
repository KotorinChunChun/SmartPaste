$ErrorActionPreference = 'Stop'
$appRoot = Split-Path $PSScriptRoot -Parent
dotnet publish (Join-Path $appRoot 'SmartPaste/SmartPaste.csproj') -c Release -r win-x64 --self-contained false -o (Join-Path $appRoot 'release') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
dotnet publish (Join-Path $appRoot 'SmartPaste.Ocr/SmartPaste.Ocr.csproj') -c Release --self-contained false -o (Join-Path $appRoot 'release/ocr') --nologo
if ($LASTEXITCODE -ne 0) { throw 'OCR publish failed' }
