param(
    [Parameter(Mandatory)] [string] $Path
)

# Authenticode-signs one file with the certificate in CODE_SIGNING_PFX_BASE64 /
# CODE_SIGNING_PFX_PASSWORD (repo secrets, not set yet). Without a certificate this only
# warns: an unsigned exe still gets a SmartScreen/antivirus warning on every run,
# but a PR or local build must not fail just because nobody has added one yet.
$ErrorActionPreference = "Stop"

$pfxBase64 = $env:CODE_SIGNING_PFX_BASE64
$pfxPassword = $env:CODE_SIGNING_PFX_PASSWORD
if ([string]::IsNullOrEmpty($pfxBase64) -and [string]::IsNullOrEmpty($pfxPassword)) {
    Write-Warning "No code-signing certificate (CODE_SIGNING_PFX_BASE64 / CODE_SIGNING_PFX_PASSWORD secrets): $Path is unsigned."
    return
}
if ([string]::IsNullOrEmpty($pfxBase64) -or [string]::IsNullOrEmpty($pfxPassword)) {
    throw "Set both CODE_SIGNING_PFX_BASE64 and CODE_SIGNING_PFX_PASSWORD, or neither."
}
if (-not (Test-Path $Path)) { throw "Nothing to sign at $Path." }

$signtool = (Get-Command signtool.exe -ErrorAction SilentlyContinue).Path
if (-not $signtool) {
    $signtool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "\\x64\\" } | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $signtool) { throw "signtool.exe not found. Install the Windows SDK, or run this on windows-latest." }

$pfx = Join-Path ([System.IO.Path]::GetTempPath()) "groundpro-signing-$([Guid]::NewGuid()).pfx"
[IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($pfxBase64))
try {
    & $signtool sign /f $pfx /p $pfxPassword /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $Path
    if ($LASTEXITCODE -ne 0) { throw "signtool could not sign $Path (exit $LASTEXITCODE)." }
    & $signtool verify /pa $Path
    if ($LASTEXITCODE -ne 0) { throw "Signed $Path, but signtool verify failed." }
    Write-Host "Signed $Path"
} finally {
    Remove-Item -LiteralPath $pfx -Force -ErrorAction SilentlyContinue
}
