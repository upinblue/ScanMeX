<#
.SYNOPSIS
    Checks a built ScanMe MSI for the things that have silently gone missing before.

.DESCRIPTION
    An MSI that installs without error is not the same as an MSI that installs what it should. This
    project has shipped one that created no Start menu entry at all -- the component carried a GUID
    inherited from upstream NAPS2, and Windows Installer reference-counts components by GUID across
    products, so on any machine that had once seen NAPS2 the shortcut was "already installed" and
    nothing appeared. Nothing failed; there was simply no entry.

    So the package gets read back rather than trusted. Run it after `pkg msi` and before publishing:

        pwsh tools/setup/Test-MsiPackage.ps1 -MsiPath NAPS2.Setup/publish/1.1.0/ScanMe-1.1.0-win-x64.msi

    Exits 0 when every check passes, 1 otherwise, so it can gate a release step.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $MsiPath,

    # The version the MSI is supposed to carry. Defaults to whatever VersionTargets.targets says, which
    # is the value `pkg msi` would have used.
    [string] $ExpectedVersion
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $MsiPath)) {
    Write-Error "No MSI at '$MsiPath'."
    exit 1
}
$MsiPath = (Resolve-Path $MsiPath).Path

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $ExpectedVersion) {
    $versionTargets = Join-Path $repoRoot 'NAPS2.Setup\targets\VersionTargets.targets'
    if (Test-Path $versionTargets) {
        $ExpectedVersion = ([xml](Get-Content $versionTargets -Raw)).Project.PropertyGroup.Version
    }
}

$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.GetType().InvokeMember(
    'OpenDatabase', 'InvokeMethod', $null, $installer, @($MsiPath, 0))

function Invoke-MsiQuery {
    param([string] $Sql)

    $view = $database.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $database, @($Sql))
    # Out-Null is not optional: InvokeMember returns null here, and an uncaptured null lands in the
    # function's output stream ahead of the rows, so the caller gets a null first element.
    $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null
    $rows = @()
    while ($true) {
        $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
        if ($null -eq $record) { break }
        $fieldCount = $record.GetType().InvokeMember('FieldCount', 'GetProperty', $null, $record, $null)
        $fields = @()
        for ($i = 1; $i -le $fieldCount; $i++) {
            $fields += [string] $record.GetType().InvokeMember(
                'StringData', 'GetProperty', $null, $record, @($i))
        }
        $rows += , $fields
    }
    $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null) | Out-Null
    return , $rows
}

function Get-MsiProperty {
    param([string] $Name)

    $rows = Invoke-MsiQuery "SELECT Value FROM Property WHERE Property='$Name'"
    if ($rows.Count -eq 0) { return $null }
    return $rows[0][0]
}

$failures = @()
function Assert-That {
    param([string] $What, [bool] $Ok, [string] $Detail)

    if ($Ok) {
        Write-Host "  PASS  $What" -ForegroundColor Green
        if ($Detail) { Write-Host "        $Detail" -ForegroundColor DarkGray }
    } else {
        Write-Host "  FAIL  $What" -ForegroundColor Red
        if ($Detail) { Write-Host "        $Detail" -ForegroundColor Red }
        $script:failures += $What
    }
}

Write-Host "Checking $(Split-Path -Leaf $MsiPath)"
Write-Host ""

# --- The Start menu entry -------------------------------------------------------------------------
# Both halves matter: a Shortcut row pointing at a directory that is not under ProgramMenuFolder puts
# the entry somewhere nobody looks, and a shortcut whose component is in no feature is never installed.
$shortcuts = Invoke-MsiQuery "SELECT Shortcut, Directory_, Name, Component_, Target FROM Shortcut"
$startMenu = $shortcuts | Where-Object { $_[1] -eq 'ProgramMenuDir' }

Assert-That 'Start menu shortcut is in the package' ($null -ne $startMenu) $(
    if ($startMenu) { "$($startMenu[2]) -> $($startMenu[4])" }
    else { 'No Shortcut row targets ProgramMenuDir. Check the ApplicationShortcut component in setup.template.wxs.' })

if ($startMenu) {
    $component = $startMenu[3]
    $features = Invoke-MsiQuery "SELECT Feature_ FROM FeatureComponents WHERE Component_='$component'"
    Assert-That 'Its component belongs to a feature' ($features.Count -gt 0) $(
        if ($features.Count -gt 0) { "component '$component' in feature '$($features[0][0])'" }
        else { "Component '$component' is in no feature, so it is never installed." })

    $dir = Invoke-MsiQuery "SELECT Directory_Parent, DefaultDir FROM Directory WHERE Directory='ProgramMenuDir'"
    Assert-That 'It sits under ProgramMenuFolder' ($dir.Count -gt 0 -and $dir[0][0] -eq 'ProgramMenuFolder') $(
        if ($dir.Count -gt 0) { "parent=$($dir[0][0]) name=$($dir[0][1])" } else { 'No ProgramMenuDir directory row.' })
}

# --- Identity -------------------------------------------------------------------------------------
# The rebranding left several of these reading NAPS2, which is what an operator sees in the installer
# title bar and in Apps & features long after the product name was changed everywhere else.
$productName = Get-MsiProperty 'ProductName'
Assert-That 'Product is named ScanMe' ($productName -eq 'ScanMe') "ProductName = '$productName'"

$manufacturer = Get-MsiProperty 'Manufacturer'
Assert-That 'Publisher is up in blue GmbH' ($manufacturer -eq 'up in blue GmbH') "Manufacturer = '$manufacturer'"

$productVersion = Get-MsiProperty 'ProductVersion'
if ($ExpectedVersion) {
    # Windows Installer only *compares* the first three fields, so a four-field version may be stored
    # in full but is equivalent to its first three. Accept either spelling.
    $expectedShort = ($ExpectedVersion -split '\.')[0..2] -join '.'
    Assert-That "Version is $ExpectedVersion" ($productVersion -eq $expectedShort -or $productVersion -eq $ExpectedVersion) `
        "ProductVersion = '$productVersion'"
} else {
    Write-Host "  SKIP  Version (no expected value given)" -ForegroundColor DarkGray
    Write-Host "        ProductVersion = '$productVersion'" -ForegroundColor DarkGray
}

# The UpgradeCode is what makes the next version replace this one instead of installing beside it.
# Changing it silently turns every future release into a parallel installation.
$upgradeCode = Get-MsiProperty 'UpgradeCode'
Assert-That 'Upgrade code is unchanged' ($upgradeCode -eq '{FEB82971-B3E6-4F19-9684-1D543E644D73}') `
    "UpgradeCode = '$upgradeCode'"

# --- The application itself -------------------------------------------------------------------------
# MSI's SQL dialect has no LIKE, so the filtering happens here rather than in the query.
$allFiles = Invoke-MsiQuery "SELECT File, FileName FROM File"
$exe = $allFiles | Where-Object { $_[1] -match 'ScanMe\.exe' }
Assert-That 'ScanMe.exe is in the package' ($null -ne $exe) $(
    if ($exe) { "$(@($exe).Count) file row(s), $($allFiles.Count) files in total" }
    else { 'No File row names ScanMe.exe.' })

Write-Host ""
if ($failures.Count -gt 0) {
    Write-Host "$($failures.Count) check(s) failed. Do not publish this MSI." -ForegroundColor Red
    exit 1
}
Write-Host "All checks passed." -ForegroundColor Green
exit 0
