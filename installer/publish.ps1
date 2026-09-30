# Builds the self-contained Windows binaries and (if Inno Setup is installed) the two installers.
# Usage (PowerShell, from the repository root):  .\installer\publish.ps1 [-Version 0.9.0]
param([string]$Version = "0.9.0")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "publish"
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue

$common = @("-c", "Release", "-r", "win-x64", "--self-contained", "-p:PublishSingleFile=true",
            "-p:EnableCompressionInSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
            "-p:DebugType=none", "-p:Version=$Version")

dotnet test "$root\Quotation.sln" -c Release
if ($LASTEXITCODE -ne 0) { throw "Tests failed - not packaging." }

dotnet publish "$root\src\Quotation.Server" @common -o "$out\server"
dotnet publish "$root\src\Quotation.Desktop" @common -o "$out\desktop"
dotnet publish "$root\tools\Tally.Simulator" @common -o "$out\simulator"

$iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (Test-Path $iscc) {
    & $iscc "/DAppVersion=$Version" "$PSScriptRoot\TSQuotationServer.iss"
    & $iscc "/DAppVersion=$Version" "$PSScriptRoot\TSQuotationClient.iss"
    Write-Host "Installers written to $out\installers"
} else {
    Write-Warning "Inno Setup 6 not found - binaries are in $out. Install it from https://jrsoftware.org/isinfo.php to build installers."
}
