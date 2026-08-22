// The comparison surface: a normalised projection of a match, produced identically from either
// engine. The C# side has a twin (StateProjection.cs) that emits the same shape.
//
// Why a projection rather than the state hash? The hash is a byte stream over C# field order; for
// the JS to reproduce it, dump.mjs would have to mirror StateCodec exactly, field for field, and
// every future codec tweak would break the harness for reasons that have nothing to do with the
// rules. A projection compares what the RULES decide - who stands where, with what stats, holding
// what - which is the thing under test. Byte-identical hashing stays available later as a
// tightening, once the projection is green.
//
// Ordering is canonical (ascending cell index, then pools) so a diff is positional, never a
// set-membership puzzle.

export const ROW_ORDER = ['foeBack', 'foeFront', 'center', 'youFront', 'youBack'];

/** C# RowKey order, so cell indices line up with CellRef.Index on the other side. */
function cellIndex(rowIdx, col) { return rowIdx * 7 + col; }

function unitOf(o) {
  if (!o) return null;
  if (o.kind === 'creature') {
    return {
      k: o.worker ? 'worker' : 'creature',
      nm: o.nm,
      a: o.a | 0,
      hp: o.h | 0,
      maxhp: (o.maxh ?? o.h) | 0,
      own: o.owner,
      bank: o.bank | 0,
      sick: !!o.sick,
      tap: !!o.tapped,
      kw: o.kw || null,
      tok: !!o.token,
      cnt: o.cnt | 0,
      oc: o.oc | 0,
    };
  }
  if (o.kind === 'building') {
    return { k: 'building', nm: o.nm, hp: o.h | 0, maxhp: (o.maxh ?? o.h) | 0,
             own: o.owner, bank: o.bank | 0, bid: o.bid || null, sup: o.sup | 0 };
  }
  if (o.kind === 'charge') {
    return { k: 'charge', nm: o.card && o.card.nm, own: o.owner, inv: o.inv | 0,
             ctype: o.ctype || 'creature', setTurn: o.setTurn | 0 };
  }
  if (o.kind === 'trap') {
    return { k: 'trap', nm: o.card && o.card.nm, own: o.owner, setTurn: o.setTurn | 0,
             trigger: (o.card && o.card.trigger) || null };
  }
  return { k: o.kind, own: o.owner };
}

function playerOf(P) {
  return {
    life: P.life | 0,
    mana: P.mana | 0,
    hand: (P.hand || []).map((c) => c.nm).sort(),
    handN: (P.hand || []).length,
    deckN: (P.deck || []).length,
    graveN: (P.grave || []).length,
    workers: {
      back: (P.min.back || []).length,
      front: (P.min.front || []).length,
      center: (P.min.center || []).length,
    },
    workersReady: {
      back: (P.min.back || []).filter((w) => !w.sick && !w.tapped).length,
      front: (P.min.front || []).filter((w) => !w.sick && !w.tapped).length,
      center: (P.min.center || []).filter((w) => !w.sick && !w.tapped).length,
    },
  };
}

/** Build the projection from the JS window's live G. */
export function projectJs(win) {
  const G = win.eval('G');
  const cells = [];
  ROW_ORDER.forEach((key, r) => {
    const arr = win.eval(`rowArr(${JSON.stringify(key)})`);
    for (let col = 0; col < 7; col++) {
      const u = unitOf(arr[col]);
      if (u) cells.push({ i: cellIndex(r, col), ...u });
    }
  });

  return {
    turn: G.turn,
    turnNo: G.turnNo | 0,
    phase: G.phase,
    over: !!G.over,
    you: playerOf(G.P.you),
    foe: playerOf(G.P.foe),
    cells,
  };
}

/** Stable stringify: keys sorted, so a textual diff is a real diff. */
export function canonical(v) {
  if (v === null || typeof v !== 'object') return JSON.stringify(v);
  if (Array.isArray(v)) return '[' + v.map(canonical).join(',') + ']';
  const keys = Object.keys(v).sort();
  return '{' + keys.map((k) => JSON.stringify(k) + ':' + canonical(v[k])).join(',') + '}';
}

/** First differing path between two projections, or null. */
export function firstDiff(a, b, path = '') {
  if (canonical(a) === canonical(b)) return null;
  if (a === null || b === null || typeof a !== 'object' || typeof b !== 'object') {
    return { path, a, b };
  }
  if (Array.isArray(a) !== Array.isArray(b)) return { path, a, b };
  if (Array.isArray(a)) {
    for (let i = 0; i < Math.max(a.length, b.length); i++) {
      const d = firstDiff(a[i] ?? null, b[i] ?? null, `${path}[${i}]`);
      if (d) return d;
    }
    return { path, a, b };
  }
  for (const k of new Set([...Object.keys(a), ...Object.keys(b)])) {
    const d = firstDiff(a[k] ?? null, b[k] ?? null, path ? `${path}.${k}` : k);
    if (d) return d;
  }
  return { path, a, b };
}
