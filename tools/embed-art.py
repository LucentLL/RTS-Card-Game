# -*- coding: utf-8 -*-
"""
embed-art.py — bake the images in assets/cards/ straight into the HTML so the
game is ONE portable file that shows your art anywhere: opened directly
(file://), on a phone, offline, or zipped and shared. No server needed.

Run it whenever you add or change art:

    python3 tools/embed-art.py

Reads : spawn-row-duel-v26.html      (the editable source, external image refs)
Writes: spawn-row-duel-v26.portable.html   (self-contained, art embedded)

Art files follow the auto-convention used by the game: a card named "Magmaw"
loads  assets/cards/magmaw_cardart.png . This tool scans the folder for every
"<slug>_cardart.<ext>" file and injects them into the game's `w.EMBEDDED` map
(keyed by <slug>), which the game checks before going to the network. So the
source file is never modified — your drop-a-file workflow stays intact; just
re-run this to refresh the portable build.
"""
import io, os, base64

HERE   = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC    = os.path.join(HERE, "spawn-row-duel-v26.html")
OUT    = os.path.join(HERE, "spawn-row-duel-v26.portable.html")
ARTDIR = os.path.join(HERE, "assets", "cards")
SUFFIX = "_cardart"

MIME = {".png": "image/png", ".jpg": "image/jpeg", ".jpeg": "image/jpeg", ".webp": "image/webp", ".gif": "image/gif"}


def data_uri(path, mime):
    with open(path, "rb") as f:
        b64 = base64.b64encode(f.read()).decode("ascii")
    return "data:%s;base64,%s" % (mime, b64)


with io.open(SRC, "r", encoding="utf-8") as f:
    html = f.read()

# Scan for <slug>_cardart.<ext> images and key them by <slug> (the game derives the
# same slug from each card's name, so they line up automatically).
entries  = {}
embedded = []
skipped  = []
for fn in sorted(os.listdir(ARTDIR)):
    if fn.startswith(".") or fn.startswith("_") or fn.lower().endswith(".md"):
        continue
    full = os.path.join(ARTDIR, fn)
    if not os.path.isfile(full):
        continue
    base, ext = os.path.splitext(fn)
    ext = ext.lower()
    mime = MIME.get(ext)
    if not mime:
        skipped.append(fn + " (unsupported type)")
        continue
    if not base.endswith(SUFFIX):
        skipped.append(fn + " (name must end with '%s', e.g. magmaw%s.png)" % (SUFFIX, SUFFIX))
        continue
    slug = base[:-len(SUFFIX)]
    if slug in entries:
        skipped.append(fn + " (duplicate slug '%s' already embedded)" % slug)
        continue
    entries[slug] = data_uri(full, mime)
    embedded.append(fn)

# Inject the map into the game's `w.EMBEDDED = { ... }` object (replace its braces' contents).
key = "w.EMBEDDED ="
i  = html.index(key)
lb = html.index("{", i)
rb = html.index("}", lb)
obj = "{" + ",".join("'%s':'%s'" % (slug, uri) for slug, uri in sorted(entries.items())) + "}"
out_html = html[:lb] + obj + html[rb + 1:]

with io.open(OUT, "w", encoding="utf-8") as f:
    f.write(out_html)

print("Embedded %d image(s): %s" % (len(embedded), ", ".join(embedded) or "none"))
if skipped:
    print("Skipped: " + "; ".join(skipped))
print("Wrote %s (%d KB)" % (os.path.basename(OUT), len(out_html.encode("utf-8")) // 1024))
print("Cards with no image file still show their built-in placeholder.")
