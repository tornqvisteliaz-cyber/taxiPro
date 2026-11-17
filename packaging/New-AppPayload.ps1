param(
    [string] $Configuration = "Release",
    [string] $Out = (Join-Path $PSScriptRoot "TaxiProAppPayload.zip")
)

# Zips the built app (src/TaxiPro.App/bin/<Configuration>/net48) into the payload TaxiProSetup.exe
# embeds and RotaHub installs: the exe, its config and every DLL next to it, plus the SimConnect
# runtime notice. Symbols (.pdb) stay out. Build (and, in CI, sign) TaxiPro.exe before running this.
# Windows PowerShell 5.1 and pwsh 7.
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$output = Join-Path $repo "src/TaxiPro.App/bin/$Configuration/net48"

foreach ($required in "TaxiPro.exe", "TaxiPro.exe.config", "TaxiPro.Core.dll", "System.Text.Json.dll",
                      "Microsoft.FlightSimulator.SimConnect.dll", "SimConnect.dll") {
    if (-not (Test-Path (Join-Path $output $required))) { throw "The app build is missing $required ($output)." }
}

Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
if (Test-Path $Out) { Remove-Item -LiteralPath $Out -Force }
$zip = [System.IO.Compression.ZipFile]::Open($Out, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem -LiteralPath $output -File | Where-Object { $_.Extension -ne ".pdb" } | ForEach-Object {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $_.Name, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
    $notice = Join-Path $repo "lib/NOTICE-MSFS-SimConnect.txt"
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $notice, "NOTICE-MSFS-SimConnect.txt", [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
} finally {
    $zip.Dispose()
}
Write-Host ("Created {0} ({1:N0} KB)" -f $Out, ((Get-Item $Out).Length / 1KB))
