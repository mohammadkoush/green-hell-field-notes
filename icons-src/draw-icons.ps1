# Draw the icon set.
#
# WHY DRAWN AND NOT SOURCED FROM THE WEB, which is what was asked for.
# The web route was tried properly and it does not hold up on this machine:
#
#   svgrepo.com      429 (rate limited to curl)
#   freesvg.org      404 on direct asset URLs
#   svgsilh.com      403
#   openclipart.org  200 - it works, and it serves SVG
#
# and there is no SVG renderer installed at all: no inkscape, no rsvg-convert, no ImageMagick.
# (`convert.exe` on PATH is the Windows filesystem tool, which is a good trap.) So the one source
# that answers gives a format nothing here can turn into a PNG.
#
# Drawing instead gets three things the web route could not:
#   - exactly the all-white silhouette style he chose, by construction
#   - CC0 by construction, so a published mod needs no attribution file and no licence audit
#   - one consistent weight and size across the whole set
#
# And nothing is locked in: icons load as loose PNGs matched on filename, so dropping a better
# capybara.png into the icons folder overrides this with no code change and no rebuild.
#
# Style rules, applied to everything here:
#   white, on transparent, antialiased, drawn at 256 and downsampled
#   a silhouette, not a line drawing - it must read at 20 pixels on a dark minimap
#   auto-cropped and centred by Convert-Icon so a bat and a mouse carry the same visual weight

param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\icons'),
    [int]$Size = 64
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot 'iconlib.ps1')

