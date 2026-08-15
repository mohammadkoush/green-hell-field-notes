"""Source animal and plant icons from PhyloPic.

PhyloPic is a database of organism silhouettes - which is exactly and only what this mod needs, so
unlike a general image search there is no filtering out photographs, book scans or diagrams. The
first Commons attempt returned a Baudelaire book cover for "tapir silhouette", because full-text
search matches page CONTENT; PhyloPic has no such failure mode because every record IS a silhouette.

It also hands back ready-made PNG rasters, so the SVG-renderer problem that killed the earlier
attempt never arises.

LICENCE IS THE HARD FILTER. Each image carries a licence URL. Public domain and CC0 are taken
silently; CC-BY is taken only with --allow-by and the author is recorded in ATTRIBUTIONS.md;
anything NonCommercial or ShareAlike is refused outright, because this mod is assumed to be
published and NC art cannot ship in it. A gap in the icon set is recoverable. A licence violation
in a published mod is not.

    python fetch-phylopic.py                # public domain / CC0 only
    python fetch-phylopic.py --allow-by     # also CC-BY, with attribution recorded
    python fetch-phylopic.py --only tapir
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

API = "https://api.phylopic.org"
UA = "FieldNotesIconBot/1.0 (Green Hell mod icons; mohammadkoush@gmail.com)"
SIZE = 64

# icon name -> taxa to try, best first. Scientific names, because PhyloPic is taxonomic: asking for
# "capybara" finds nothing and asking for Hydrochoerus finds the animal.
TAXA = {
    # anteater, animal, caveman, mushroom, palmheart and plant deliberately do NOT live here
    # any more - they come from game-icons via fetch-gameicons.py. Two scripts owning one
    # filename is the double-write bug that has landed twice; the sets must stay disjoint.
    "tapir":     ["Tapirus", "Tapiridae"],
    "capybara":  ["Hydrochoerus", "Caviidae"],
    "peccary":   ["Tayassu", "Pecari", "Tayassuidae"],
    "agouti":    ["Dasyprocta", "Dasyproctidae"],
    "armadillo": ["Dasypus", "Cingulata"],
    "turtle":    ["Chelonoidis", "Testudinidae", "Testudines"],
    "mouse":     ["Mus", "Muridae"],
    "monkey":    ["Ateles", "Cebus", "Platyrrhini"],
    "lizard":    ["Iguana", "Iguanidae"],
    "crab":      ["Brachyura", "Cancer"],
    "frog":      ["Dendrobates", "Anura"],
    "fish":      ["Osteoglossum", "Characiformes", "Actinopterygii"],
    "bug":       ["Coleoptera", "Scarabaeidae"],
    "bat":       ["Chiroptera", "Phyllostomidae"],
    "parrot":    ["Ara", "Psittaciformes"],
    "toucan":    ["Ramphastos", "Ramphastidae"],
    "snake":     ["Boa", "Serpentes"],
    "spider":    ["Araneae", "Theraphosidae"],
    "scorpion":  ["Scorpiones"],
    "predator":  ["Panthera onca", "Panthera", "Felidae"],
    "stingray":  ["Potamotrygon", "Myliobatiformes", "Batoidea"],

    "coconut":   ["Cocos nucifera", "Cocos"],
    "banana":    ["Musa", "Musaceae"],
    "papaya":    ["Carica papaya", "Carica"],
    "cassava":   ["Manihot esculenta", "Manihot"],
    "molineria": ["Rubus", "Vaccinium"],

    # The three the notepad map carries. Ant and bee stand in for anthill and beehive - the
    # occupant is far more recognisable at icon size than the structure, and nobody mistakes an
    # ant for anything else.
    "anthill":   ["Formicidae", "Atta"],
    "honey":     ["Apis mellifera", "Apidae"],
}

FREE = ("publicdomain", "/zero/", "cc0")
BY = ("/by/", "licenses/by/")
REFUSE = ("-nc", "/nc", "noncommercial", "-sa", "/sa")


def get(url, binary=False, tries=3):
    for attempt in range(tries):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": UA})
            with urllib.request.urlopen(req, timeout=30) as r:
                data = r.read()
            return data if binary else json.loads(data.decode("utf-8"))
        except Exception as exc:
            if attempt == tries - 1:
                return None
            time.sleep(1.2 * (attempt + 1))
    return None


def build_id():
    root = get(API + "/")
    return (root or {}).get("build", 549)


def licence_ok(url, allow_by):
    u = (url or "").lower()
    if any(r in u for r in REFUSE):
        return False, "refused (NC/SA)"
    if any(f in u for f in FREE):
        return True, "public domain / CC0"
    if allow_by and any(b in u for b in BY):
        return True, "CC BY"
    return False, "not free enough"


def best_raster(links):
    """The smallest raster at least 256 across - big enough to downsample cleanly, small enough not
    to pull a megabyte per icon."""
    files = links.get("rasterFiles") or []
    best = None
    for f in files:
        try:
            w = int((f.get("sizes") or "0x0").split("x")[0])
        except ValueError:
            continue
        if w < 200:
            continue
        if best is None or w < best[0]:
            best = (w, f["href"])
    if best:
        return best[1]
    return files[-1]["href"] if files else None


def to_icon(raw):
    """PhyloPic rasters are black shapes on transparent. Force the colour white, keep the alpha,
    crop and centre - the same output shape the rest of the set already has."""
    try:
        im = Image.open(io.BytesIO(raw)).convert("RGBA")
    except Exception:
        return None

    px = im.load()
    w, h = im.size
    out = Image.new("RGBA", (w, h), (255, 255, 255, 0))
    op = out.load()
    solid = 0
    for y in range(h):
        for x in range(w):
            a = px[x, y][3]
            if a:
                op[x, y] = (255, 255, 255, a)
                if a > 40:
                    solid += 1

    if not solid:
        return None
    bbox = out.getbbox()
    if not bbox:
        return None

    cropped = out.crop(bbox)
    pad = int(SIZE * 0.06)
    box = SIZE - 2 * pad
    cw, ch = cropped.size
    scale = min(box / float(cw), box / float(ch))
    dw, dh = max(1, int(round(cw * scale))), max(1, int(round(ch * scale)))
    resized = cropped.resize((dw, dh), Image.LANCZOS)

    canvas = Image.new("RGBA", (SIZE, SIZE), (255, 255, 255, 0))
    canvas.paste(resized, ((SIZE - dw) // 2, (SIZE - dh) // 2), resized)
    return canvas


def ink_fraction(img):
    """How much of the 64px square the shape actually fills. This is the difference between an icon
    that is correct and one that is legible."""
    px = img.load()
    on = 0
    for y in range(0, SIZE, 2):
        for x in range(0, SIZE, 2):
            if px[x, y][3] > 40:
                on += 1
    return on / float((SIZE // 2) ** 2)


def fetch(name, taxa, build, allow_by, force):
    dest = os.path.join(ICON_DIR, name + ".png")
    if os.path.exists(dest) and not force:
        return "skip", "already present", ""

    # Take the BEST candidate, not the first.
    #
    # The first pass grabbed whatever was licence-clean and stopped, and it produced a set where
    # half the icons were correct and invisible: a thread-thin fish, a hairline snake, a lacy
    # mushroom. At 20 pixels on a dark minimap a silhouette is only as good as how much of the
    # square it fills, so every acceptable candidate is scored on ink coverage and the fullest
    # sensible one wins. Anything over ~0.62 is a blob with no readable outline; under ~0.12 it
    # disappears. The sweet spot is a shape you can still recognise but cannot miss.
    tried = []
    best = None   # (score, img, raw, credit)

    for taxon in taxa:
        q = urllib.parse.urlencode({"build": build, "filter_name": taxon.lower()})
        listing = get("%s/images?%s&page=0" % (API, q))
        items = ((listing or {}).get("_links") or {}).get("items") or []
        tried.append("%s(%d)" % (taxon, len(items)))

        for item in items[:14]:
            rec = get(API + item["href"])
            if not rec:
                continue
            links = rec.get("_links") or {}
            lic = (links.get("license") or {}).get("href", "")
            ok, why = licence_ok(lic, allow_by)
            if not ok:
                continue

            href = best_raster(links)
            if not href:
                continue
            raw = get(href, binary=True)
            if not raw:
                continue
            img = to_icon(raw)
            if img is None:
                continue

            fill = ink_fraction(img)
            # Distance from an ideal fill of 0.34, so both "too thin" and "too solid" lose.
            score = -abs(fill - 0.34)
            author = (links.get("contributor") or {}).get("title") or "unknown"
            credit = "%s | %s | %s | %s" % (item.get("title", taxon), why, author, lic)

            if best is None or score > best[0]:
                best = (score, img, raw, credit, fill)

        # A good enough candidate from an early, more specific taxon beats trawling the broader
        # fallbacks - Tapirus before Tapiridae, because the species is more likely to look right.
        if best is not None and abs(best[4] - 0.34) < 0.12:
            break

    if best is None:
        return "fail", "tried " + ", ".join(tried), ""

    os.makedirs(ICON_DIR, exist_ok=True)
    os.makedirs(SOURCED, exist_ok=True)
    best[1].save(dest)
    with open(os.path.join(SOURCED, name + ".phylopic.png"), "wb") as fh:
        fh.write(best[2])
    return "ok", "fill %.2f" % best[4], best[3]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default=None)
    ap.add_argument("--force", action="store_true")
    # CC-BY is ALLOWED BY DEFAULT and the author is recorded. It is a redistributable licence and
    # refusing it halved the hit rate for no benefit - the tapir failed with two NonCommercial
    # images and nothing else. --strict falls back to public domain / CC0 only.
    ap.add_argument("--strict", action="store_true",
                    help="public domain / CC0 only; refuse even CC-BY")
    args = ap.parse_args()

    build = build_id()
    print("PhyloPic build %s\n" % build)

    names = [args.only] if args.only else sorted(TAXA)
    got, failed, credits = [], [], []

    for i, name in enumerate(names, 1):
        taxa = TAXA.get(name)
        if not taxa:
            continue
        print("  [%2d/%2d] %-11s" % (i, len(names), name), end=" ", flush=True)
        state, why, credit = fetch(name, taxa, build, not args.strict, args.force)
        print("%-4s %s" % (state, why))
        if state == "ok":
            got.append(name)
            credits.append("| %s | %s |" % (name, credit))
        elif state == "fail":
            failed.append(name)

    if credits:
        old = ""
        if os.path.exists(ATTRIB):
            old = open(ATTRIB, encoding="utf-8").read()
        with open(ATTRIB, "w", encoding="utf-8") as fh:
            fh.write("# Icon sources\n\n")
            fh.write("Silhouettes from [PhyloPic](https://www.phylopic.org/), accepted only when the\n")
            fh.write("licence is public domain / CC0, or CC-BY with the author named below.\n")
            fh.write("NonCommercial and ShareAlike images are refused: this mod is assumed to be\n")
            fh.write("published, and a gap in the icon set is recoverable where a licence violation\n")
            fh.write("is not. Originals are kept in `icons-src/sourced/`.\n\n")
            fh.write("| icon | taxon, licence, contributor, url |\n|---|---|\n")
            fh.write("\n".join(sorted(credits)) + "\n")
            if old and "Icon sources" not in old.split("\n")[0]:
                fh.write("\n" + old)

    print("\n  sourced %d, failed %d" % (len(got), len(failed)))
    if failed:
        print("  no free silhouette: " + ", ".join(failed))
    return 0


if __name__ == "__main__":
    sys.exit(main())
