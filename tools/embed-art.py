# -*- coding: utf-8 -*-
"""
embed-art.py — bake the images in assets/cards/ straight into the HTML so the
game is ONE portable file that shows your art anywhere: opened directly
(file://), on a phone, offline, or zipped and shared. No server needed.

Run it whenever you add or change art:

    python3 tools/embed-art.py

Reads : spawn-row-duel-v26.html      (the editable source, external image refs)
Writes: spawn-row-duel-v26.portable.html   (self-contained, art embedded)

The source file is never modified, so your drop-a-file / swap-by-name workflow
stays intact — re-run this to refresh the portable build.
"""
import io, os, base64

HERE   = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC    = os.path.join(HERE, "spawn-row-duel-v26.html")
OUT    = os.path.join(HERE, "spawn-row-duel-v26.portable.html")
ARTDIR = os.path.join(HERE, "assets", "cards")

MIME = {".png": "image/png", ".jpg": "image/jpeg", ".jpeg": "image/jpeg", ".webp": "image/webp", ".gif": "image/gif"}

def data_uri(path):
    ext = os.path.splitext(path)[1].lower()
    mime = MIME.get(ext)
    if not mime:
        return None
    with open(path, "rb") as f:
        b64 = base64.b64encode(f.read()).decode("ascii")
    return "data:%s;base64,%s" % (mime, b64)

with io.open(SRC, "r", encoding="utf-8") as f:
    html = f.read()

# Operate only inside the CARD_FILES table so nothing else is touched.
start = html.index("w.CARD_FILES = {")
end   = html.index("};", start) + 2
block = html[start:end]

embedded = []
skipped  = []
for fn in sorted(os.listdir(ARTDIR)):
    if fn.startswith(".") or fn.startswith("_") or fn.lower().endswith(".md"):
        continue
    full = os.path.join(ARTDIR, fn)
    if not os.path.isfile(full):
        continue
    uri = data_uri(full)
    token = "'" + fn + "'"
    if uri and token in block:
        block = block.replace(token, "'" + uri + "'")
        embedded.append(fn)
    elif uri:
        skipped.append(fn + " (in folder but no card maps to it)")
    else:
        skipped.append(fn + " (unsupported type)")

out_html = html[:start] + block + html[end:]
with io.open(OUT, "w", encoding="utf-8") as f:
    f.write(out_html)

print("Embedded %d image(s): %s" % (len(embedded), ", ".join(embedded) or "none"))
if skipped:
    print("Skipped: " + "; ".join(skipped))
print("Wrote %s (%d KB)" % (os.path.basename(OUT), len(out_html.encode("utf-8")) // 1024))
print("Cards with no image file still show their built-in placeholder.")
