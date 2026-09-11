<#
.SYNOPSIS
  Puts a "FixFinder" shortcut on the Desktop pointing at the published exe.

.DESCRIPTION
  The shortcut's working directory is set to the folder holding the exe, which is the part
  worth getting right: FixFinder writes its Logs\ folder relative to where it starts, and a
  shortcut launched with the Desktop as its working directory would scatter run logs there
  instead of keeping them beside the tool.

  Run publish-exe.ps1 first. The shortcut points at a specific file, so republishing to the
  same path keeps working, but moving the folder means running this again.

  The .lnk itself is gitignored - it holds absolute paths and is specific to this machine.
#>
[CmdletBinding()]
param(
    # Defaults to the published single-file exe; pass -Target to point at something else.
    [string]$Target = (Join-Path $PSScriptRoot "publish\FixFinder.exe"),

    [string]$Name = "FixFinder",

    # Replace an existing shortcut of the same name without asking.
    [switch]$Force
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $Target)) {
    throw "Not found: $Target`n`nBuild it first:  powershell -ExecutionPolicy Bypass -File publish-exe.ps1"
}

$Target = (Resolve-Path $Target).Path
$workingDirectory = Split-Path $Target -Parent

# GetFolderPath rather than "$env:USERPROFILE\Desktop": this account's Desktop is redirected
# into OneDrive, and the literal path would silently create a shortcut nobody can see.
$desktop = [Environment]::GetFolderPath('DesktopDirectory')
$shortcut = Join-Path $desktop "$Name.lnk"

if ((Test-Path $shortcut) -and -not $Force) {
    Write-Host "Replacing the existing shortcut at $shortcut"
}

$shell = New-Object -ComObject WScript.Shell

try {
    $link = $shell.CreateShortcut($shortcut)
    $link.TargetPath = $Target
    $link.WorkingDirectory = $workingDirectory
    $link.IconLocation = "$Target,0"
    $link.Description = "FixFinder - run a program, catch its crash, and look for a published fix"
    $link.WindowStyle = 1
    $link.Save()
}
finally {
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
}

# Read it back rather than trusting the write - a shortcut that points at the wrong thing looks
# identical to one that points at the right thing until it is double-clicked.
$verify = New-Object -ComObject WScript.Shell
try {
    $check = $verify.CreateShortcut($shortcut)

    Write-Host ""
    Write-Host "Created: $shortcut"
    Write-Host "  target      : $($check.TargetPath)"
    Write-Host "  starts in   : $($check.WorkingDirectory)"
    Write-Host "  target size : $([Math]::Round((Get-Item $check.TargetPath).Length / 1MB, 1)) MB"
    Write-Host "  signature   : $((Get-AuthenticodeSignature $check.TargetPath).Status)"
    Write-Host ""
}
finally {
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($verify)
}
