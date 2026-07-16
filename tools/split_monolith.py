#!/usr/bin/env python3
"""One-time (re-runnable) splitter: spawn-row-duel-v26.html -> index.html + src/styles/*.css + src/js/*.js

The split is PURE LINE SLICING at unique marker strings — no code is retyped, so the
build tool (tools/build.py) can reassemble a single-file that is BYTE-IDENTICAL to the
original monolith. This script asserts that identity itself before writing anything.

Layout emitted:
  index.html            head (verbatim) + <link> per CSS part + body DOM (verbatim) + <script src> per JS part
  src/styles/NN_*.css   the <style> block, sliced at section comments
  src/js/NN_*.js        the <script> block, sliced at layer/section banners (LOAD ORDER = filename order)
"""
import sys, io, os, json

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC  = os.path.join(ROOT, 'spawn-row-duel-v26.html')

# ---- markers: (filename, exact text the boundary line must START with) ----
# The first entry of each region has marker None = region start.
CSS_PARTS = [
    ('00_base.css',             None),
    ('01_board.css',            '  /* the mat is the surrounding "table" environment'),
    ('02_walls_hud.css',        '  /* WC3-style themed STONE command panel along the bottom'),
    ('03_cards.css',            '  /* hand RESTS as a compact name+cost strip'),
    ('04_panels_menus.css',     '  /* RTS build menu */'),
    ('05_overlays_screens.css', '  /* ---------- pause-to-respond priority bar ----------'),
]
JS_PARTS = [
    ('01_core_defs.js',        None),
    ('02_art.js',              '/* Placeholder art. Element backgrounds are derived'),
    ('03_cards_creatures.js',  '/* ---------- creature pools (8 per element'),
    ('04_cards_leaders.js',    '/* ---------- Command Centers ----------'),
    ('05_board_state.js',      '/* ---------- board / row geometry'),
    ('06_mana_workers.js',     '/* ---------- mana (generic) ----------'),
    ('07_structures.js',       '/* ----- STRUCTURE UPGRADES: level a built structure up IN PLACE'),
    ('08_battlefield.js',      '/* ===== BATTLEFIELD SCENERY'),
    ('09_game_start.js',       'function startGame(youId,foeId,youDeck,foeDeck){'),
    ('10_menus_campaign.js',   '/* ===== main menu / deck builder / solo screens ====='),
    ('11_deck_builder.js',     '/* -- deck builder -- */'),
    ('12_render.js',           '/* ---------- render ---------- */'),
    ('13_input.js',            '/* ---------- input ---------- */'),
    ('14_spells_traps.js',     '/* ---------- spells & traps ---------- */'),
    ('15_combat.js',           '/* ---------- combat (row-distance targeting'),
    ('16_movement.js',         '/* ---------- movement (once/turn'),
    ('17_turns_ai.js',         '/* ---------- turns ---------- */'),
    ('18_inspect_viewers.js',  '/* ---------- tap-to-inspect: every card explains itself'),
    ('20_sfx.js',              '/* ---------- SFX: all sounds synthesized live'),
    ('21_fx.js',               '/* ---------- FX: overlay engine'),
    ('22_fx_wrappers.js',      '/* DOM cell for a live unit object'),
    ('30_resp.js',             '/* ═══════════ PAUSE-TO-RESPOND'),
    ('31_ui_shell.js',         '/* ---------- v18: fullscreen fit, hand fan, rotate prompt'),
    ('40_mp_net.js',           '/* ═══════════════════════ MULTIPLAYER LAYER (MP)'),
    ('41_mp_sync.js',          '/* ---------- 4.2 MPMAP: guest↔host perspective mirror'),
    ('42_mp_apply.js',         '/* ---------- 4.5 MPAPPLY: the host re-validates'),
    ('43_mp_intents.js',       '/* ---------- 4.6 wrappers: guest intent capture'),
    ('44_mp_lobby.js',         '/* ---------- 4.7 guest FX replay + decisions + protocol pump + lobby'),
    ('99_boot.js',             'bootstrap();'),
]

