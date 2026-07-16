#!/usr/bin/env python3
"""Build the single-file distribution: index.html + src parts -> dist/spawn-row-duel.html

Inverse of tools/split_monolith.py: replaces the stylesheet <link> block with an inline
<style> block and the <script src> block with one inline <script> block. Used for
packaging (portable build, Tauri/Capacitor shells) — the web game on GitHub Pages serves
index.html + src/ directly and does NOT need this.

Usage: py tools/build.py            -> dist/spawn-row-duel.html
"""
import sys, os, json

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

def read_part(path):
    data = open(path, 'rb').read().decode('utf-8')
    if not data.endswith('\n'):
        sys.exit(f'{path}: expected trailing newline (see split_monolith.py convention)')
    return data[:-1]                             # drop exactly the ONE conventional trailing \n

def replace_block(lines, tags, replacement, label):
    idx = []
    for t in tags:
        hits = [i for i, l in enumerate(lines) if l == t]
        if len(hits) != 1:
            sys.exit(f'{label}: tag not found exactly once in index.html: {t!r}')
        idx.append(hits[0])
    if idx != list(range(idx[0], idx[0] + len(idx))):
        sys.exit(f'{label}: tag block is not contiguous/in manifest order')
    return lines[:idx[0]] + replacement + lines[idx[-1] + 1:]

def main():
    man = json.load(open(os.path.join(ROOT, 'tools', 'build_manifest.json'), encoding='utf-8'))
    index = open(os.path.join(ROOT, 'index.html'), 'rb').read().decode('utf-8').split('\n')

    css = [read_part(os.path.join(ROOT, 'src', 'styles', f)) for f in man['styles']]
    js  = [read_part(os.path.join(ROOT, 'src', 'js', f))     for f in man['js']]

    css_tags = [f'<link rel="stylesheet" href="src/styles/{f}">' for f in man['styles']]
    js_tags  = [f'<script src="src/js/{f}"></script>'            for f in man['js']]

    out = replace_block(index, css_tags, ['<style>'] + '\n'.join(css).split('\n') + ['</style>'], 'styles')
    out = replace_block(out,  js_tags,  ['<script>'] + '\n'.join(js).split('\n') + ['</script>'], 'script')

    dist = os.path.join(ROOT, 'dist')
    os.makedirs(dist, exist_ok=True)
    target = os.path.join(dist, 'spawn-row-duel.html')
    with open(target, 'wb') as fh:
        fh.write('\n'.join(out).encode('utf-8'))
    print(f'OK: wrote {target} ({os.path.getsize(target):,} bytes)')

if __name__ == '__main__':
    main()
