<#
.SYNOPSIS
  Authenticode-signs a freshly-built DLL with the local "CN=Ethan Brown" dev certificate so
  this machine's Application Control (WDAC/ISG) policy lets the dotnet host load it.

  A rebuilt unsigned managed DLL under the user profile has no ISG reputation and gets blocked
  with 0x800711C7 ("An Application Control policy has blocked this file") - which breaks both
  `run-verifier.cmd` (it runs `dotnet bin\Debug\...\ShoppingCartAppVerifier.dll`) and
  `ShoppingCartAppVerifier.Cli`. The same self-signed cert the publish .exe uses
  (sign-exe.ps1) is already trusted in CurrentUser\Root + TrustedPublisher, so signing the
  Debug DLL clears it.

  Local trust only - nothing here helps on another machine or satisfies Smart App Control. On
  a machine without the cert (e.g. CI) this is a no-op: it warns and exits 0, leaving the DLL
  unsigned, which is fine because CI runners don't enforce WDAC.

  Wired into both .csproj files as an AfterTargets="Build" step; can also be run by hand.
#>
param(
    [Parameter(Mandatory = $true)][string]$TargetPath
)
$ErrorActionPreference = "Stop"

if (-not (Test-Path $TargetPath)) { throw "Not found: $TargetPath" }

$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Where-Object { $_.Subject -eq "CN=Ethan Brown" } |
    Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $cert) {
    Write-Host "sign-for-wdac: no 'CN=Ethan Brown' cert - leaving $([System.IO.Path]::GetFileName($TargetPath)) unsigned"
    exit 0
}

$sig = Set-AuthenticodeSignature -FilePath $TargetPath -Certificate $cert -HashAlgorithm SHA256
Write-Host "sign-for-wdac: $($sig.Status) $([System.IO.Path]::GetFileName($TargetPath))"
