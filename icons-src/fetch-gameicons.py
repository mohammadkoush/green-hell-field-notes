"""Fetch the object icons from the game-icons.net set hosted on Wikimedia Commons.

PhyloPic solved the animals and could never solve an axe, because an axe is not an organism. This is
the other half: game-icons.net is a large library of game-grade icons - weapons, tools, camps, ore -
drawn as flat silhouettes by a handful of artists, and a big chunk of it is mirrored on Commons.
Which means Commons renders the SVGs to PNG server-side, so the missing SVG renderer still does not
matter.

BY EXACT TITLE, NOT BY SEARCH. Commons full-text search is what returned a Baudelaire book cover for
"tapir silhouette" and photographs of a pub called The Bow and Arrow. Every icon here is named
explicitly, chosen by eye from a title listing first, so there is nothing to guess and nothing to
filter. A title that stops existing fails loudly rather than quietly fetching a photograph.

LICENCE. The game-icons set is CC BY 3.0, so every one of these is recorded in ATTRIBUTIONS.md with
its artist. That is the deal for using it and it costs one line per icon.

    python fetch-gameicons.py
    python fetch-gameicons.py --only axe
"""

import argparse
import io
import json
import os
import sys
import time
import urllib.parse
import urllib.request

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ICON_DIR = os.path.abspath(os.path.join(HERE, "..", "icons"))
SOURCED = os.path.join(HERE, "sourced")
ATTRIB = os.path.abspath(os.path.join(HERE, "..", "ATTRIBUTIONS.md"))

API = "https://commons.wikimedia.org/w/api.php"
UA = "FieldNotesIconBot/1.0 (Green Hell mod icons; mohammadkoush@gmail.com)"
SIZE = 64

# Chosen by eye from the Commons title listing. The pick matters as much as the source: a wood axe
# rather than a battle axe because this is a survival game, a stone spear rather than an ice spear,
# a forest camp rather than a goblin one.
WANTED = {
    "axe":       "File:Wood-axe - Lorc - game-icons.svg",
    "bow":       "File:Bow-arrow - Delapouite - game-icons.svg",
    "spear":     "File:Stone-spear - Lorc - game-icons.svg",
    "camp":      "File:Forest-camp - Delapouite - game-icons.svg",
    "iron":      "File:Ore - Faithtoken - game-icons.svg",
    "birdnest":  "File:Nest-eggs - Delapouite - game-icons.svg",
    "mushroom":  "File:Mushroom - Lorc - game-icons.svg",
    # An unarmed figure ON PURPOSE. The obvious "Caveman" icon carries a spear, and this icon exists
    # precisely to mean "a savage holding nothing" - it would have collided with the spearman.
    "caveman":   "File:Brute - Delapouite - game-icons.svg",
    "plant":     "File:Sprout - Lorc - game-icons.svg",
    # A palm TREE. "Palm - Lorc" is a hand, which is what the word means everywhere except here.
    "palmheart": "File:Palm-tree - Delapouite - game-icons.svg",
    "anteater":  "File:Anteater - Caro Asercion - game-icons.svg",
    # The fallback for "a species is classified but has no icon of its own". A capybara because it
    # is the right continent and reads as a generic mammal; the deer that was here before was neither.
    "animal":    "File:Capybara - Caro Asercion - game-icons.svg",
}

# Tried in order if the first title has moved or been renamed.
FALLBACKS = {
    "axe":       ["File:Battle-axe - Lorc - game-icons.svg", "File:Sharp-axe - Delapouite - game-icons.svg"],
    "bow":       ["File:Pocket-bow - Lorc - game-icons.svg"],
    "spear":     ["File:Barbed-spear - Lorc - game-icons.svg"],
    "camp":      ["File:Desert-camp - Delapouite - game-icons.svg"],
    "birdnest":  ["File:Nest-birds - Delapouite - game-icons.svg"],
    "caveman":   ["File:Barbarian - Delapouite - game-icons.svg"],
    "palmheart": ["File:Coconuts - Delapouite - game-icons.svg"],
    "plant":     ["File:Ground-sprout - Lorc - game-icons.svg", "File:Plant-roots - Delapouite - game-icons.svg"],
}


def get(url, binary=False, tries=3):
    for attempt in range(tries):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": UA})
            with urllib.request.urlopen(req, timeout=30) as r:
                data = r.read()
            return data if binary else json.loads(data.decode("utf-8"))
        except Exception:
            if attempt == tries - 1:
                return None
            time.sleep(1.2 * (attempt + 1))
    return None


def imageinfo(title):
    q = urllib.parse.urlencode({
        "action": "query", "format": "json", "prop": "imageinfo",
        "iiprop": "url|extmetadata", "iiurlwidth": "512", "titles": title,
    })
    d = get("%s?%s" % (API, q))
    if not d:
        return None
    for _, page in (d.get("query", {}).get("pages", {}) or {}).items():
        if "missing" in page:
            return None
        info = (page.get("imageinfo") or [None])[0]
        if info:
            return info
    return None


