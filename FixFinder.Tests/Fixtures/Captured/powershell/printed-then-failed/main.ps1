$names = @("Ada", "Bob", $null)
foreach ($name in $names) {
    Write-Output $name.ToUpper()
}
