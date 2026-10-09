<#
.SYNOPSIS
  Publishes FixFinder and FixFinder Learn as single self-contained exes - FixFinder.exe and
  FixFinderLearn.exe, side by side - and signs them.

.DESCRIPTION
  Produces one file per application with the .NET runtime and WPF bundled inside, so each runs on
  a machine with no .NET installed. That is what makes it worth doing at all - a framework-dependent
  exe is a few hundred KB but is just a launcher for a runtime that has to already be there, which
  is no more portable than the DLL it replaces.

  FixFinder Learn goes in the same folder because that is where FixFinder looks for it: a finding's
  Learn about this button starts FixFinderLearn.exe from beside FixFinder.exe.

  Compression is on. It roughly halves the output at the cost of a slower first start, which is
  the right trade for a tool launched by hand rather than in a loop.

  Each exe is Authenticode-signed with the same local certificate the Debug DLLs use. Read the
  warning printed at the end before assuming that makes them runnable everywhere: local trust
  satisfies an Application Control policy, and it does not satisfy Smart App Control, which
  judges by reputation rather than by signature.

.PARAMETER CertificateSubject
  Which certificate to sign with, for example "CN=Your Name". Defaults to the
  FIXFINDER_CERT_SUBJECT environment variable, and failing that to the newest code-signing
  certificate in the personal store - so a fresh clone works without editing anything.

.PARAMETER Version
  The release being built, for example 2.4.1: the exes' file version, and the version a report
  saved as a web page names at its foot, with the commit after it. Left out, the build is numbered
  1.0.0, as .NET numbers a build nobody has given a version.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",

    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    # Skip the runtime bundle: much smaller, but requires the .NET 8 desktop runtime installed.
    [switch]$FrameworkDependent,

    [switch]$SkipSigning,

    [string]$CertificateSubject = $env:FIXFINDER_CERT_SUBJECT
)

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$output = Join-Path $root "publish"

# Each application: its project, the exe dotnet names after its assembly, and the name a person sees. FixFinder.Gui is
# the assembly, but the thing a person double-clicks should just be FixFinder.
$applications = @(
    @{ Project = "FixFinder.Gui\FixFinder.Gui.csproj"; Built = "FixFinder.Gui.exe"; Final = "FixFinder.exe" },
    @{ Project = "FixFinder.Learn\FixFinder.Learn.csproj"; Built = "FixFinder.Learn.exe"; Final = "FixFinderLearn.exe" }
)

foreach ($application in $applications) {
    $projectPath = Join-Path $root $application.Project
    if (-not (Test-Path $projectPath)) { throw "Not found: $projectPath" }
}

Write-Host ""
Write-Host "Publishing FixFinder and FixFinder Learn ($Configuration, $Runtime, $(if ($FrameworkDependent) { 'framework-dependent' } else { 'self-contained' }))..."
Write-Host ""

# Everything except Logs, which is not ours to throw away: it is the record of what FixFinder
# ran and what it wrote, for a tool whose whole job is modifying source. Keeping it also means a
# republish no longer fails outright when the app happens to be open holding today's log.
if (Test-Path $output) {
    Get-ChildItem $output -Force |
        Where-Object { $_.Name -ne "Logs" } |
        Remove-Item -Recurse -Force
}

$sharedArguments = @(
    "-c", $Configuration,
    "-r", $Runtime,
    "-o", $output,
    "--nologo",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=embedded",
    "-p:SatelliteResourceLanguages=en"
)

if ($Version) { $sharedArguments += "-p:Version=$Version" }

if ($FrameworkDependent) {
    $sharedArguments += "--self-contained:false"
} else {
    $sharedArguments += "--self-contained:true"
    # Compression only applies to a self-contained bundle.
    $sharedArguments += "-p:EnableCompressionInSingleFile=true"
}

$finishedExes = @()

foreach ($application in $applications) {
    & dotnet publish (Join-Path $root $application.Project) @sharedArguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish of $($application.Project) failed with exit code $LASTEXITCODE" }

    $published = Join-Path $output $application.Built
    $final = Join-Path $output $application.Final

    if (-not (Test-Path $published)) { throw "Expected $published but it was not produced" }
    if (Test-Path $final) { Remove-Item $final -Force }
    Move-Item $published $final

    $finishedExes += $final
}

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
        foreach ($finishedExe in $finishedExes) {
            $sig = Set-AuthenticodeSignature -FilePath $finishedExe -Certificate $cert -HashAlgorithm SHA256
            Write-Host "Signed $(Split-Path $finishedExe -Leaf): $($sig.Status)  [$($cert.Subject)]"
        }
    } else {
        Write-Warning "No code-signing certificate found - the exes are unsigned."
    }
}

Write-Host ""
foreach ($finishedExe in $finishedExes) {
    $size = [Math]::Round((Get-Item $finishedExe).Length / 1MB, 1)
    Write-Host "Built: $finishedExe  ($size MB)"
}

$finalNames = $applications | ForEach-Object { $_.Final }
$extra = Get-ChildItem $output -File | Where-Object { $finalNames -notcontains $_.Name }
if ($extra) {
    Write-Host ""
    Write-Host "Alongside them:"
    $extra | ForEach-Object { Write-Host ("  {0}  ({1:N0} bytes)" -f $_.Name, $_.Length) }
}

Write-Host ""
Write-Host "Note: this machine enforces Smart App Control, which judges an executable by"
Write-Host "reputation rather than by signature. A self-signed exe has none, so Windows may"
Write-Host "refuse to start these here even though they are signed and the certificate is"
Write-Host "trusted locally. If that happens, run-fixfinder.cmd still works - it launches the"
Write-Host "DLL through dotnet.exe, which is already trusted."
Write-Host ""