def flatten(html):
    for tag in ("<br>", "<br/>", "</a>", "</span>", "</div>"):
        html = html.replace(tag, " ")
    while "<" in html and ">" in html:
        a = html.index("<")
        b = html.index(">", a)
        html = html[:a] + html[b + 1:]
    return " ".join(html.split())[:100]


def to_icon(raw):
    """Commons renders these with transparency, so the alpha channel already IS the shape - keep it
    and force the colour white. The luminance path is only there for the odd file that arrives on a
    solid background."""
    try:
        im = Image.open(io.BytesIO(raw)).convert("RGBA")
    except Exception:
        return None

    w, h = im.size
    px = im.load()

    clear = sum(1 for i in range(0, w * h, max(1, (w * h) // 3000))
                if px[i % w, i // w][3] < 24)
    checked = len(range(0, w * h, max(1, (w * h) // 3000)))
    transparent = checked and (clear / float(checked)) > 0.10

    out = Image.new("RGBA", (w, h), (255, 255, 255, 0))
    op = out.load()
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if transparent:
                na = a
            else:
                na = 255 - int(0.299 * r + 0.587 * g + 0.114 * b)
                if na < 28:
                    na = 0
            if na:
                op[x, y] = (255, 255, 255, na)

    bbox = out.getbbox()
    if not bbox:
        return None
    cropped = out.crop(bbox)

    pad = int(SIZE * 0.06)
    box = SIZE - 2 * pad
    cw, ch = cropped.size
    scale = min(box / float(cw), box / float(ch))
    dw, dh = max(1, int(round(cw * scale))), max(1, int(round(ch * scale)))
    canvas = Image.new("RGBA", (SIZE, SIZE), (255, 255, 255, 0))
    resized = cropped.resize((dw, dh), Image.LANCZOS)
    canvas.paste(resized, ((SIZE - dw) // 2, (SIZE - dh) // 2), resized)
    return canvas


def fetch(name):
    titles = [WANTED[name]] + FALLBACKS.get(name, [])
    for title in titles:
        info = imageinfo(title)
        if not info:
            continue
        meta = info.get("extmetadata") or {}
        lic = (meta.get("LicenseShortName", {}).get("value") or "").strip()
        author = flatten((meta.get("Artist", {}).get("value") or "").strip())

        url = info.get("thumburl") or info.get("url")
        if not url:
            continue
        raw = get(url, binary=True)
        if not raw:
            continue
        img = to_icon(raw)
        if img is None:
            continue

        os.makedirs(ICON_DIR, exist_ok=True)
        os.makedirs(SOURCED, exist_ok=True)
        img.save(os.path.join(ICON_DIR, name + ".png"))
        with open(os.path.join(SOURCED, name + ".gameicon.png"), "wb") as fh:
            fh.write(raw)
        return "ok", lic or "?", "| %s | %s | %s | %s |" % (
            name, title.replace("File:", ""), lic or "?", author or "unknown")

    return "fail", "no title resolved", ""


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default=None)
    args = ap.parse_args()

    names = [args.only] if args.only else sorted(WANTED)
    got, failed, credits = [], [], []

    for i, name in enumerate(names, 1):
        if name not in WANTED:
            print("  %s: not in the list" % name)
            continue
        print("  [%2d/%2d] %-11s" % (i, len(names), name), end=" ", flush=True)
        state, why, credit = fetch(name)
        print("%-4s %s" % (state, why))
        if state == "ok":
            got.append(name)
            credits.append(credit)
        else:
            failed.append(name)

    if credits:
        old = open(ATTRIB, encoding="utf-8").read() if os.path.exists(ATTRIB) else "# Icon sources\n"
        marker = "\n## Object icons - game-icons.net"

        # MERGE, do not replace. A --only run knows about one icon; rewriting the table from just
        # that run would silently drop ten credits and leave the mod using CC BY art uncredited.
        kept = {}
        if marker in old:
            for line in old[old.index(marker):].splitlines():
                cells = [c.strip() for c in line.strip().strip("|").split("|")]
                if len(cells) == 4 and cells[0] not in ("icon", "---") and "---" not in cells[1]:
                    kept[cells[0]] = line.strip()
            old = old[:old.index(marker)]
        for credit in credits:
            kept[credit.strip().strip("|").split("|")[0].strip()] = credit

        block = ("\n## Object icons - game-icons.net via Wikimedia Commons\n\n"
                 "CC BY 3.0. Artists named per icon.\n\n"
                 "| icon | file | licence | artist |\n|---|---|---|---|\n"
                 + "\n".join(kept[k] for k in sorted(kept)) + "\n")
        with open(ATTRIB, "w", encoding="utf-8") as fh:
            fh.write(old.rstrip() + "\n" + block)

    print("\n  sourced %d, failed %d" % (len(got), len(failed)))
    if failed:
        print("  failed: " + ", ".join(failed))
    return 0


if __name__ == "__main__":
    sys.exit(main())
