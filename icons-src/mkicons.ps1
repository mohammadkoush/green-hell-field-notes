# Turn the supplied artwork into map icons.
#
# Everything here is deterministic and re-runnable: point it at the source images, get a folder of
# 64x64 PNGs with clean alpha. Re-run it after swapping a source and the mod picks the new icon up on
# the next launch - no rebuild, because icons ship as loose PNGs rather than baked into the DLL.
#
# THREE WAYS TO CUT A BACKGROUND OUT, because the sources are three different kinds of picture:
#
#   silhouette  black ink on white (spider, scorpion, panther). Alpha comes from how DARK the pixel
#               is and the colour is forced to white - which both removes the background and does the
#               "reverse the colours" he asked for in one pass. Antialiasing survives, because a grey
#               edge pixel becomes a half-transparent white one instead of being thresholded away.
#   keepcolor   coloured art on white (the plant sheet, the coconuts). Alpha comes from how far the
#               pixel is from white; the colour is kept.
#   chroma      coloured art on a coloured background (the snake on beige). Background colour is
#               SAMPLED from a corner rather than guessed, and anything within tolerance goes.
#
# Then every icon is auto-cropped to its own content and centred in a square, so a wide panther and a
# tall scorpion end up the same visual weight on the map instead of one being twice the other.

