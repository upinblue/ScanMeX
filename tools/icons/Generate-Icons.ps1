<#
.SYNOPSIS
    Regenerates NAPS2.Lib/Icons from the Fluent UI System Icons set.

.DESCRIPTION
    Reads icon-map.tsv, downloads the matching SVG from microsoft/fluentui-system-icons (MIT) and
    renders it to PNG at the size the existing Icons.resx entry expects. Only names that already
    have a resx entry are written, so this script cannot introduce an icon that nothing looks up.

    Rendering goes through WPF's Geometry.Parse, which understands the SVG path mini-language. The
    Fluent icons are single-colour fill paths with no strokes, gradients or clip paths, so this is
    exact rather than an approximation. Icons are rendered in black; the app tints them at runtime
    (see DefaultIconProvider), which is what makes dark mode work.

    Downloaded SVGs are cached in .cache so a re-run is offline and fast. Delete .cache to refresh.

.PARAMETER WhatIf
    Report what would be written without touching NAPS2.Lib/Icons.

.EXAMPLE
    pwsh tools/icons/Generate-Icons.ps1
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $Branch = 'main'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$toolDir  = $PSScriptRoot
$repoRoot = Split-Path (Split-Path $toolDir -Parent) -Parent
$iconsDir = Join-Path $repoRoot 'NAPS2.Lib\Icons'
$resxPath = Join-Path $repoRoot 'NAPS2.Lib\Icons.resx'
$cacheDir = Join-Path $toolDir '.cache'
# -WhatIf:$false so a dry run still populates the cache and can therefore validate the mapping.
New-Item -ItemType Directory -Force $cacheDir -WhatIf:$false | Out-Null

# ---------------------------------------------------------------------------------------------
# 1. The upstream icon index: which names exist, and in which design sizes.
# ---------------------------------------------------------------------------------------------
$indexPath = Join-Path $cacheDir 'icons_regular.md'
if (-not (Test-Path $indexPath)) {
    Write-Host "Downloading icon index..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri "https://raw.githubusercontent.com/microsoft/fluentui-system-icons/$Branch/icons_regular.md" -OutFile $indexPath
}
$folderOf = @{}   # base name -> asset folder
$sizesOf  = @{}   # base name -> int[] design sizes
foreach ($line in Get-Content $indexPath) {
    if ($line -notmatch '^\|') { continue }
    $cols = $line.Split('|')
    if ($cols.Count -lt 5 -or $cols[1] -eq 'Name' -or $cols[1] -match '^-+$') { continue }
    foreach ($m in [regex]::Matches($cols[4], 'ic_fluent_([a-z0-9_]+)_(\d+)_regular')) {
        $b = $m.Groups[1].Value
        if (-not $folderOf.ContainsKey($b)) { $folderOf[$b] = $cols[1]; $sizesOf[$b] = @() }
        $sizesOf[$b] += [int]$m.Groups[2].Value
    }
}
Write-Host "Index: $($folderOf.Count) icons" -ForegroundColor DarkGray

# ---------------------------------------------------------------------------------------------
# 2. The mapping, and the resx entries it is allowed to write.
# ---------------------------------------------------------------------------------------------
$map = @{}
foreach ($line in Get-Content (Join-Path $toolDir 'icon-map.tsv')) {
    if ($line -match '^\s*(#|$)') { continue }
    $p = $line -split "`t"
    if ($p.Count -lt 2) { throw "Malformed mapping line (expected a tab): $line" }
    $map[$p[0].Trim()] = $p[1].Trim()
}
$badNames = $map.Values | Sort-Object -Unique | Where-Object { -not $folderOf.ContainsKey($_) }
if ($badNames) { throw "No such Fluent icon: $($badNames -join ', ')" }
Write-Host "Mapping: $($map.Count) entries, all resolve upstream" -ForegroundColor DarkGray

$resx = Get-Content $resxPath -Raw
$resxTargets = @{}   # resx name -> relative png path
foreach ($m in [regex]::Matches($resx, '(?s)<data name="([A-Za-z0-9_]+)" type="System\.Resources\.ResXFileRef[^>]*>\s*<value>([^;]+);')) {
    $resxTargets[$m.Groups[1].Value] = $m.Groups[2].Value.Trim()
}

# ---------------------------------------------------------------------------------------------
# 3. Fetch + render.
# ---------------------------------------------------------------------------------------------
function Get-Svg([string] $base, [int] $wantSize) {
    # Pick the design size closest to (but not below, where possible) the target: the small
    # variants are drawn with fewer details on purpose, so 16px output wants the 16px drawing.
    $avail = $sizesOf[$base] | Sort-Object -Unique
    $pick  = $avail | Where-Object { $_ -ge $wantSize } | Select-Object -First 1
    if (-not $pick) { $pick = $avail | Select-Object -Last 1 }

    $file = "ic_fluent_${base}_${pick}_regular.svg"
    $path = Join-Path $cacheDir $file
    if (-not (Test-Path $path)) {
        $folder = [uri]::EscapeDataString($folderOf[$base])
        Invoke-WebRequest -Uri "https://raw.githubusercontent.com/microsoft/fluentui-system-icons/$Branch/assets/$folder/SVG/$file" -OutFile $path
    }
    @{ Text = (Get-Content $path -Raw); DesignSize = $pick }
}

