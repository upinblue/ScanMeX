<#
.SYNOPSIS
    Turns the ScanMe manuals into PDFs.

.DESCRIPTION
    Markdown -> HTML -> PDF, with nothing to install: the Markdown is converted by PowerShell 7's own
    ConvertFrom-Markdown (Markdig), pdf\style.css is inlined, and the resulting page is printed by
    headless Edge, which is on every Windows machine.

    The Markdown stays plain enough to read on GitHub. The only convention the build adds is the HTML
    comment

        <!-- pagebreak -->

    which is invisible on GitHub and becomes a page break in the PDF. Everything before the first one is
    the cover page.

    By default the pages carry no header or footer, which is what makes them look like a typeset
    document rather than a printed web page. The manual is cross-referenced by chapter number, not by
    page, so nothing depends on page numbers being there.

    -PageNumbers adds them. It first tries the DevTools protocol, which is the only way to get a footer
    of our own ("ScanMe - Benutzerhandbuch" on the left, "Seite x von y" on the right). Some browser
    builds -- Edge 151 among them -- answer Page.printToPDF with "Printing is not available"; the script
    then falls back to the browser's own header and footer, which does carry a page number but also the
    date and the file path.

.PARAMETER Language
    de, en, or both (the default).

.PARAMETER PageNumbers
    Adds a footer with the running title and the page number. See above for the caveat.

.PARAMETER KeepHtml
    Leaves the intermediate HTML file next to the Markdown, for checking the layout in a browser.

.EXAMPLE
    pwsh docs/manual/Build-Pdf.ps1
    pwsh docs/manual/Build-Pdf.ps1 -Language de -KeepHtml
