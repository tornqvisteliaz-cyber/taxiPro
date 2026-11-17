param(
    [Parameter(Mandatory)] [string] $Product,
    [Parameter(Mandatory)] [string] $Name,
    [Parameter(Mandatory)] [string] $Version,
    # The package zip: app\... and community\{folder}\... (see HubRelease.cs).
    [Parameter(Mandatory)] [string] $Package,
    [Parameter(Mandatory)] [string] $Url,
    [Parameter(Mandatory)] [string] $Out,
    [string] $AppDir = "",
    [string] $AppExe = "",
    [string] $Shortcut = "",
    [string[]] $Processes = @(),
    # "folder:sim" pairs, e.g. "rotaidem-heavypro-panel:msfs2024".
    [string[]] $Community = @(),
    [string] $Notes = "",
    # The Rotaidem release key as RSA XML (RSACryptoServiceProvider.ToXmlString($true)), the same
    # key that signs HeavyPro's update.jws and rotahub.jws. In CI it comes from the UPDATE_SIGNING_KEY secret.
    [string] $KeyXml = $env:UPDATE_SIGNING_KEY,
    # The public key RotaHub has built in; signing with any other key publishes a release nobody accepts.
    [string] $PublicKey = (Join-Path $PSScriptRoot "update-public-key.json")
)

# Copied from the RotaHub repo (packaging/New-HubManifest.ps1). Writes rotahub.jws: the compact JWS (RS256) RotaHub trusts, naming the version, the package and
# its SHA-256, where the app goes and which Community packages to link. Windows PowerShell 5.1 and pwsh 7.
$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($KeyXml)) { throw "No release signing key (UPDATE_SIGNING_KEY)." }
if (-not [Version]::TryParse($Version, [ref]$null)) { throw "Not a version: $Version" }
if (-not $Url.StartsWith("https://")) { throw "The package URL must be https: $Url" }

function ConvertTo-Base64Url([byte[]] $bytes) {
    [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

$app = [ordered]@{}
if ($AppDir) { $app.dir = $AppDir }
if ($AppExe) { $app.exe = $AppExe }
if ($Shortcut) { $app.shortcut = $Shortcut }
$app.processes = @($Processes | Where-Object { $_ })

$parts = @($Community | Where-Object { $_ } | ForEach-Object {
    $folder, $sim = $_.Split(':', 2)
    [ordered]@{ folder = $folder; sim = $(if ($sim) { $sim } else { "msfs2024" }) }
})

$file = Get-Item -LiteralPath $Package
$payload = [ordered]@{
    schema    = 1
    product   = $Product
    name      = $Name
    version   = $Version
    published = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    package   = [ordered]@{ url = $Url; sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); size = $file.Length }
    app       = $app
    community = $parts
    notes     = $Notes
} | ConvertTo-Json -Compress -Depth 5

$utf8 = New-Object System.Text.UTF8Encoding($false)
$head = ConvertTo-Base64Url $utf8.GetBytes('{"alg":"RS256","typ":"JWT"}')
$body = ConvertTo-Base64Url $utf8.GetBytes($payload)

$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
try {
    $rsa.FromXmlString($KeyXml)
    if ($rsa.PublicOnly) { throw "UPDATE_SIGNING_KEY holds only a public key." }
    $expected = (Get-Content -LiteralPath $PublicKey -Raw | ConvertFrom-Json).n
    if ((ConvertTo-Base64Url $rsa.ExportParameters($false).Modulus) -ne $expected) {
        throw "UPDATE_SIGNING_KEY does not match $PublicKey."
    }
    $signature = $rsa.SignData([Text.Encoding]::ASCII.GetBytes("$head.$body"),
        [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
} finally {
    $rsa.Dispose()
}

[IO.File]::WriteAllText($Out, "$head.$body." + (ConvertTo-Base64Url $signature), $utf8)
Write-Host "Signed RotaHub manifest for $Name $Version"
