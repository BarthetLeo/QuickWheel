# Construit la version à partager (par défaut dans publish\, ou dans le dossier passé avec -Out) :
#   QuickWheel.exe (autonome, sans .NET à installer), LISEZMOI.txt, Assets\MDI-LICENSE.txt et QuickWheel.zip.
# Usage : powershell -ExecutionPolicy Bypass -File tools\publish.ps1 [-Out <dossier>]
# Attention : le dossier de sortie est vidé avant la copie.
param([string]$Out)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
if (-not $Out) { $Out = Join-Path $project 'publish' }
$out = [System.IO.Path]::GetFullPath($Out)
$staging = Join-Path ([System.IO.Path]::GetTempPath()) 'QuickWheel-publish'

if (Get-Process QuickWheel -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$out*" }) {
    throw "QuickWheel tourne depuis $out : quitte-le avant de publier."
}

if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none -o $staging
if ($LASTEXITCODE -ne 0) { throw 'La publication a échoué.' }
Copy-Item (Join-Path $PSScriptRoot 'LISEZMOI.txt') $staging

New-Item -ItemType Directory -Force $out | Out-Null
Get-ChildItem $out | Remove-Item -Recurse -Force
Copy-Item "$staging\*" $out -Recurse
Compress-Archive -Path "$staging\*" -DestinationPath (Join-Path $out 'QuickWheel.zip') -Force
Remove-Item $staging -Recurse -Force

Write-Output "Prêt : $out"
Write-Output "À envoyer : $(Join-Path $out 'QuickWheel.zip')"