#>
[CmdletBinding()]
param(
    [ValidateSet('de', 'en', 'both')]
    [string] $Language = 'both',

    [switch] $PageNumbers,

    [switch] $KeepHtml
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSCommandPath
$cssPath = Join-Path $root 'pdf\style.css'
$outDir = Join-Path $root 'pdf'

$documents = @(
    [pscustomobject]@{
        Language = 'de'
        Source   = 'ScanMe-Handbuch-de.md'
        Output   = 'ScanMe-Handbuch-de.pdf'
        Footer   = 'ScanMe &ndash; Benutzerhandbuch'
        PageWord = 'Seite'
        OfWord   = 'von'
    }
    [pscustomobject]@{
        Language = 'en'
        Source   = 'ScanMe-Manual-en.md'
        Output   = 'ScanMe-Manual-en.pdf'
        Footer   = 'ScanMe &ndash; User Manual'
        PageWord = 'Page'
        OfWord   = 'of'
    }
)
if ($Language -ne 'both') {
    $documents = $documents | Where-Object { $_.Language -eq $Language }
}

# A4 and the margins style.css declares, in inches, because that is what the protocol takes.
$Paper = @{ Width = 8.27; Height = 11.69; Top = 0.71; Bottom = 0.63; Side = 0.63 }

function Find-Edge {
    $candidates = @(
        "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
        "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
        "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
        "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { return $c }
    }
    throw 'Neither Edge nor Chrome was found. One of them is needed to print the PDF.'
}

function Build-Html([string] $MarkdownPath, [string] $Css, [string] $Lang) {
    # Markdig, through PowerShell's own cmdlet. Pipe tables, fenced code and raw HTML all come out as
    # they should, and the <!-- pagebreak --> comments are passed through untouched.
    $body = (ConvertFrom-Markdown -Path $MarkdownPath).Html

    $marker = '<!-- pagebreak -->'
    $i = $body.IndexOf($marker)
    if ($i -ge 0) {
        $cover = $body.Substring(0, $i)
        $rest = $body.Substring($i + $marker.Length)
        $body = "<section class=`"cover`">$cover</section>$rest"
    }
    $body = $body.Replace($marker, '<div class="page-break"></div>')

    $title = 'ScanMe'
    if ($body -match '<h1[^>]*>(.*?)</h1>') { $title = $Matches[1] -replace '<[^>]+>', '' }

    return @"
<!DOCTYPE html>
<html lang="$Lang">
<head>
<meta charset="utf-8">
<title>$title</title>
<style>
$Css
</style>
</head>
<body>
$body
</body>
</html>
"@
}

function Get-FreePort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
    $listener.Stop()
    return $port
}

# --- DevTools protocol ------------------------------------------------------
# Enough of it to open one page and print it. Everything is request/response on one socket, so a plain
# send-then-read-until-our-id loop is all the bookkeeping needed.

function Send-Cdp([System.Net.WebSockets.ClientWebSocket] $Socket, [int] $Id, [string] $Method, [hashtable] $Params) {
    $message = @{ id = $Id; method = $Method }
    if ($Params) { $message.params = $Params }
    $json = $message | ConvertTo-Json -Depth 10 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($json)
    $segment = [ArraySegment[byte]]::new($bytes)
    $Socket.SendAsync($segment, 'Text', $true, [Threading.CancellationToken]::None).GetAwaiter().GetResult() | Out-Null

    # Events and responses share the socket; read until the reply carrying our id turns up.
    $buffer = [byte[]]::new(1MB)
    while ($true) {
        $sb = [Text.StringBuilder]::new()
        do {
            $segment = [ArraySegment[byte]]::new($buffer)
            $result = $Socket.ReceiveAsync($segment, [Threading.CancellationToken]::None).GetAwaiter().GetResult()
            [void] $sb.Append([Text.Encoding]::UTF8.GetString($buffer, 0, $result.Count))
        } while (-not $result.EndOfMessage)

        $reply = $sb.ToString() | ConvertFrom-Json
        if ($reply.id -eq $Id) {
            if ($reply.error) { throw "DevTools $Method failed: $($reply.error.message)" }
            return $reply.result
        }
    }
}

function Invoke-PrintToPdf {
    param(
        [string] $Edge,
        [string] $HtmlPath,
        [string] $PdfPath,
        [string] $FooterHtml
    )

    $port = Get-FreePort
    $profileDir = Join-Path ([IO.Path]::GetTempPath()) ("scanme-manual-" + [Guid]::NewGuid().ToString('N'))
    $url = ([Uri] $HtmlPath).AbsoluteUri
    $arguments = @(
        # Plain --headless, not --headless=new: Edge's new headless answers Page.printToPDF with
        # "Printing is not available", and the footer is the whole reason for going through the
        # protocol at all.
        '--headless'
        '--disable-gpu'
        '--no-first-run'
        '--no-default-browser-check'
        '--disable-extensions'
        "--user-data-dir=$profileDir"
        "--remote-debugging-port=$port"
        $url
    )
    $edgeProcess = Start-Process -FilePath $Edge -ArgumentList $arguments -PassThru -WindowStyle Hidden
    $socket = $null
    try {
        # Wait for the page target to show up and finish loading.
        $target = $null
        $deadline = (Get-Date).AddSeconds(30)
        while ((Get-Date) -lt $deadline) {
            try {
                $targets = Invoke-RestMethod -Uri "http://127.0.0.1:$port/json/list" -TimeoutSec 3
                $target = $targets | Where-Object { $_.type -eq 'page' -and $_.webSocketDebuggerUrl } | Select-Object -First 1
                if ($target) { break }
            } catch { }
            Start-Sleep -Milliseconds 300
        }
        if (-not $target) { throw "The browser did not open a debuggable page within 30 seconds." }

        $socket = [System.Net.WebSockets.ClientWebSocket]::new()
        $socket.ConnectAsync([Uri] $target.webSocketDebuggerUrl, [Threading.CancellationToken]::None).GetAwaiter().GetResult() | Out-Null

        $id = 0
        $deadline = (Get-Date).AddSeconds(30)
        while ((Get-Date) -lt $deadline) {
            $state = Send-Cdp $socket (++$id) 'Runtime.evaluate' @{
                expression = 'document.readyState'
                returnByValue = $true
            }
            if ($state.result.value -eq 'complete') { break }
            Start-Sleep -Milliseconds 300
        }
        # Fonts and images are loaded by then, but give the layout one frame to settle.
        Start-Sleep -Milliseconds 500

        $params = @{
            printBackground     = $true
            paperWidth          = $Paper.Width
            paperHeight         = $Paper.Height
            marginTop           = $Paper.Top
            marginBottom        = $Paper.Bottom
            marginLeft          = $Paper.Side
            marginRight         = $Paper.Side
            preferCSSPageSize   = $false
            displayHeaderFooter = [bool] $FooterHtml
            headerTemplate      = '<span></span>'
            footerTemplate      = $FooterHtml ? $FooterHtml : '<span></span>'
        }
        $result = Send-Cdp $socket (++$id) 'Page.printToPDF' $params
        [IO.File]::WriteAllBytes($PdfPath, [Convert]::FromBase64String($result.data))
    }
    finally {
        if ($socket) { try { $socket.Dispose() } catch { } }
        if ($edgeProcess -and -not $edgeProcess.HasExited) {
            try { $edgeProcess.Kill($true) } catch { }
        }
        Start-Sleep -Milliseconds 300
        Remove-Item -Recurse -Force $profileDir -ErrorAction SilentlyContinue
    }
}

function Invoke-PrintToPdfSimple {
    param([string] $Edge, [string] $HtmlPath, [string] $PdfPath, [switch] $WithHeaderFooter)

    $profileDir = Join-Path ([IO.Path]::GetTempPath()) ("scanme-manual-" + [Guid]::NewGuid().ToString('N'))
    $arguments = @(
        '--headless=new'
        '--disable-gpu'
        '--no-first-run'
        '--no-default-browser-check'
        "--user-data-dir=$profileDir"
        '--run-all-compositor-stages-before-draw'
        '--virtual-time-budget=10000'
        ($WithHeaderFooter ? '--print-to-pdf-header-footer' : '--no-pdf-header-footer')
        "--print-to-pdf=$PdfPath"
        (([Uri] $HtmlPath).AbsoluteUri)
    )
    Start-Process -FilePath $Edge -ArgumentList $arguments -Wait -NoNewWindow | Out-Null
    Remove-Item -Recurse -Force $profileDir -ErrorAction SilentlyContinue
}

# --- build ------------------------------------------------------------------

$edge = Find-Edge
$css = Get-Content -Path $cssPath -Raw
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

foreach ($doc in $documents) {
    $mdPath = Join-Path $root $doc.Source
    if (-not (Test-Path $mdPath)) { throw "Missing source file: $mdPath" }

    # The HTML is written beside the Markdown so the relative image paths in assets\ resolve.
    $htmlPath = Join-Path $root ('.' + [IO.Path]::GetFileNameWithoutExtension($doc.Source) + '.html')
    $pdfPath = Join-Path $outDir $doc.Output
    Build-Html -MarkdownPath $mdPath -Css $css -Lang $doc.Language | Set-Content -Path $htmlPath -Encoding utf8

    Write-Host "Printing $($doc.Source) ..."
    try {
        if ($PageNumbers) {
            $footer =
                '<div style="width:100%;margin:0 16mm;font-family:''Segoe UI'',sans-serif;font-size:8px;' +
                'color:#7a7a7a;display:flex;justify-content:space-between;">' +
                "<span>$($doc.Footer)</span>" +
                "<span>$($doc.PageWord) <span class=`"pageNumber`"></span> $($doc.OfWord) " +
                '<span class="totalPages"></span></span></div>'
            try {
                Invoke-PrintToPdf -Edge $edge -HtmlPath $htmlPath -PdfPath $pdfPath -FooterHtml $footer
            }
            catch {
                Write-Warning "This browser will not print through the DevTools protocol ($($_.Exception.Message)). Using its own header and footer instead, which also carries the date and the file path."
                Invoke-PrintToPdfSimple -Edge $edge -HtmlPath $htmlPath -PdfPath $pdfPath -WithHeaderFooter
            }
        }
        else {
            Invoke-PrintToPdfSimple -Edge $edge -HtmlPath $htmlPath -PdfPath $pdfPath
        }
    }
    finally {
        if (-not $KeepHtml) { Remove-Item -Force $htmlPath -ErrorAction SilentlyContinue }
    }

    if (-not (Test-Path $pdfPath)) { throw "$($doc.Output) was not produced." }
    $kb = [int]((Get-Item $pdfPath).Length / 1KB)
    Write-Host "  -> $pdfPath ($kb KB)"
}