$OutDir = [System.IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force $OutDir | Out-Null

# ---------------------------------------------------------------------------------------------
# Quadrupeds share a skeleton and differ by proportion, which is how you tell a capybara from a
# tapir at icon size: not detail, but the ratio of body to leg to snout.
# ---------------------------------------------------------------------------------------------
function Draw-Quad {
    param($g, $w,
          [double]$bodyW = 130, [double]$bodyH = 70,   # body ellipse
          [double]$legLen = 55,  [double]$legW = 15,   # legs
          [double]$headR = 34,                          # head
          [double]$snout = 0,                           # snout length, 0 = none
          [double]$tail = 0,                            # tail length, 0 = none
          [double]$rump = 0,                            # extra height at the hip
          [bool]$ears = $false)

    $cx = 128.0; $cy = 128.0
    $bx = $cx - 6

    # Rump first so the body ellipse covers the seam.
    if ($rump -gt 0) { Ellipse $g $w ($bx - $bodyW*0.32) ($cy - $rump*0.5) ($bodyH*0.95) ($bodyH + $rump) }
    Ellipse $g $w $bx $cy $bodyW $bodyH

    $legTop = $cy + $bodyH*0.25
    $legBot = $legTop + $legLen
    Line $g $w ($bx - $bodyW*0.30) $legTop ($bx - $bodyW*0.34) $legBot $legW
    Line $g $w ($bx - $bodyW*0.16) $legTop ($bx - $bodyW*0.12) $legBot $legW
    Line $g $w ($bx + $bodyW*0.22) $legTop ($bx + $bodyW*0.26) $legBot $legW
    Line $g $w ($bx + $bodyW*0.34) $legTop ($bx + $bodyW*0.30) $legBot $legW

    $hx = $bx + $bodyW*0.48; $hy = $cy - $bodyH*0.32
    Ellipse $g $w $hx $hy ($headR*2) ($headR*2)

    if ($snout -gt 0) {
        $pts = @(
            (P ($hx + $headR*0.5) ($hy - $headR*0.35)),
            (P ($hx + $headR*0.5 + $snout) ($hy + $headR*0.15)),
            (P ($hx + $headR*0.5 + $snout*0.85) ($hy + $headR*0.62)),
            (P ($hx + $headR*0.35) ($hy + $headR*0.7))
        )
        Poly $g $w $pts
    }
    if ($ears) {
        Ellipse $g $w ($hx - $headR*0.35) ($hy - $headR*0.95) ($headR*0.85) ($headR*0.95)
        Ellipse $g $w ($hx + $headR*0.45) ($hy - $headR*0.9)  ($headR*0.85) ($headR*0.95)
    }
    if ($tail -gt 0) {
        Line $g $w ($bx - $bodyW*0.48) ($cy - $bodyH*0.1) ($bx - $bodyW*0.48 - $tail) ($cy - $bodyH*0.5) 9
    }
}

# ---------------------------------------------------------------------------------------------
$shapes = @{}

$shapes['tapir'] = {
    param($g,$w) Draw-Quad $g $w -bodyW 140 -bodyH 78 -legLen 52 -legW 17 -headR 30 -snout 48 -rump 10
}
$shapes['capybara'] = {
    # Blunt, blocky, almost no neck and no tail at all - that absence is the tell.
    param($g,$w) Draw-Quad $g $w -bodyW 138 -bodyH 82 -legLen 38 -legW 16 -headR 34 -snout 16
}
$shapes['peccary'] = {
    param($g,$w)
    Draw-Quad $g $w -bodyW 132 -bodyH 74 -legLen 50 -legW 14 -headR 30 -snout 34 -rump 6
    # Bristles along the spine.
    for ($i = 0; $i -lt 6; $i++) {
        $x = 70 + $i*17
        Poly $g $w @((P $x 92), (P ($x+7) 66), (P ($x+13) 92))
    }
}
$shapes['agouti'] = {
    # Small, high at the hip, nose down - the shape of a thing that is always about to run.
    param($g,$w) Draw-Quad $g $w -bodyW 104 -bodyH 58 -legLen 46 -legW 11 -headR 25 -snout 20 -rump 26
}
$shapes['armadillo'] = {
    param($g,$w)
    # A banded dome, drawn as one filled shell with slots cut back out of it, plus a pointed head
    # and a tapered tail. The bands are the whole identity.
    Pie $g $w 40 84 176 120 180 180
    Rect $g $w 40 140 176 8
    Line $g $w 60 148 56 176 13
    Line $g $w 100 148 98 176 13
    Line $g $w 156 148 158 176 13
    Poly $g $w @((P 208 118), (P 246 138), (P 208 152))
    Line $g $w 44 128 14 154 10
    # Slots between the bands.
    Punch $g @((P 96 84),(P 104 84),(P 104 144),(P 96 144))
    Punch $g @((P 136 86),(P 144 86),(P 144 144),(P 136 144))
    Punch $g @((P 168 98),(P 176 98),(P 176 144),(P 168 144))
}
$shapes['turtle'] = {
    param($g,$w)
    Pie $g $w 46 76 164 128 180 180
    Rect $g $w 46 138 164 10
    Ellipse $g $w 206 124 44 36
    Line $g $w 66 146 52 172 15
    Line $g $w 106 148 100 178 15
    Line $g $w 150 148 156 178 15
    Line $g $w 186 146 198 170 13
    Punch $g @((P 100 78),(P 110 78),(P 110 140),(P 100 140))
    Punch $g @((P 146 78),(P 156 78),(P 156 140),(P 146 140))
}
$shapes['mouse'] = {
    param($g,$w)
    Ellipse $g $w 92 132 104 74
    Ellipse $g $w 168 116 56 54
    Ellipse $g $w 150 76 44 44
    Ellipse $g $w 192 74 44 44
    Poly $g $w @((P 196 126), (P 236 138), (P 196 146))
    # A long thin tail, curled - the other half of "mouse".
    Arc $g $w 20 118 70 70 210 200 8
}
$shapes['parrot'] = {
    param($g,$w)
    Ellipse $g $w 96 96 82 96
    Ellipse $g $w 118 62 62 60
    Poly $g $w @((P 146 62), (P 184 78), (P 146 96))        # hooked beak
    Poly $g $w @((P 92 150), (P 128 156), (P 78 240), (P 56 224))   # long tail
    Line $g $w 108 178 104 208 12
}
$shapes['toucan'] = {
    # The beak IS the bird. Nothing else needs to be right.
    param($g,$w)
    Ellipse $g $w 82 118 88 100
    Ellipse $g $w 118 70 60 58
    Poly $g $w @((P 142 52), (P 244 86), (P 140 108))
    Poly $g $w @((P 74 170), (P 116 176), (P 84 244), (P 60 232))
    Line $g $w 96 186 92 214 12
}
$shapes['frog'] = {
    # From above, splayed. A frog seen from the side is a lump.
    param($g,$w)
    Ellipse $g $w 128 132 110 116
    Ellipse $g $w 128 74 84 58
    Ellipse $g $w 102 58 30 30
    Ellipse $g $w 154 58 30 30
    Line $g $w 84 108 34 74 15;  Line $g $w 34 74 22 116 13
    Line $g $w 172 108 222 74 15; Line $g $w 222 74 234 116 13
    Line $g $w 92 172 44 210 16;  Line $g $w 44 210 74 236 14
    Line $g $w 164 172 212 210 16; Line $g $w 212 210 182 236 14
}
$shapes['lizard'] = {
    param($g,$w)
    Ellipse $g $w 118 128 116 62
    Ellipse $g $w 190 128 54 46
    Poly $g $w @((P 60 116), (P 60 140), (P 12 132))
    Line $g $w 96 104 62 62 12;  Line $g $w 152 104 190 62 12
    Line $g $w 96 152 62 194 12; Line $g $w 152 152 190 194 12
}
$shapes['crab'] = {
    param($g,$w)
    Ellipse $g $w 128 138 128 88
    for ($i = 0; $i -lt 3; $i++) {
        $y = 128 + $i*26
        Line $g $w 70 $y (30 - $i*4) ($y + 26) 11
        Line $g $w 186 $y (226 + $i*4) ($y + 26) 11
    }
    Line $g $w 74 108 34 68 13;  Poly $g $w @((P 40 78), (P 8 46), (P 34 40), (P 52 62))
    Line $g $w 182 108 222 68 13; Poly $g $w @((P 216 78), (P 248 46), (P 222 40), (P 204 62))
}
$shapes['anteater'] = {
    # Long tapering snout and an enormous tail. Two silhouette facts, both unmistakable.
    param($g,$w)
    Draw-Quad $g $w -bodyW 124 -bodyH 66 -legLen 48 -legW 14 -headR 24 -snout 66
    Poly $g $w @((P 66 106), (P 6 42), (P 4 118), (P 62 148))
}
$shapes['bat'] = {
    param($g,$w)
    Ellipse $g $w 128 128 48 76
    Ellipse $g $w 128 84 40 38
    Poly $g $w @((P 112 66), (P 104 34), (P 126 58))
    Poly $g $w @((P 144 66), (P 152 34), (P 130 58))
    Poly $g $w @((P 108 106), (P 12 72), (P 34 138), (P 6 150), (P 106 166))
    Poly $g $w @((P 148 106), (P 244 72), (P 222 138), (P 250 150), (P 150 166))
}
$shapes['fish'] = {
    # Arowana, peacock bass, angelfish, discus - four species that are all "a fish" at 20 pixels.
    param($g,$w)
    Ellipse $g $w 118 128 168 92
    Poly $g $w @((P 40 128), (P 4 88), (P 14 128), (P 4 168))
    Poly $g $w @((P 130 84), (P 152 40), (P 168 86))
    Ellipse $g $w 176 112 20 20
    Punch $g @((P 172 108),(P 182 108),(P 182 118),(P 172 118))
}
$shapes['monkey'] = {
    param($g,$w)
    Ellipse $g $w 120 96 80 84
    Ellipse $g $w 76 78 38 40
    Ellipse $g $w 164 78 38 40
    Ellipse $g $w 118 170 92 92
    Line $g $w 86 148 44 196 15
    Line $g $w 152 148 194 196 15
    Arc $g $w 150 176 96 88 300 210 12
}
$shapes['bug'] = {
    # Caterpillar, beetle, prawn - small many-legged things, from above.
    param($g,$w)
    Ellipse $g $w 128 140 104 148
    Ellipse $g $w 128 56 66 60
    Line $g $w 110 34 84 6 8
    Line $g $w 146 34 172 6 8
    for ($i = 0; $i -lt 3; $i++) {
        $y = 108 + $i*40
        Line $g $w 82 $y 30 ($y - 16) 10
        Line $g $w 174 $y 226 ($y - 16) 10
    }
    Punch $g @((P 76 118),(P 180 118),(P 180 126),(P 76 126))
    Punch $g @((P 76 158),(P 180 158),(P 180 166),(P 76 166))
}
$shapes['snake'] = {
    # Redrawn as a silhouette. The supplied illustration was full colour and detailed - keyed to a
    # blob at 20 pixels, and out of place now the set is all white.
    param($g,$w)
    Arc $g $w 44 96 168 130 20 300 26
    Arc $g $w 84 120 92 84 40 260 22
    Ellipse $g $w 196 74 54 44
    Poly $g $w @((P 214 74), (P 252 66), (P 236 78), (P 252 88))
}

# ---- weapons, for the human variants -----------------------------------------------------
$shapes['bow'] = {
    param($g,$w)
    Arc $g $w 74 26 118 204 290 140 18
    Line $g $w 92 44 92 212 6
    Line $g $w 60 128 216 128 11
    Poly $g $w @((P 210 112), (P 246 128), (P 210 144))
    Poly $g $w @((P 74 116), (P 52 128), (P 74 140))
}
$shapes['spear'] = {
    param($g,$w)
    Line $g $w 74 232 176 84 14
    Poly $g $w @((P 186 66), (P 214 22), (P 224 74), (P 196 96))
    Line $g $w 160 100 200 118 9
}
$shapes['axe'] = {
    param($g,$w)
    Line $g $w 96 236 148 52 16
    Poly $g $w @((P 140 66), (P 226 40), (P 236 110), (P 148 118))
    Poly $g $w @((P 140 66), (P 58 40), (P 48 110), (P 138 118))
}
$shapes['caveman'] = {
    # First attempt still read as a stick man with an arm up, which is the exact thing he rejected.
    # The fix is not thicker lines - it is that a stick figure is drawn as JOINTS AND STROKES while a
    # silhouette is drawn as MASS. So: one heavy body polygon, a hunched neck, legs that are wedges
    # rather than lines, and a club held low. Squat and wide, because "caveman" is a posture.
    param($g,$w)
    Ellipse $g $w 104 52 58 58
    # Torso: wide at the shoulders, tapering, with the head sunk into it.
    Poly $g $w @((P 60 96), (P 150 92), (P 162 150), (P 148 176), (P 68 176), (P 54 146))
    # Legs as wedges, planted apart.
    Poly $g $w @((P 70 170), (P 104 170), (P 96 240), (P 60 240))
    Poly $g $w @((P 112 170), (P 148 170), (P 152 240), (P 116 240))
    # Near arm across the body, far arm holding the club.
    Poly $g $w @((P 62 104), (P 40 148), (P 62 158), (P 82 116))
    Poly $g $w @((P 148 104), (P 196 132), (P 186 154), (P 142 132))
    # The club: a thick tapered stick, held low rather than raised.
    Poly $g $w @((P 178 128), (P 246 92), (P 254 122), (P 188 152))
}

# ---- plants, redrawn white ---------------------------------------------------------------
$shapes['coconut'] = {
    param($g,$w)
    Ellipse $g $w 128 148 152 152
    Line $g $w 128 74 150 26 13
    Poly $g $w @((P 150 30), (P 210 14), (P 176 48))
    Punch $g @((P 104 118),(P 122 108),(P 130 126),(P 112 136))
    Punch $g @((P 146 116),(P 164 106),(P 172 124),(P 154 134))
    Punch $g @((P 124 156),(P 142 146),(P 150 164),(P 132 174))
}
$shapes['banana'] = {
    param($g,$w)
    for ($i = -1; $i -le 1; $i++) {
        Arc $g $w (70 + $i*26) (60 + [Math]::Abs($i)*14) 120 150 200 130 22
    }
    Line $g $w 128 44 128 16 12
}
$shapes['papaya'] = {
    param($g,$w)
    Ellipse $g $w 128 148 124 172
    Line $g $w 128 62 128 22 11
    Poly $g $w @((P 128 30), (P 190 12), (P 150 46))
}
$shapes['cassava'] = {
    param($g,$w)
    for ($i = -1; $i -le 1; $i++) {
        Ellipse $g $w (128 + $i*46) (176 - [Math]::Abs($i)*10) 54 128
    }
    Line $g $w 128 96 128 44 12
    Poly $g $w @((P 128 52), (P 74 16), (P 122 24))
    Poly $g $w @((P 128 52), (P 182 16), (P 134 24))
}
$shapes['palmheart'] = {
    param($g,$w)
    Line $g $w 128 238 128 120 14
    for ($i = 0; $i -lt 5; $i++) {
        $a = -80 + $i*40
        $r = [Math]::PI * $a / 180.0
        $ex = 128 + [Math]::Sin($r) * 104
        $ey = 116 - [Math]::Cos($r) * 104
        Line $g $w 128 122 $ex $ey 16
    }
}
$shapes['molineria'] = {
    param($g,$w)
    Ellipse $g $w 96 150 64 64
    Ellipse $g $w 156 138 64 64
    Ellipse $g $w 126 200 64 64
    Line $g $w 120 112 148 48 11
    Poly $g $w @((P 146 54), (P 210 26), (P 168 74))
}
$shapes['mushroom'] = {
    param($g,$w)
    Pie $g $w 30 52 196 152 180 180
    Rect $g $w 30 122 196 12
    Poly $g $w @((P 104 134), (P 152 134), (P 144 232), (P 112 232))
}
$shapes['plant'] = {
    param($g,$w)
    Line $g $w 128 240 128 70 13
    for ($i = 0; $i -lt 3; $i++) {
        $y = 96 + $i*46
        Ellipse $g $w 82 $y 84 40
        Ellipse $g $w 174 ($y + 20) 84 40
    }
    Ellipse $g $w 128 58 56 66
}

# ---------------------------------------------------------------------------------------------
Write-Host "Drawing $($shapes.Count) icons" -ForegroundColor Cyan
foreach ($name in ($shapes.Keys | Sort-Object)) {
    New-DrawnIcon -OutName ($name + '.png') -Body $shapes[$name] -OutDir $OutDir -Size $Size
}
Write-Host ""
Write-Host "Icons written to $OutDir" -ForegroundColor Yellow