function Write-Png([hashtable] $svg, [int] $size, [string] $outPath) {
    $text = $svg.Text
    if ($text -notmatch 'viewBox="([\d\.\-\s]+)"') { throw "No viewBox in SVG for $outPath" }
    $vb = $Matches[1].Trim() -split '\s+'
    $vbX = [double]$vb[0]; $vbY = [double]$vb[1]; $vbW = [double]$vb[2]; $vbH = [double]$vb[3]

    $paths = [regex]::Matches($text, '<path[^>]*\sd="([^"]+)"')
    if ($paths.Count -eq 0) { throw "No path data in SVG for $outPath" }
    if ($text -match '<(linearGradient|radialGradient|image|text)\b') {
        throw "SVG for $outPath uses an unsupported element; this renderer only handles fill paths"
    }

    $group = New-Object System.Windows.Media.GeometryGroup
    $group.FillRule = [System.Windows.Media.FillRule]::Nonzero
    foreach ($p in $paths) { $group.Children.Add([System.Windows.Media.Geometry]::Parse($p.Groups[1].Value)) }

    $scale = [double]$size / [System.Math]::Max($vbW, $vbH)
    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $tg = New-Object System.Windows.Media.TransformGroup
    $tg.Children.Add((New-Object System.Windows.Media.TranslateTransform(-$vbX, -$vbY)))
    $tg.Children.Add((New-Object System.Windows.Media.ScaleTransform($scale, $scale)))
    $dc.PushTransform($tg)
    $dc.DrawGeometry([System.Windows.Media.Brushes]::Black, $null, $group)
    $dc.Pop()
    $dc.Close()

    # Render at 96dpi so the transform above maps viewBox units to pixels 1:1 ...
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap(
        $size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($visual)

    # ... then stamp the file with the same 192dpi the rest of NAPS2's icons declare. This is not
    # cosmetic metadata: ToolStripDoubleButton (the stacked Move up/Move down and Settings/About
    # buttons) paints with Graphics.DrawImage(image, Point), the overload that sizes the image by
    # its physical size, i.e. graphicsDpi/imageDpi. A 96dpi icon draws at double size there on a
    # 192dpi screen while the text is still offset by the pixel width, so the label lands on top of
    # the glyph. Same pixels, different pHYs chunk.
    $stride = $size * 4
    $pixels = New-Object byte[] ($stride * $size)
    $rtb.CopyPixels($pixels, $stride, 0)
    $final = [System.Windows.Media.Imaging.BitmapSource]::Create(
        $size, $size, 192, 192, [System.Windows.Media.PixelFormats]::Pbgra32, $null, $pixels, $stride)

    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($final))
    $fs = [System.IO.File]::Create($outPath)
    try { $enc.Save($fs) } finally { $fs.Dispose() }
}

# resx suffix -> (output size, file suffix)
$variants = @(
    @{ Suffix = '_small'; Size = 16; File = '-small' },
    @{ Suffix = '';       Size = 32; File = '' },
    @{ Suffix = '_hires'; Size = 64; File = '-hires' }
)

$written = 0; $skipped = @()
foreach ($baseName in ($map.Keys | Sort-Object)) {
    # Exact resx names in the map (e.g. arrow_up_small) are overrides, handled with their variant.
    if ($resxTargets.ContainsKey($baseName) -and ($baseName -match '_(small|hires)$')) { continue }

    foreach ($v in $variants) {
        $resxName = $baseName + $v.Suffix
        if (-not $resxTargets.ContainsKey($resxName)) { continue }

        $fluent = if ($map.ContainsKey($resxName)) { $map[$resxName] } else { $map[$baseName] }
        $outPath = Join-Path $iconsDir ($baseName + $v.File + '.png')

        if ($PSCmdlet.ShouldProcess($outPath, "render $fluent @ $($v.Size)px")) {
            $svg = Get-Svg $fluent $v.Size
            Write-Png $svg $v.Size $outPath
        }
        $written++
    }
}

# Variant-level overrides whose base is not itself mapped would be missed above; catch them here.
foreach ($resxName in ($map.Keys | Where-Object { $_ -match '_(small|hires)$' -and $resxTargets.ContainsKey($_) })) {
    $v = $variants | Where-Object { $resxName -match ($_.Suffix + '$') -and $_.Suffix } | Select-Object -First 1
    $baseName = $resxName -replace '_(small|hires)$', ''
    $outPath = Join-Path $iconsDir ($baseName + $v.File + '.png')
    if ($PSCmdlet.ShouldProcess($outPath, "render $($map[$resxName]) @ $($v.Size)px (override)")) {
        Write-Png (Get-Svg $map[$resxName] $v.Size) $v.Size $outPath
    }
}

Write-Host "Wrote $written PNG(s) to $iconsDir" -ForegroundColor Green
$unmapped = $resxTargets.Keys |
    ForEach-Object { $_ -replace '_(small|hires)$', '' } |
    Sort-Object -Unique |
    Where-Object { -not $map.ContainsKey($_) }
if ($unmapped) { Write-Host "Left as-is (see icon-map.tsv header): $($unmapped -join ', ')" -ForegroundColor DarkGray }
