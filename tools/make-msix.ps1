# Packages Shelf into an .msix file using makeappx + signtool from the Windows SDK.
#
# Requires Windows SDK installed (provides makeappx.exe and signtool.exe):
#   winget install --id Microsoft.WindowsSDK.10.0.22621
# or download from https://developer.microsoft.com/windows/downloads/windows-sdk/
#
# Usage:
#   pwsh tools\make-msix.ps1                    # build + pack (unsigned)
#   pwsh tools\make-msix.ps1 -Sign              # build + pack + sign with local self-signed cert
#   pwsh tools\make-msix.ps1 -SkipBuild -Sign   # repack existing publish output and sign
#
# Output: bin\Store\Shelf.msix

[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$Sign,
    [string]$CertSubject = "CN=BridgesCommunity"
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$Csproj      = Join-Path $ProjectRoot "Shelf.csproj"
$Manifest    = Join-Path $ProjectRoot "Shelf.Package\Package.appxmanifest"
$Assets      = Join-Path $ProjectRoot "Shelf.Package\Assets"
$PublishOut  = Join-Path $ProjectRoot "bin\Store\publish"
$WorkDir     = Join-Path $ProjectRoot "bin\Store\msix-stage"
$MsixPath    = Join-Path $ProjectRoot "bin\Store\Shelf.msix"

# ---- Locate Windows SDK tools ----
function Find-SdkTool {
    param([string]$ToolName)
    $kitRoot = "C:\Program Files (x86)\Windows Kits\10\bin"
    if (-not (Test-Path $kitRoot)) { return $null }
    # Pick the highest version subfolder that contains x64\<ToolName>.
    $candidates = Get-ChildItem $kitRoot -Directory |
        Where-Object { $_.Name -match '^10\.\d+\.\d+\.\d+$' } |
        Sort-Object Name -Descending
    foreach ($v in $candidates) {
        $p = Join-Path $v.FullName "x64\$ToolName"
        if (Test-Path $p) { return $p }
    }
    return $null
}

$Makeappx = Find-SdkTool "makeappx.exe"
$Signtool = Find-SdkTool "signtool.exe"

if (-not $Makeappx) {
    Write-Error @"
makeappx.exe not found.

Install Windows SDK (~1.5 GB):
  winget install --id Microsoft.WindowsSDK.10.0.22621
or from https://developer.microsoft.com/windows/downloads/windows-sdk/

Required SDK components:
  - MSI Tools                              -> makeappx.exe
  - Windows SDK Signing Tools for Desktop  -> signtool.exe
  - Windows App Certification Kit          -> WACK (for Etap 7.5)
"@
}

Write-Host "Found makeappx: $Makeappx"
if ($Sign -and -not $Signtool) {
    Write-Error "signtool.exe not found (needed because -Sign was passed)."
}
if ($Signtool) { Write-Host "Found signtool: $Signtool" }
Write-Host ""

# ---- Stop running Shelf so dotnet publish does not collide ----
Get-Process -Name Shelf -ErrorAction SilentlyContinue | Stop-Process -Force

# ---- Build (dotnet publish) ----
if (-not $SkipBuild) {
    if (Test-Path $PublishOut) { Remove-Item $PublishOut -Recurse -Force }
    Write-Host "Publishing Shelf in Store configuration..."
    & "C:\Program Files\dotnet\dotnet.exe" publish $Csproj `
        -c Store `
        -r win-x64 `
        --self-contained true `
        -o $PublishOut `
        --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet publish failed (exit code $LASTEXITCODE)"
    }
}

if (-not (Test-Path (Join-Path $PublishOut "Shelf.exe"))) {
    Write-Error "Publish output missing Shelf.exe at $PublishOut"
}

# ---- Stage MSIX content ----
if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null

Write-Host "Staging MSIX content in $WorkDir"
Copy-Item (Join-Path $PublishOut "*") $WorkDir -Recurse -Force
# Inside an .msix the manifest must be named AppxManifest.xml at the root.
Copy-Item $Manifest (Join-Path $WorkDir "AppxManifest.xml") -Force
New-Item -ItemType Directory -Force -Path (Join-Path $WorkDir "Assets") | Out-Null
Copy-Item (Join-Path $Assets "*.png") (Join-Path $WorkDir "Assets") -Force

# ---- Pack ----
if (Test-Path $MsixPath) { Remove-Item $MsixPath -Force }
Write-Host "Packing $MsixPath ..."
& $Makeappx pack /d $WorkDir /p $MsixPath /o
if ($LASTEXITCODE -ne 0) {
    Write-Error "makeappx pack failed (exit code $LASTEXITCODE)"
}

# ---- Sign (optional) ----
if ($Sign) {
    Write-Host "Looking for code-signing certificate with Subject '$CertSubject'..."
    $cert = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $CertSubject -and $_.HasPrivateKey } |
        Select-Object -First 1

    if (-not $cert) {
        Write-Host "No certificate found, generating a self-signed one (valid 3 years)..."
        $cert = New-SelfSignedCertificate `
            -Type CodeSigningCert `
            -Subject $CertSubject `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -KeyUsage DigitalSignature `
            -KeyAlgorithm RSA `
            -KeyLength 2048 `
            -NotAfter (Get-Date).AddYears(3) `
            -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
        Write-Host "Created self-signed cert. Thumbprint: $($cert.Thumbprint)"
        Write-Host ""
        Write-Host "To install the .msix locally you must also trust the cert:"
        Write-Host "  Export-Certificate -Cert Cert:\CurrentUser\My\$($cert.Thumbprint) -FilePath shelf-cert.cer"
        Write-Host "  Import-Certificate -FilePath shelf-cert.cer -CertStoreLocation Cert:\LocalMachine\Root  # needs admin"
        Write-Host ""
    }

    Write-Host "Signing $MsixPath ..."
    & $Signtool sign /sha1 $cert.Thumbprint /fd SHA256 $MsixPath
    if ($LASTEXITCODE -ne 0) {
        Write-Error "signtool sign failed (exit code $LASTEXITCODE)"
    }
}

# ---- Report ----
$size = [Math]::Round((Get-Item $MsixPath).Length / 1MB, 1)
Write-Host ""
Write-Host "Built: $MsixPath ($size MB)"
if ($Sign) {
    Write-Host "Signed with: $($cert.Thumbprint)"
    Write-Host ""
    Write-Host "Install locally (cert must be trusted first - see message above):"
    Write-Host "  Add-AppxPackage -Path $MsixPath"
} else {
    Write-Host "Unsigned. Re-run with -Sign for local install."
    Write-Host "(Microsoft Store re-signs at publish time anyway - unsigned is fine for upload.)"
}
