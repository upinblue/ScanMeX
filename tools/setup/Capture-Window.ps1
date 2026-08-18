<#
.SYNOPSIS
    Captures a window of the running ScanMe process to a PNG, for release-note screenshots.

.DESCRIPTION
    The window is rendered through PrintWindow rather than copied off the screen. Reading the screen is
    the obvious approach and it does not work here: it needs the window's rectangle in the same
    coordinate space the capturing process uses, and between the host's DPI awareness, a scaled display
    and DWM's extended frame bounds those disagreed badly enough to produce a capture of the right size
    containing the window in one corner and desktop in the rest.

    PrintWindow asks the window to draw itself into a device context, so no screen coordinates are
    involved at all, nothing has to be in the foreground, and an overlapping window cannot get into the
    shot. PW_RENDERFULLCONTENT (2) is what makes it work for a composited window -- without that flag a
    DWM-composited client area comes back blank.

    Usage:

        pwsh tools/setup/Capture-Window.ps1 -TitlePattern 'ScanMe -' -OutPath docs/releases/assets/1.0.16.0/main-window.png
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $OutPath,

    # Matched against window titles as a regex. The first match wins.
    [string] $TitlePattern = 'ScanMe',

    [string] $ProcessName = 'ScanMe',

    # Seconds to wait after bringing the window forward, so animations and lazy layout settle.
    [double] $SettleSeconds = 1.0
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

if (-not ('WindowCapture' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public class WindowCapture {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr hWnd, out int pid);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT value, int size);

    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);

    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const int SW_RESTORE = 9;
    public const uint PW_RENDERFULLCONTENT = 2;

    public static string TitleOf(IntPtr hWnd) {
        int len = GetWindowTextLength(hWnd);
        if (len == 0) return "";
        var sb = new StringBuilder(len + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
'@
}

# Before any coordinate is read or any pixel is copied.
[WindowCapture]::SetProcessDPIAware() | Out-Null

$processIds = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
if ($processIds.Count -eq 0) {
    throw "No '$ProcessName' process is running."
}

# EnumWindows rather than MainWindowHandle: a dialog is not its process's main window, and the About
# and profile dialogs are exactly what these screenshots are for.
$script:found = [IntPtr]::Zero
$script:foundTitle = ''
$callback = [WindowCapture+EnumWindowsProc] {
    param([IntPtr] $hWnd, [IntPtr] $lParam)

    if ($script:found -ne [IntPtr]::Zero) { return $true }
    if (-not [WindowCapture]::IsWindowVisible($hWnd)) { return $true }

    # Not $pid -- that is a read-only automatic variable, and assigning to it throws.
    $windowPid = 0
    [WindowCapture]::GetWindowThreadProcessId($hWnd, [ref] $windowPid) | Out-Null
    if ($processIds -notcontains $windowPid) { return $true }

    $title = [WindowCapture]::TitleOf($hWnd)
    if ($title -and $title -match $TitlePattern) {
        $script:found = $hWnd
        $script:foundTitle = $title
    }
    return $true
}
[WindowCapture]::EnumWindows($callback, [IntPtr]::Zero) | Out-Null

if ($script:found -eq [IntPtr]::Zero) {
    throw "No visible '$ProcessName' window matching '$TitlePattern'."
}

$handle = $script:found
[WindowCapture]::ShowWindow($handle, [WindowCapture]::SW_RESTORE) | Out-Null
[WindowCapture]::SetForegroundWindow($handle) | Out-Null
Start-Sleep -Seconds $SettleSeconds

# GetWindowRect, not the DWM extended bounds: PrintWindow draws the whole window including its frame,
# so the bitmap has to be the whole window's size. The extended bounds are smaller (they exclude the
# invisible resize border), and using them cropped the right and bottom edges off every capture.
$rect = New-Object WindowCapture+RECT
if (-not [WindowCapture]::GetWindowRect($handle, [ref] $rect)) {
    throw "GetWindowRect failed for '$script:foundTitle'."
}

$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) {
    throw "Window '$script:foundTitle' has no visible area ($width x $height)."
}

$outDir = Split-Path -Parent $OutPath
if ($outDir -and -not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}

$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$hdc = $graphics.GetHdc()
try {
    $ok = [WindowCapture]::PrintWindow($handle, $hdc, [WindowCapture]::PW_RENDERFULLCONTENT)
} finally {
    $graphics.ReleaseHdc($hdc)
}
$graphics.Dispose()

if (-not $ok) {
    $bitmap.Dispose()
    throw "PrintWindow failed for '$script:foundTitle'."
}

$bitmap.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()

Write-Host "Captured '$script:foundTitle' ($width x $height) to $OutPath"
