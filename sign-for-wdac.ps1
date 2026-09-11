<#
.SYNOPSIS
  Authenticode-signs a freshly-built DLL with a local code-signing certificate so this machine's
  Application Control policy lets the dotnet host load it.

.DESCRIPTION
  A rebuilt unsigned managed DLL under the user profile has no ISG reputation and gets blocked
  with 0x800711C7 ("An Application Control policy has blocked this file"), which breaks running
  the tool through `dotnet <dll>`. Signing it with a certificate already trusted in
  CurrentUser\Root and CurrentUser\TrustedPublisher clears that.

  Local trust only. Nothing here helps on another machine, and it does not satisfy Smart App
  Control, which judges by reputation rather than by signature.

  On a machine with no code-signing certificate - CI, for instance - this is a no-op: it warns
  and exits 0, leaving the DLL unsigned, which is fine because CI does not enforce WDAC.

  Wired into each .csproj as an AfterTargets="Build" step; can also be run by hand.

.PARAMETER CertificateSubject
  Which certificate to sign with, for example "CN=Your Name". Defaults to the FIXFINDER_CERT_SUBJECT
  environment variable, and failing that to the newest code-signing certificate in the personal
  store - so a fresh clone works without editing anything.
#>
param(
    [Parameter(Mandatory = $true)][string]$TargetPath,
    [string]$CertificateSubject = $env:FIXFINDER_CERT_SUBJECT
)
$ErrorActionPreference = "Stop"

if (-not (Test-Path $TargetPath)) { throw "Not found: $TargetPath" }

$candidates = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Sort-Object NotAfter -Descending

if ($CertificateSubject) {
    $candidates = $candidates | Where-Object { $_.Subject -eq $CertificateSubject }
}

$cert = $candidates | Select-Object -First 1

if (-not $cert) {
    $which = if ($CertificateSubject) { "matching '$CertificateSubject'" } else { "in CurrentUser\My" }
    Write-Host "sign-for-wdac: no code-signing certificate $which - leaving $([System.IO.Path]::GetFileName($TargetPath)) unsigned"
    exit 0
}

$sig = Set-AuthenticodeSignature -FilePath $TargetPath -Certificate $cert -HashAlgorithm SHA256
Write-Host "sign-for-wdac: $($sig.Status) $([System.IO.Path]::GetFileName($TargetPath)) [$($cert.Subject)]"