def main():
    raw = open(SRC, 'rb').read().decode('utf-8')
    lines = raw.split('\n')                      # elements have no trailing \n; joined with '\n' reproduces raw

    def find_line(exact):
        hits = [i for i, l in enumerate(lines) if l == exact]
        if len(hits) != 1:
            sys.exit(f'structural line not unique ({len(hits)} hits): {exact!r}')
        return hits[0]

    style_open  = find_line('<style>')
    style_close = find_line('</style>')
    script_close= find_line('</script>')
    # the game script opener: the LAST bare '<script>' line (the head has only a one-line inline SW script)
    script_opens = [i for i, l in enumerate(lines) if l == '<script>']
    if len(script_opens) != 1:
        sys.exit(f'expected exactly one bare <script> line, got {len(script_opens)}')
    script_open = script_opens[0]
    assert style_open < style_close < script_open < script_close, 'tag order broken'

    def slice_region(parts, lo, hi, label):
        """lo..hi are the content line indices (inclusive). Returns [(fname, [lines])]."""
        bounds = []
        for fname, marker in parts:
            if marker is None:
                bounds.append((fname, lo)); continue
            hits = [i for i in range(lo, hi + 1) if lines[i].startswith(marker)]
            if len(hits) != 1:
                sys.exit(f'{label} marker not unique ({len(hits)} hits): {marker!r}')
            bounds.append((fname, hits[0]))
        for a, b in zip(bounds, bounds[1:]):
            if not a[1] < b[1]:
                sys.exit(f'{label} markers out of order: {a[0]} !< {b[0]}')
        out = []
        for k, (fname, start) in enumerate(bounds):
            end = bounds[k + 1][1] - 1 if k + 1 < len(bounds) else hi
            out.append((fname, lines[start:end + 1]))
        return out

    css = slice_region(CSS_PARTS, style_open + 1, style_close - 1, 'css')
    js  = slice_region(JS_PARTS,  script_open + 1, script_close - 1, 'js')

    # ---- css sanity: brace balance per file (comment- and quote-aware scan) ----
    for fname, ls in css:
        depth, in_comment = 0, False
        for line in ls:
            i, q = 0, None                       # CSS strings can't span lines — quote state is per-line
            while i < len(line):
                ch = line[i]
                if in_comment:
                    if line[i:i+2] == '*/': in_comment = False; i += 1
                elif q:
                    if ch == '\\': i += 1
                    elif ch == q: q = None
                elif line[i:i+2] == '/*': in_comment = True; i += 1
                elif ch in '\'"': q = ch
                elif ch == '{': depth += 1
                elif ch == '}': depth -= 1
                i += 1
        if depth != 0 or in_comment:
            sys.exit(f'css {fname}: unbalanced braces/comment (depth={depth}, in_comment={in_comment})')

    # ---- assemble index.html ----
    head = lines[:style_open]                    # everything before <style>, verbatim
    mid  = lines[style_close + 1:script_open]    # </head><body> + DOM, verbatim
    tail = lines[script_close + 1:]              # </body></html> (+ trailing '' if file ends with \n)
    link_block   = [f'<link rel="stylesheet" href="src/styles/{f}">' for f, _ in css]
    script_block = [f'<script src="src/js/{f}"></script>' for f, _ in js]
    index = head + link_block + mid + script_block + tail

    # ---- BYTE-IDENTITY PROOF: rebuild the monolith from the pieces, compare to source ----
    rebuilt = (head + ['<style>'] + [l for _, ls in css for l in ls] + ['</style>']
               + mid + ['<script>'] + [l for _, ls in js for l in ls] + ['</script>'] + tail)
    if '\n'.join(rebuilt) != raw:
        sys.exit('REBUILD IS NOT BYTE-IDENTICAL — aborting, nothing written')

    # ---- write everything ----
    def w(path, ls):
        # Convention shared with tools/build.py: file content = '\n'.join(lines) + ONE extra '\n'.
        # build.py drops exactly one trailing '\n' when reading, so the round-trip is lossless
        # even when a part's last line is blank.
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, 'wb') as fh:
            fh.write(('\n'.join(ls) + '\n').encode('utf-8'))
    for fname, ls in css: w(os.path.join(ROOT, 'src', 'styles', fname), ls)
    for fname, ls in js:  w(os.path.join(ROOT, 'src', 'js', fname), ls)
    with open(os.path.join(ROOT, 'index.html'), 'wb') as fh:
        fh.write('\n'.join(index).encode('utf-8'))

    # manifest for tools/build.py (part order is load order)
    manifest = {'styles': [f for f, _ in css], 'js': [f for f, _ in js]}
    with open(os.path.join(ROOT, 'tools', 'build_manifest.json'), 'w', encoding='utf-8') as fh:
        json.dump(manifest, fh, indent=1)

    print(f'OK: byte-identity proven. {len(css)} css + {len(js)} js parts, index.html written.')
    for fname, ls in js: print(f'  src/js/{fname:24s} {len(ls):5d} lines')

if __name__ == '__main__':
    main()
