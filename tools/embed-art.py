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


def sniff_mime(data, fallback):
    # trust the file's actual bytes over its extension (people mislabel webp as .png, etc.)
    if data[:8] == b"\x89PNG\r\n\x1a\n":
        return "image/png"
    if data[:3] == b"\xff\xd8\xff":
        return "image/jpeg"
    if data[:6] in (b"GIF87a", b"GIF89a"):
        return "image/gif"
    if data[:4] == b"RIFF" and data[8:12] == b"WEBP":
        return "image/webp"
    return fallback


def data_uri(path, fallback_mime):
    with open(path, "rb") as f:
        raw = f.read()
    mime = sniff_mime(raw, fallback_mime)
    b64 = base64.b64encode(raw).decode("ascii")
    return "data:%s;base64,%s" % (mime, b64)


with io.open(SRC, "r", encoding="utf-8") as f:
    html = f.read()

SPRITEDIR = os.path.join(HERE, "assets", "sprites")


def scan(dirpath, suffix, example):
    """Collect <slug><suffix>.<ext> images from dirpath, keyed by <slug>."""
    entries, embedded, skipped = {}, [], []
    if not os.path.isdir(dirpath):
        return entries, embedded, skipped
    for fn in sorted(os.listdir(dirpath)):
        if fn.startswith(".") or fn.startswith("_") or fn.lower().endswith(".md"):
            continue
        full = os.path.join(dirpath, fn)
        if not os.path.isfile(full):
            continue
        base, ext = os.path.splitext(fn)
        mime = MIME.get(ext.lower())
        if not mime:
            skipped.append(fn + " (unsupported type)")
            continue
        if not base.endswith(suffix):
            skipped.append(fn + " (name must end with '%s', e.g. %s)" % (suffix, example))
            continue
        slug = base[:-len(suffix)]
        if slug in entries:
            skipped.append(fn + " (duplicate slug '%s')" % slug)
            continue
        entries[slug] = data_uri(full, mime)
        embedded.append(fn)
    return entries, embedded, skipped


def inject(html, key, entries):
    """Replace the contents of the game's `key = { ... }` object literal."""
    i  = html.index(key)
    lb = html.index("{", i)
    rb = html.index("}", lb)
    obj = "{" + ",".join("'%s':'%s'" % (slug, uri) for slug, uri in sorted(entries.items())) + "}"
    return html[:lb] + obj + html[rb + 1:]


# Card art (assets/cards/<slug>_cardart.<ext>), on-field cut-outs (<slug>_fieldart.<ext>) and floating sprites
art_entries,    art_done,    art_skip    = scan(ARTDIR,    SUFFIX,      "magmaw" + SUFFIX + ".png")
field_entries,  field_done,  field_skip  = scan(ARTDIR,    "_fieldart", "aegisol_fieldart.png")
sprite_entries, sprite_done, sprite_skip = scan(SPRITEDIR, "_sprite",   "magmaw_sprite.png")
# the cardart and fieldart scans share assets/cards, so each reports the other's files as "wrong suffix" — drop that cross-noise
art_skip   = [s for s in art_skip   if "_fieldart" not in s]
field_skip = [s for s in field_skip if "_cardart"  not in s]

out_html = inject(html, "w.EMBEDDED =", art_entries)
out_html = inject(out_html, "w.EMBEDDED_FIELD =", field_entries)
out_html = inject(out_html, "w.EMBEDDED_SPRITES =", sprite_entries)

with io.open(OUT, "w", encoding="utf-8") as f:
    f.write(out_html)

print("Embedded %d card art image(s): %s" % (len(art_done), ", ".join(art_done) or "none"))
print("Embedded %d field cut-out(s): %s" % (len(field_done), ", ".join(field_done) or "none"))
print("Embedded %d sprite image(s): %s" % (len(sprite_done), ", ".join(sprite_done) or "none"))
skipped = art_skip + field_skip + sprite_skip
if skipped:
    print("Skipped: " + "; ".join(skipped))
print("Wrote %s (%d KB)" % (os.path.basename(OUT), len(out_html.encode("utf-8")) // 1024))
print("Cards with no image file still show their built-in placeholder / borrowed card art.")
