# Drawing vocabulary shared by draw-icons.ps1.
#
# Every shape is white on transparent. Punch punches transparency back out, which is how a banded
# armadillo shell or a shaded coconut gets its detail without a second colour - at 20 pixels on a
# dark map, a notch reads and a grey does not.
#
# Coordinates are all in a 256x256 box. Convert-Icon then auto-crops to whatever was actually drawn
# and centres it, so nothing here has to be laid out precisely - only shaped correctly.

function P { param([double]$x, [double]$y) return (New-Object System.Drawing.PointF ([single]$x), ([single]$y)) }

function Ellipse {
    param($g, $w, [double]$cx, [double]$cy, [double]$width, [double]$height)
    $g.FillEllipse($w, [single]($cx - $width/2), [single]($cy - $height/2), [single]$width, [single]$height)
}

function Rect {
    param($g, $w, [double]$x, [double]$y, [double]$width, [double]$height)
    $g.FillRectangle($w, [single]$x, [single]$y, [single]$width, [single]$height)
}

function Line {
    param($g, $w, [double]$x1, [double]$y1, [double]$x2, [double]$y2, [double]$width = 12)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([single]$width)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($pen, [single]$x1, [single]$y1, [single]$x2, [single]$y2)
    $pen.Dispose()
}

function Arc {
    param($g, $w, [double]$x, [double]$y, [double]$width, [double]$height,
          [double]$start, [double]$sweep, [double]$thickness = 14)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([single]$thickness)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($pen, [single]$x, [single]$y, [single]$width, [single]$height, [single]$start, [single]$sweep)
    $pen.Dispose()
}

function Pie {
    param($g, $w, [double]$x, [double]$y, [double]$width, [double]$height,
          [double]$start, [double]$sweep)
    $g.FillPie($w, [single]$x, [single]$y, [single]$width, [single]$height, [single]$start, [single]$sweep)
}

function Poly {
    param($g, $w, $points)
    $arr = New-Object 'System.Drawing.PointF[]' $points.Count
    for ($i = 0; $i -lt $points.Count; $i++) { $arr[$i] = $points[$i] }
    $g.FillPolygon($w, $arr)
}

# Punch a hole. CompositingMode SourceCopy with a fully transparent brush writes zero alpha rather
# than blending toward it - blending would leave a grey ghost instead of a clean notch.
function Punch {
    param($g, $points)
    $old = $g.CompositingMode
    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $clear = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0, 0, 0, 0))
    $arr = New-Object 'System.Drawing.PointF[]' $points.Count
    for ($i = 0; $i -lt $points.Count; $i++) { $arr[$i] = $points[$i] }
    $g.FillPolygon($clear, $arr)
    $clear.Dispose()
    $g.CompositingMode = $old
}

# Convert-Icon lives in mkicons.ps1; this is a trimmed copy so draw-icons.ps1 stands alone rather
# than depending on the order the two scripts are run in.
function Convert-Drawn {
    param([System.Drawing.Bitmap]$Bmp, [string]$OutPath, [int]$Size)

    $rect = New-Object System.Drawing.Rectangle 0, 0, $Bmp.Width, $Bmp.Height
    $data = $Bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                          [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bytes = New-Object byte[] ($data.Stride * $Bmp.Height)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $Bmp.UnlockBits($data)

    $minX = $Bmp.Width; $minY = $Bmp.Height; $maxX = -1; $maxY = -1
    for ($y = 0; $y -lt $Bmp.Height; $y++) {
        $row = $y * $data.Stride
        for ($x = 0; $x -lt $Bmp.Width; $x++) {
            if ($bytes[$row + $x*4 + 3] -gt 12) {
                if ($x -lt $minX) { $minX = $x }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    if ($maxX -lt 0) { return $false }

    $cw = $maxX - $minX + 1; $ch = $maxY - $minY + 1
    $out = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g2 = [System.Drawing.Graphics]::FromImage($out)
    $g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g2.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g2.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

    $pad = [int]($Size * 0.06); $box = $Size - 2*$pad
    $scale = [Math]::Min($box / [double]$cw, $box / [double]$ch)
    $dw = [int]([Math]::Round($cw * $scale)); $dh = [int]([Math]::Round($ch * $scale))
    $g2.DrawImage($Bmp, (New-Object System.Drawing.Rectangle ([int](($Size-$dw)/2)), ([int](($Size-$dh)/2)), $dw, $dh),
                  (New-Object System.Drawing.Rectangle $minX, $minY, $cw, $ch),
                  [System.Drawing.GraphicsUnit]::Pixel)
    $g2.Dispose()
    $out.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $out.Dispose()
    return $true
}

function New-DrawnIcon {
    param([string]$OutName, [scriptblock]$Body, [string]$OutDir, [int]$Size = 64)

    $bmp = New-Object System.Drawing.Bitmap 256, 256, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::FromArgb(0, 0, 0, 0))
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)

    & $Body $g $white

    $white.Dispose(); $g.Dispose()

    $dest = Join-Path $OutDir $OutName
    if (Convert-Drawn -Bmp $bmp -OutPath $dest -Size $Size) {
        Write-Host ("  {0,-16} ok" -f $OutName) -ForegroundColor Green
    } else {
        Write-Host ("  {0,-16} NOTHING DRAWN" -f $OutName) -ForegroundColor Red
    }
    $bmp.Dispose()
}