param(
    [string]$SrcDir = 'C:\Users\moham\Downloads\Claude\project-tree\data\images',
    [string]$OutDir = (Join-Path $PSScriptRoot '..\icons'),
    [int]$Size = 64
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$OutDir = [System.IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force $OutDir | Out-Null

function Get-Pixels {
    param([System.Drawing.Bitmap]$Bmp)
    $rect = New-Object System.Drawing.Rectangle 0, 0, $Bmp.Width, $Bmp.Height
    $data = $Bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                          [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bytes = New-Object byte[] ($data.Stride * $Bmp.Height)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $Bmp.UnlockBits($data)
    return @{ Bytes = $bytes; Stride = $data.Stride; W = $Bmp.Width; H = $Bmp.Height }
}

function Set-Pixels {
    param($P)
    $bmp = New-Object System.Drawing.Bitmap $P.W, $P.H, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $rect = New-Object System.Drawing.Rectangle 0, 0, $P.W, $P.H
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly,
                          [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    [System.Runtime.InteropServices.Marshal]::Copy($P.Bytes, 0, $data.Scan0, $P.Bytes.Length)
    $bmp.UnlockBits($data)
    return $bmp
}

function Convert-Icon {
    param(
        [string]$Path,
        [int]$X, [int]$Y, [int]$W, [int]$H,   # crop out of the source
        [string]$Mode,                        # silhouette | keepcolor | chroma
        [int]$Tolerance = 60,
        [string]$OutName
    )

    $src = [System.Drawing.Bitmap]::FromFile($Path)

    # Crop first, so background sampling and auto-crop only ever see the piece we care about.
    if ($W -le 0) { $W = $src.Width - $X }
    if ($H -le 0) { $H = $src.Height - $Y }
    $crop = New-Object System.Drawing.Bitmap $W, $H, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($crop)
    $g.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, $W, $H),
                 (New-Object System.Drawing.Rectangle $X, $Y, $W, $H),
                 [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose(); $src.Dispose()

    $p = Get-Pixels $crop
    $crop.Dispose()
    $b = $p.Bytes; $stride = $p.Stride

    # For chroma, take the background from the top-left pixel - it is background by construction
    # after the crop, and sampling beats guessing at a beige.
    $bgB = $b[0]; $bgG = $b[1]; $bgR = $b[2]

    for ($y = 0; $y -lt $p.H; $y++) {
        $row = $y * $stride
        for ($x = 0; $x -lt $p.W; $x++) {
            $i = $row + $x * 4
            $bb = $b[$i]; $gg = $b[$i + 1]; $rr = $b[$i + 2]

            if ($Mode -eq 'silhouette') {
                # 0 = black ink (fully opaque), 255 = paper (gone). Colour forced to white.
                $lum = [int](0.299 * $rr + 0.587 * $gg + 0.114 * $bb)
                $a = 255 - $lum
                if ($a -lt 24) { $a = 0 }
                $b[$i] = 255; $b[$i + 1] = 255; $b[$i + 2] = 255; $b[$i + 3] = [byte]$a
            }
            elseif ($Mode -eq 'keepcolor') {
                # Distance from white becomes opacity, so pale line art keeps its soft edges.
                $minc = [Math]::Min($rr, [Math]::Min($gg, $bb))
                $a = 255 - $minc
                if ($a -lt 20) { $a = 0 } else { $a = [Math]::Min(255, [int]($a * 2.2)) }
                $b[$i + 3] = [byte]$a
            }
            else {
                $d = [Math]::Abs($rr - $bgR) + [Math]::Abs($gg - $bgG) + [Math]::Abs($bb - $bgB)
                if ($d -lt $Tolerance) { $b[$i + 3] = 0 }
            }
        }
    }

    # Auto-crop to whatever actually survived.
    $minX = $p.W; $minY = $p.H; $maxX = -1; $maxY = -1
    for ($y = 0; $y -lt $p.H; $y++) {
        $row = $y * $stride
        for ($x = 0; $x -lt $p.W; $x++) {
            if ($b[$row + $x * 4 + 3] -gt 12) {
                if ($x -lt $minX) { $minX = $x }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    if ($maxX -lt 0) { Write-Host "  $OutName : nothing survived the cut" -ForegroundColor Red; return }

    $keyed = Set-Pixels $p
    $cw = $maxX - $minX + 1; $ch = $maxY - $minY + 1

    # Centre inside a square and scale to fit, so every icon carries the same visual weight.
    $out = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g2 = [System.Drawing.Graphics]::FromImage($out)
    $g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g2.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g2.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

    $pad = [int]($Size * 0.06)
    $box = $Size - 2 * $pad
    $scale = [Math]::Min($box / [double]$cw, $box / [double]$ch)
    $dw = [int]([Math]::Round($cw * $scale)); $dh = [int]([Math]::Round($ch * $scale))
    $dx = [int](($Size - $dw) / 2); $dy = [int](($Size - $dh) / 2)

    $g2.DrawImage($keyed, (New-Object System.Drawing.Rectangle $dx, $dy, $dw, $dh),
                  (New-Object System.Drawing.Rectangle $minX, $minY, $cw, $ch),
                  [System.Drawing.GraphicsUnit]::Pixel)
    $g2.Dispose(); $keyed.Dispose()

    $dest = Join-Path $OutDir $OutName
    $out.Save($dest, [System.Drawing.Imaging.ImageFormat]::Png)
    $out.Dispose()
    Write-Host ("  {0,-22} {1}x{2} content -> {3}px" -f $OutName, $cw, $ch, $Size) -ForegroundColor Green
}

# ---------------------------------------------------------------------------------------------
$plants   = Join-Path $SrcDir 'shot-20260813-205112.png'   # 1920x1152, 8 cols x 5 rows
$snake    = Join-Path $SrcDir 'shot-20260813-205454.png'   # 311x234
$spider   = Join-Path $SrcDir 'shot-20260813-205600.png'   # 260x280, watermark along the bottom
$scorpion = Join-Path $SrcDir 'shot-20260813-205721.png'   # 740x740
$cats     = Join-Path $SrcDir 'shot-20260813-205909.png'   # 627x350, three panthers
$coconut  = Join-Path $SrcDir 'shot-20260813-210242.png'   # 500x500, four coconuts 2x2

$cw = 240; $ch = 230   # one cell of the plant sheet
function Cell { param([int]$col, [int]$row) return @{ X = $col * $cw; Y = [int]($row * 230.4); W = $cw; H = $ch } }

Write-Host "Creatures" -ForegroundColor Cyan
# Beige background with a pattern printed on it, so this one is chroma-keyed off a sampled corner.
Convert-Icon -Path $snake -X 0 -Y 0 -W 0 -H 0 -Mode chroma -Tolerance 95 -OutName 'snake.png'
# The watermark lives in the bottom strip; cut it off before anything else looks at the picture.
Convert-Icon -Path $spider -X 0 -Y 10 -W 260 -H 215 -Mode silhouette -OutName 'spider.png'
Convert-Icon -Path $scorpion -X 0 -Y 0 -W 0 -H 0 -Mode silhouette -OutName 'scorpion.png'
# Far right of the three, as asked: the prowling full body reads as an animal at icon size, where
# the two head crests would just be a dark blob.
Convert-Icon -Path $cats -X 395 -Y 80 -W 232 -H 210 -Mode silhouette -OutName 'predator.png'

Write-Host "Plants" -ForegroundColor Cyan
# Top left of the four, whole and stemmed.
Convert-Icon -Path $coconut -X 0 -Y 0 -W 250 -H 250 -Mode keepcolor -OutName 'coconut.png'

$picks = @(
    @{ c = 2; r = 1; n = 'banana.png'    },   # broad tree
    @{ c = 4; r = 1; n = 'molineria.png' },   # berries on a stem
    @{ c = 1; r = 3; n = 'papaya.png'    },   # gourd on the ground
    @{ c = 2; r = 3; n = 'cassava.png'   },   # leaves with a root system
    @{ c = 4; r = 4; n = 'palmheart.png' },   # grass tuft
    @{ c = 4; r = 2; n = 'mushroom.png'  },   # squat cactus reads as a mushroom at 64px
    @{ c = 0; r = 1; n = 'plant.png'     }    # leafy stem - the fallback for anything unmapped
)
# birdnest and camp used to be cut from this sheet - a wheat sheaf and a sunflower. Both are drawn
# properly further down instead; neither ever looked like what it claimed to be.
foreach ($p in $picks) {
    $cell = Cell $p.c $p.r
    Convert-Icon -Path $plants -X $cell.X -Y $cell.Y -W $cell.W -H $cell.H -Mode keepcolor -OutName $p.n
}

Write-Host "People" -ForegroundColor Cyan

# DRAWN, not cut, because no human artwork was supplied and the stand-in that was used instead - the
# panther - made savages show up as wildcats. A wrong icon is worse than no icon, because it reads as
# a correct one. These are deliberately plain silhouettes: a figure with a spear, and a smaller
# figure without. At 64px on a map, unmistakably-a-person is the whole job.
#
# Both are placeholders in the sense that dropping a better savage.png or kid.png into the icons
# folder overrides them with no code change - the icon lookup matches on filename.
function New-Figure {
    param([string]$OutName, [bool]$WithSpear, [double]$HeadScale, [double]$Height)

    $S = 256
    $bmp = New-Object System.Drawing.Bitmap $S, $S, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::FromArgb(0, 0, 0, 0))

    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 15
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    # Everything hangs off these three numbers so the child is the same drawing with a bigger head
    # and shorter legs, which is how you read "child" at a glance.
    $top = 30.0
    $headR = 26.0 * $HeadScale
    $cx = 104.0

    $headY = $top + $headR
    $shoulder = $headY + $headR + 14
    $hip = $shoulder + 66 * $Height
    $foot = $hip + 76 * $Height

    $g.FillEllipse($white, [single]($cx - $headR), [single]($headY - $headR),
                   [single]($headR * 2), [single]($headR * 2))

    # Torso, arms, legs - drawn as thick round-capped strokes so the figure holds together at 64px
    # instead of breaking into thin lines.
    $g.DrawLine($pen, [single]$cx, [single]$shoulder, [single]$cx, [single]$hip)
    $g.DrawLine($pen, [single]$cx, [single]($shoulder + 8), [single]($cx - 44), [single]($shoulder + 62))
    $g.DrawLine($pen, [single]$cx, [single]($shoulder + 8), [single]($cx + 40), [single]($shoulder + 44))
    $g.DrawLine($pen, [single]$cx, [single]$hip, [single]($cx - 34), [single]$foot)
    $g.DrawLine($pen, [single]$cx, [single]$hip, [single]($cx + 34), [single]$foot)

    if ($WithSpear) {
        $spearPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 9
        $spearPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $spearPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $sx = $cx + 62
        $g.DrawLine($spearPen, [single]$sx, [single]($top - 4), [single]($sx - 16), [single]($foot + 14))
        # A head on the spear, so it is a weapon and not a walking stick.
        $tip = New-Object 'System.Drawing.PointF[]' 3
        $tip[0] = New-Object System.Drawing.PointF ([single]$sx), ([single]($top - 26))
        $tip[1] = New-Object System.Drawing.PointF ([single]($sx - 16)), ([single]($top + 16))
        $tip[2] = New-Object System.Drawing.PointF ([single]($sx + 16)), ([single]($top + 12))
        $g.FillPolygon($white, $tip)
        $spearPen.Dispose()
    }

    $pen.Dispose(); $white.Dispose(); $g.Dispose()

    # Down to icon size through the same auto-crop and centring as everything else, so a person
    # carries the same visual weight as a jaguar.
    $tmp = Join-Path $env:TEMP ("fieldnotes-" + $OutName)
    $bmp.Save($tmp, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Convert-Icon -Path $tmp -X 0 -Y 0 -W 0 -H 0 -Mode chroma -Tolerance 8 -OutName $OutName
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
}

New-Figure -OutName 'savage.png' -WithSpear $true  -HeadScale 1.0 -Height 1.0
New-Figure -OutName 'kid.png'    -WithSpear $false -HeadScale 1.3 -Height 0.72

Write-Host "Drawn shapes" -ForegroundColor Cyan

# Three more with no source art, drawn for the same reason as the people: a stand-in borrowed from
# another subject reads as correct and is therefore worse than nothing. The bird nest was a WHEAT
# SHEAF and the crafted-gear mark was a SUNFLOWER, which is how you end up with a map that quietly
# lies about what is on it.
function New-Shape {
    param([string]$OutName, [string]$Shape)

    $S = 256
    $bmp = New-Object System.Drawing.Bitmap $S, $S, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::FromArgb(0, 0, 0, 0))
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)

    if ($Shape -eq 'stingray') {
        # Seen from above: pointed nose, long swept wings, whip tail. A ray is one of the few animals
        # that is completely readable as a flat silhouette, so this needs no detail at all.
        # The first attempt domed the leading edge and came out looking like an umbrella. What makes
        # a ray a ray is the CONCAVE trailing edge - wings swept back past the body, not a smooth
        # skirt - so the two points either side of the tail sit higher than the wing tips.
        $pts = New-Object 'System.Drawing.PointF[]' 8
        $pts[0] = New-Object System.Drawing.PointF 128, 34
        $pts[1] = New-Object System.Drawing.PointF 192, 74
        $pts[2] = New-Object System.Drawing.PointF 250, 152
        $pts[3] = New-Object System.Drawing.PointF 166, 138
        $pts[4] = New-Object System.Drawing.PointF 128, 160
        $pts[5] = New-Object System.Drawing.PointF 90,  138
        $pts[6] = New-Object System.Drawing.PointF 6,   152
        $pts[7] = New-Object System.Drawing.PointF 64,  74
        $g.FillPolygon($white, $pts)

        $tail = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 11
        $tail.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $tail.EndCap = [System.Drawing.Drawing2D.LineCap]::Triangle
        $g.DrawLine($tail, [single]128, [single]160, [single]128, [single]244)
        $tail.Dispose()
    }
    elseif ($Shape -eq 'birdnest') {
        # A woven rim drawn as a thick ellipse outline, with eggs sitting inside it. The rim being an
        # OUTLINE rather than a fill is what makes it read as a nest and not a bowl.
        $rim = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 26
        $g.DrawEllipse($rim, 26, 116, 204, 104)
        $rim.Dispose()
        $g.FillEllipse($white, 86,  92, 48, 56)
        $g.FillEllipse($white, 130, 84, 48, 56)
        $g.FillEllipse($white, 108, 118, 48, 56)
    }
    else {
        # Crafted gear: crossed tools. Not a tent - he asked for CRAFTED items to be told apart from
        # what grows on the island, and a tent says "camp" while crossed tools say "things you made".
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 20
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $g.DrawLine($pen, [single]58, [single]208, [single]186, [single]66)
        $g.DrawLine($pen, [single]198, [single]208, [single]70, [single]66)
        $pen.Dispose()

        $head1 = New-Object 'System.Drawing.PointF[]' 3
        $head1[0] = New-Object System.Drawing.PointF 196, 34
        $head1[1] = New-Object System.Drawing.PointF 232, 84
        $head1[2] = New-Object System.Drawing.PointF 162, 78
        $g.FillPolygon($white, $head1)

        $head2 = New-Object 'System.Drawing.PointF[]' 3
        $head2[0] = New-Object System.Drawing.PointF 60,  34
        $head2[1] = New-Object System.Drawing.PointF 94,  78
        $head2[2] = New-Object System.Drawing.PointF 24,  84
        $g.FillPolygon($white, $head2)
    }

    $white.Dispose(); $g.Dispose()

    $tmp = Join-Path $env:TEMP ("fieldnotes-" + $OutName)
    $bmp.Save($tmp, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Convert-Icon -Path $tmp -X 0 -Y 0 -W 0 -H 0 -Mode chroma -Tolerance 8 -OutName $OutName
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
}

New-Shape -OutName 'stingray.png' -Shape 'stingray'
New-Shape -OutName 'birdnest.png' -Shape 'birdnest'
New-Shape -OutName 'camp.png'     -Shape 'crafted'

Write-Host ""
Write-Host "Icons written to $OutDir" -ForegroundColor Yellow
