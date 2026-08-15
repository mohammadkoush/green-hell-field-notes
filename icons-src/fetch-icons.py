"""Source icons from Wikimedia Commons instead of drawing them.

WHY THIS WORKS WHERE THE EARLIER ATTEMPT DID NOT
The blocker was never the internet - it was that every icon site serves SVG and this machine has no
SVG renderer (no Inkscape, no rsvg-convert, no ImageMagick; the convert.exe on PATH is the Windows
filesystem tool). Commons solves that on its side: the imageinfo API takes `iiurlwidth` and hands
back a PNG it rasterised itself, even when the original is an SVG. No local renderer needed.

It also answers the licence question in the same call. `extmetadata.LicenseShortName` comes back with
every file, so "is this safe to redistribute in a published mod" is a field rather than a guess -
which matters, because he chose to assume publication.

WHAT IT ACCEPTS
Public domain and CC0 only, by default. CC-BY is available behind --allow-by and records the author
in ATTRIBUTIONS.md; anything else is refused and reported. A mod that ships art it cannot licence is
worse than a mod with a gap in it.

WHAT IT PRODUCES
64px white silhouettes with clean alpha, auto-cropped and centred - the same shape of output the
drawn set had, so nothing downstream changes. Two source shapes are handled:
  black-on-transparent   (most Commons silhouettes) - keep the alpha, force the colour white
  black-on-white         - alpha from darkness, colour white
Which one is in play is detected rather than assumed.

    python fetch-icons.py                 # fetch everything missing
    python fetch-icons.py --only tapir    # one name
    python fetch-icons.py --force         # re-fetch even if a file already exists
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
SOURCED_DIR = os.path.join(HERE, "sourced")          # untouched originals, for re-processing
ATTRIB = os.path.abspath(os.path.join(HERE, "..", "ATTRIBUTIONS.md"))

API = "https://commons.wikimedia.org/w/api.php"
# Commons asks for a descriptive agent and throttles anonymous scripts that do not send one.
UA = "FieldNotesIconBot/1.0 (Green Hell mod icon sourcing; contact: mohammadkoush@gmail.com)"

SIZE = 64

# Search terms per icon. The word "silhouette" does most of the work - it is how Commons categorises
# exactly the kind of art this needs - and the second term is a fallback when the first finds
# nothing usable.
WANTED = {
    "tapir":      ["tapir silhouette", "Tapirus silhouette"],
    "capybara":   ["capybara silhouette", "Hydrochoerus silhouette"],
    "peccary":    ["peccary silhouette", "wild boar silhouette"],
    "agouti":     ["agouti silhouette", "Dasyprocta silhouette"],
    "armadillo":  ["armadillo silhouette"],
    "turtle":     ["tortoise silhouette", "turtle silhouette"],
    "mouse":      ["mouse silhouette", "rat silhouette"],
    "anteater":   ["anteater silhouette", "Myrmecophaga silhouette"],
    "monkey":     ["monkey silhouette", "spider monkey silhouette"],
    "lizard":     ["lizard silhouette", "iguana silhouette"],
    "crab":       ["crab silhouette"],
    "frog":       ["frog silhouette"],
    "fish":       ["fish silhouette"],
    "bug":        ["beetle silhouette", "insect silhouette"],
    "bat":        ["bat silhouette"],
    "parrot":     ["parrot silhouette", "macaw silhouette"],
    "toucan":     ["toucan silhouette"],
    "snake":      ["snake silhouette", "serpent silhouette"],
    "spider":     ["spider silhouette", "tarantula silhouette"],
    "scorpion":   ["scorpion silhouette"],
    "predator":   ["jaguar silhouette", "panther silhouette"],
    "stingray":   ["stingray silhouette", "manta ray silhouette"],
    "animal":     ["deer silhouette", "mammal silhouette"],

    "bow":        ["bow and arrow silhouette", "archery bow icon"],
    "spear":      ["spear silhouette", "javelin silhouette"],
    "axe":        ["axe silhouette", "hatchet silhouette"],
    "caveman":    ["caveman silhouette", "neanderthal silhouette"],

    "coconut":    ["coconut silhouette", "coconut icon"],
    "banana":     ["banana silhouette", "banana icon"],
    "papaya":     ["papaya silhouette", "papaya icon"],
    "cassava":    ["cassava silhouette", "manioc icon"],
    "palmheart":  ["palm tree silhouette", "palm leaf silhouette"],
    "molineria":  ["berry silhouette", "berries icon"],
    "mushroom":   ["mushroom silhouette"],
    "birdnest":   ["bird nest silhouette", "nest icon"],
    "plant":      ["plant silhouette", "leaf silhouette"],
    "camp":       ["crossed tools silhouette", "hammer and axe icon"],
}

FREE = ("cc0", "public domain", "pd-", "pd ", "no restrictions")
BY_OK = ("cc by", "cc-by")


def get(url, binary=False, tries=3):
    for attempt in range(tries):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": UA})
            with urllib.request.urlopen(req, timeout=30) as r:
                data = r.read()
            return data if binary else json.loads(data.decode("utf-8"))
        except Exception as exc:
            if attempt == tries - 1:
                print("      ! %s" % exc)
                return None
            time.sleep(1.5 * (attempt + 1))
    return None


def search(term, limit=12):
    q = urllib.parse.urlencode({
        "action": "query", "format": "json", "list": "search",
        "srsearch": term, "srnamespace": "6", "srlimit": str(limit),
    })
    d = get("%s?%s" % (API, q))
    if not d:
        return []
    return [r["title"] for r in d.get("query", {}).get("search", [])]


def imageinfo(title):
    q = urllib.parse.urlencode({
        "action": "query", "format": "json", "prop": "imageinfo",
        "iiprop": "url|extmetadata", "iiurlwidth": "512", "titles": title,
    })
    d = get("%s?%s" % (API, q))
    if not d:
        return None
    pages = d.get("query", {}).get("pages", {})
    for _, page in pages.items():
        info = (page.get("imageinfo") or [None])[0]
        if info:
            return info
    return None


def licence_of(info):
    meta = info.get("extmetadata") or {}
    short = (meta.get("LicenseShortName", {}).get("value") or "").strip()
    author = (meta.get("Artist", {}).get("value") or "").strip()
    # Artist arrives as HTML often enough to be worth flattening crudely.
    for tag in ("<br>", "<br/>", "</a>", "</span>", "</div>"):
        author = author.replace(tag, " ")
    while "<" in author and ">" in author:
        a = author.index("<"); b = author.index(">", a)
        author = author[:a] + author[b + 1:]
    return short, " ".join(author.split())[:120]


def acceptable(short, allow_by):
    s = short.lower()
    if any(f in s for f in FREE):
        return True
    if allow_by and any(b in s for b in BY_OK) and "nd" not in s and "nc" not in s:
        return True
    return False


def to_silhouette(raw):
    """PNG bytes -> a 64px white silhouette with clean alpha, or None."""
    try:
        im = Image.open(io.BytesIO(raw)).convert("RGBA")
    except Exception:
        return None

    w, h = im.size
    px = im.load()

    # Which kind of source is this? If a real chunk of the image is transparent, the shape is
    # already cut out and the alpha channel IS the silhouette. Otherwise it is ink on paper.
    clear = 0
    step = max(1, (w * h) // 4000)
    checked = 0
    for i in range(0, w * h, step):
        a = px[i % w, i // w][3]
        checked += 1
        if a < 24:
            clear += 1
    transparent_source = checked and (clear / float(checked)) > 0.15

    out = Image.new("RGBA", (w, h), (255, 255, 255, 0))
    op = out.load()
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if transparent_source:
                na = a
            else:
                lum = int(0.299 * r + 0.587 * g + 0.114 * b)
                na = 255 - lum
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
    resized = cropped.resize((dw, dh), Image.LANCZOS)

    canvas = Image.new("RGBA", (SIZE, SIZE), (255, 255, 255, 0))
    canvas.paste(resized, ((SIZE - dw) // 2, (SIZE - dh) // 2), resized)
    return canvas


def ink_fraction(img):
    """How much of the square is actually filled. Guards against two failure modes at once:
    a near-empty result (a thin line drawing that vanished) and a near-solid one (a photo or a
    filled background that keyed to a black square)."""
    px = img.load()
    on = 0
    for y in range(0, SIZE, 2):
        for x in range(0, SIZE, 2):
            if px[x, y][3] > 40:
                on += 1
    return on / float((SIZE // 2) ** 2)


def solidity(img):
    """What fraction of the ink sits in solid MASS rather than in specks.

    This exists because the first run returned a scanned book cover for "tapir silhouette" and
    every other check passed it: the licence was public domain, the ink coverage was reasonable,
    the image was real. What it was not, was a shape.

    A silhouette is one or two large connected areas, so nearly every ink pixel is surrounded by
    ink. A page of text - or a line drawing, or a photo keyed to noise - is thousands of tiny marks,
    so most ink pixels have empty neighbours. Counting fully-surrounded ink separates the two
    without needing to understand either.
    """
    px = img.load()
    ink = 0
    surrounded = 0
    for y in range(1, SIZE - 1):
        for x in range(1, SIZE - 1):
            if px[x, y][3] <= 40:
                continue
            ink += 1
            if (px[x - 1, y][3] > 40 and px[x + 1, y][3] > 40 and
                    px[x, y - 1][3] > 40 and px[x, y + 1][3] > 40):
                surrounded += 1
    return (surrounded / float(ink)) if ink else 0.0


def title_matches(title, keyword):
    """The file's own name has to mention the thing. Cheap, and it alone would have thrown out the
    Baudelaire book that came back for 'tapir silhouette' - Commons full-text search matches page
    content, not just titles, so the search term is a suggestion rather than a filter."""
    t = title.lower()
    return keyword.lower() in t


def fetch_one(name, terms, allow_by, force):
    dest = os.path.join(ICON_DIR, name + ".png")
    if os.path.exists(dest) and not force:
        return ("skip", "already present", "")

    for term in terms:
        keyword = term.split()[0]
        titles = search(term)
        # Anything with "silhouette" in the filename first - that is the art we actually want, and
        # trying it before the rest saves downloading a pile of photographs.
        titles.sort(key=lambda t: ("silhouette" not in t.lower(), len(t)))

        for title in titles:
            low = title.lower()
            if low.endswith((".pdf", ".tif", ".tiff", ".webm", ".ogv", ".djvu")):
                continue
            if not title_matches(title, keyword):
                continue
            # Scans and book pages are never what we want and are the single biggest source of
            # false positives - Commons is full of them and they match on page text.
            if any(bad in low for bad in ("page", "scan", "cover", "plate ", "book", "map of")):
                continue
            info = imageinfo(title)
            if not info:
                continue
            short, author = licence_of(info)
            if not acceptable(short, allow_by):
                continue

            url = info.get("thumburl") or info.get("url")
            if not url:
                continue
            raw = get(url, binary=True)
            if not raw:
                continue

            img = to_silhouette(raw)
            if img is None:
                continue
            frac = ink_fraction(img)
            # Too empty means the shape did not survive keying; too full means a photo or a solid
            # block came through. Either way it is not a silhouette.
            if frac < 0.05 or frac > 0.80:
                continue
            # And it has to be MASS, not specks. This is the check the book cover failed.
            if solidity(img) < 0.55:
                continue

            os.makedirs(ICON_DIR, exist_ok=True)
            os.makedirs(SOURCED_DIR, exist_ok=True)
            img.save(dest)
            with open(os.path.join(SOURCED_DIR, name + ".src.png"), "wb") as fh:
                fh.write(raw)
            return ("ok", short, "%s | %s | %s" % (title, short, author or "unknown"))

    return ("fail", "nothing acceptable found", "")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default=None)
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--allow-by", action="store_true",
                    help="also accept CC-BY, recording the author in ATTRIBUTIONS.md")
    args = ap.parse_args()

    names = [args.only] if args.only else sorted(WANTED)
    lines, got, failed, skipped = [], [], [], []

    for i, name in enumerate(names, 1):
        terms = WANTED.get(name)
        if not terms:
            print("  %-12s no search terms" % name)
            continue
        print("  [%2d/%2d] %-12s" % (i, len(names), name), end=" ", flush=True)
        state, note, credit = fetch_one(name, terms, args.allow_by, args.force)
        print(state if state != "ok" else "ok  (%s)" % note)
        if state == "ok":
            got.append(name)
            lines.append("| %s | %s |" % (name, credit))
        elif state == "fail":
            failed.append(name)
        else:
            skipped.append(name)

    if lines:
        with open(ATTRIB, "w", encoding="utf-8") as fh:
            fh.write("# Icon sources\n\n")
            fh.write("Every icon below was fetched from Wikimedia Commons and accepted only if its\n")
            fh.write("licence is public domain or CC0 (or CC-BY when explicitly allowed, in which case\n")
            fh.write("the author is named). Originals are kept in `icons-src/sourced/`.\n\n")
            fh.write("| icon | source file, licence, author |\n|---|---|\n")
            fh.write("\n".join(sorted(lines)) + "\n")

    print("\n  sourced %d, failed %d, skipped %d" % (len(got), len(failed), len(skipped)))
    if failed:
        print("  no acceptable source: " + ", ".join(failed))
    return 0


if __name__ == "__main__":
    sys.exit(main())
