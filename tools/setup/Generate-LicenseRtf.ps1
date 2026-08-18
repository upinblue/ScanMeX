<#
.SYNOPSIS
    Regenerates NAPS2.Setup/license.rtf from the repository's LICENSE file.

.DESCRIPTION
    The MSI and the EXE installer both show a licence page, and WiX's WixUILicenseRtf control only
    renders RTF. The file it renders used to be upstream NAPS2's Word-exported RTF, which meant the
    installer's licence page and the repository's LICENSE were two separate documents: the LICENSE was
    updated for the fork while the installer kept telling operators they were installing
    "NAPS2 - Not Another PDF Scanner" from sourceforge.

    Generating one from the other is what keeps them from drifting again. Run this after editing
    LICENSE, and commit both.

        pwsh tools/setup/Generate-LicenseRtf.ps1

    The output is deliberately a minimal RTF -- one font, one size, a \par per line -- rather than
    anything Word would produce. It has to survive being hand-inspected in a diff, and the Word export
    it replaces was 60 KB of font tables for four pages of plain text.
#>
[CmdletBinding()]
param(
    [string] $LicensePath,
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $LicensePath) { $LicensePath = Join-Path $repoRoot 'LICENSE' }
if (-not $OutputPath) { $OutputPath = Join-Path $repoRoot 'NAPS2.Setup\license.rtf' }

if (-not (Test-Path $LicensePath)) {
    throw "No LICENSE at '$LicensePath'."
}

$lines = [System.IO.File]::ReadAllText($LicensePath) -split "`r?`n"

$sb = [System.Text.StringBuilder]::new()

# Verdana at 8pt is what the previous RTF used, and the WiX licence control is sized for it: a larger
# face turns the GPL's numbered clauses into a scrollbar the operator will not use.
[void]$sb.Append('{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fswiss\fcharset0 Verdana;}}')
[void]$sb.Append('{\colortbl;\red0\green0\blue0;}')
[void]$sb.Append('\viewkind4\uc1\pard\f0\fs16 ')

foreach ($line in $lines) {
    $text = $line

    # RTF's own specials first, or the escapes below would be escaped again.
    $text = $text.Replace('\', '\\').Replace('{', '\{').Replace('}', '\}')

    # Anything outside cpg1252 has to go out as a \uN escape with an ASCII fallback, or the licence
    # page shows mojibake. The GPL text is ASCII, but the ScanMe header carries the odd non-ASCII
    # character and a future edit may carry more.
    $encoded = [System.Text.StringBuilder]::new()
    foreach ($ch in $text.ToCharArray()) {
        $code = [int] $ch
        if ($code -lt 128) {
            [void]$encoded.Append($ch)
        } else {
            # \uN needs a signed 16-bit value, and '?' is the fallback for readers that ignore \u.
            $signed = if ($code -gt 32767) { $code - 65536 } else { $code }
            [void]$encoded.Append("\u$signed`?")
        }
    }

    [void]$sb.Append($encoded.ToString())
    [void]$sb.Append("\par`r`n")
}

[void]$sb.Append('}')

# WiX reads this as bytes, and RTF is a 7-bit format by design -- write ASCII so the file cannot pick
# up a BOM that the control would render as three stray characters at the top of the page.
[System.IO.File]::WriteAllText($OutputPath, $sb.ToString(), [System.Text.Encoding]::ASCII)

$size = (Get-Item $OutputPath).Length
Write-Host "Wrote $OutputPath ($($lines.Count) lines, $size bytes) from $LicensePath"
