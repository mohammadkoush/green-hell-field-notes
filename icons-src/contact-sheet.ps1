# Build a labelled contact sheet of every icon, for approval.
#
# Drawn on the same near-black the minimap uses, at roughly the size an icon actually appears in
# game, plus a larger copy beside it - because "does it read at map size" and "is the shape right"
# are two different questions and a sheet that only answers one of them is not worth looking at.

param(
    [string]$IconDir = (Join-Path $PSScriptRoot '..\icons'),
    [string]$Out = (Join-Path $PSScriptRoot '..\..\project-tree\data\images\fieldnotes-icons.png')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$IconDir = [System.IO.Path]::GetFullPath($IconDir)
$Out = [System.IO.Path]::GetFullPath($Out)
$files = @(Get-ChildItem $IconDir -Filter *.png | Sort-Object Name)
if ($files.Count -eq 0) { throw "no icons in $IconDir" }

$big = 84          # inspect size
$small = 22        # roughly what the minimap draws
$cellW = 152
$cellH = 124
$cols = 6
$rows = [Math]::Ceiling($files.Count / [double]$cols)
$titleH = 54

$W = $cols * $cellW + 24
$H = $rows * $cellH + $titleH + 20

$bmp = New-Object System.Drawing.Bitmap $W, $H, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
# The minimap's own background, so the contrast on this sheet is the contrast in game.
$g.Clear([System.Drawing.Color]::FromArgb(255, 12, 16, 14))

$titleFont = New-Object System.Drawing.Font('Segoe UI', 15, [System.Drawing.FontStyle]::Bold)
$subFont   = New-Object System.Drawing.Font('Segoe UI', 9)
$nameFont  = New-Object System.Drawing.Font('Segoe UI', 9)
$cream = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 220, 210, 175))
$grey  = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 130, 140, 132))

$g.DrawString("Field Notes - icon set ($($files.Count))", $titleFont, $cream, 14, 10)
$g.DrawString("large = shape check    small = actual minimap size", $subFont, $grey, 16, 34)

$i = 0
foreach ($f in $files) {
    $c = $i % $cols
    # [int] in PowerShell ROUNDS - and rounds half to even - so [int](4/6) is 1, not 0. That put
    # entries in the wrong row, skipping some cells and stacking two labels on top of each other in
    # others. Floor is what integer division was supposed to mean here.
    $r = [Math]::Floor($i / $cols)
    $x = 12 + $c * $cellW
    $y = $titleH + $r * $cellH

    $img = [System.Drawing.Image]::FromFile($f.FullName)
    $g.DrawImage($img, $x + 6, $y + 4, $big, $big)
    # The same icon at map size, above the baseline. The first sheet put it level with the text
    # and three names came out unreadable; the second still bled 2px into the next cell and doubled
    # two labels on top of each other. The cell is wide enough for both now.
    $g.DrawImage($img, ($x + $big + 14), ($y + 10), $small, $small)
    $img.Dispose()

    $g.DrawString($f.BaseName, $nameFont, $cream, $x + 6, $y + $big + 8)
    $i++
}

$g.Dispose()
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

Write-Host "contact sheet -> $Out" -ForegroundColor Green
Write-Host "$($files.Count) icons" -ForegroundColor Yellow
