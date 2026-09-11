<#
.SYNOPSIS
  Publishes FixFinder as a single self-contained FixFinder.exe and signs it.

.DESCRIPTION
  Produces one file with the .NET runtime and WPF bundled inside, so it runs on a machine with
  no .NET installed. That is what makes it worth doing at all - a framework-dependent exe is a
  few hundred KB but is just a launcher for a runtime that has to already be there, which is no
  more portable than the DLL it replaces.

  Compression is on. It roughly halves the output at the cost of a slower first start, which is
  the right trade for a tool launched by hand rather than in a loop.

  The exe is Authenticode-signed with the same local certificate the Debug DLLs use. Read the
  warning printed at the end before assuming that makes it runnable everywhere: local trust
  satisfies an Application Control policy, and it does not satisfy Smart App Control, which
  judges by reputation rather than by signature.

.PARAMETER CertificateSubject
  Which certificate to sign with, for example "CN=Your Name". Defaults to the
  FIXFINDER_CERT_SUBJECT environment variable, and failing that to the newest code-signing
  certificate in the personal store - so a fresh clone works without editing anything.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",

    # Skip the runtime bundle: much smaller, but requires the .NET 8 desktop runtime installed.
    [switch]$FrameworkDependent,

    [switch]$SkipSigning,

    [string]$CertificateSubject = $env:FIXFINDER_CERT_SUBJECT
)

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$project = Join-Path $root "FixFinder.Gui\FixFinder.Gui.csproj"
$output = Join-Path $root "publish"

if (-not (Test-Path $project)) { throw "Not found: $project" }

Write-Host ""
Write-Host "Publishing FixFinder ($Configuration, $Runtime, $(if ($FrameworkDependent) { 'framework-dependent' } else { 'self-contained' }))..."
Write-Host ""

if (Test-Path $output) { Remove-Item $output -Recurse -Force }

$arguments = @(
    "publish", $project,
    "-c", $Configuration,
    "-r", $Runtime,
    "-o", $output,
    "--nologo",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=embedded",
    "-p:SatelliteResourceLanguages=en"
)

if ($FrameworkDependent) {
    $arguments += "--self-contained:false"
} else {
    $arguments += "--self-contained:true"
    # Compression only applies to a self-contained bundle.
    $arguments += "-p:EnableCompressionInSingleFile=true"
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# The assembly is FixFinder.Gui; the thing a person double-clicks should just be FixFinder.
$published = Join-Path $output "FixFinder.Gui.exe"
$final = Join-Path $output "FixFinder.exe"

if (-not (Test-Path $published)) { throw "Expected $published but it was not produced" }
if (Test-Path $final) { Remove-Item $final -Force }
Move-Item $published $final

# Signing comes after the rename: Authenticode covers the file's bytes, and renaming afterwards
# would be fine, but signing the final artifact keeps "what was signed" unambiguous.
if (-not $SkipSigning) {
    # -CodeSigningCert is a dynamic parameter from the certificate provider and is not always
    # available; where it is missing the call throws rather than returning nothing. Publishing
    # unsigned is a warning, not a failure, so the lookup must degrade to "no certificate".
    $candidates = @()

    try { $candidates = @(Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction Stop) }
    catch { $candidates = @() }

    $candidates = $candidates | Sort-Object NotAfter -Descending
    if ($CertificateSubject) { $candidates = $candidates | Where-Object { $_.Subject -eq $CertificateSubject } }

    $cert = $candidates | Select-Object -First 1

    if ($cert) {
        $sig = Set-AuthenticodeSignature -FilePath $final -Certificate $cert -HashAlgorithm SHA256
        Write-Host "Signed: $($sig.Status)  [$($cert.Subject)]"
    } else {
        Write-Warning "No code-signing certificate found - the exe is unsigned."
    }
}

$size = [Math]::Round((Get-Item $final).Length / 1MB, 1)

Write-Host ""
Write-Host "Built: $final  ($size MB)"

$extra = Get-ChildItem $output -File | Where-Object { $_.Name -ne "FixFinder.exe" }
if ($extra) {
    Write-Host ""
    Write-Host "Alongside it:"
    $extra | ForEach-Object { Write-Host ("  {0}  ({1:N0} bytes)" -f $_.Name, $_.Length) }
}

Write-Host ""
Write-Host "Note: this machine enforces Smart App Control, which judges an executable by"
Write-Host "reputation rather than by signature. A self-signed exe has none, so Windows may"
Write-Host "refuse to start this one here even though it is signed and the certificate is"
Write-Host "trusted locally. If that happens, run-fixfinder.cmd still works - it launches the"
Write-Host "DLL through dotnet.exe, which is already trusted."
Write-Host ""
