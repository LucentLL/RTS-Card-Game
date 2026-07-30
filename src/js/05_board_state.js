/* ---------- board / row geometry (§2–3) ----------
   Rows top→bottom. Distance = |rowIdx difference|. Adjacent rows are 1 apart.
   The center is a single shared row; its cells can hold a unit owned by either side. */
const ROWS=['foeBack','foeFront','center','youFront','youBack'];
function rowArr(key){
  if(key==='center')return G.center;
  if(key==='foeBack')return G.P.foe.back;
  if(key==='foeFront')return G.P.foe.front;
  if(key==='youFront')return G.P.you.front;
  if(key==='youBack')return G.P.you.back;
  return null;
}
function rowIdx(key){return ROWS.indexOf(key);}
function unitAt(key,i){return rowArr(key)[i];}
// rows a unit of `owner` may legally deploy into (own front/base + the shared center)
function ownRows(owner){return owner==='you'?['youFront','youBack','center']:['foeFront','foeBack','center'];}
function rowKeyFor(owner,which){return which==='center'?'center':(owner==='you'?(which==='front'?'youFront':'youBack'):(which==='front'?'foeFront':'foeBack'));}
// label used in logs / hints
function rowName(key){return ({foeBack:'enemy base',foeFront:'enemy front',center:'the contested center',youFront:'your front line',youBack:'your base'})[key]||key;}
// storage array for a (perspective-owner, which) pair; the center is shared regardless of owner
function cellArr(owner,which){return which==='center'?G.center:(G.P[owner]?G.P[owner][which]:null);}
function canDeploy(owner,which){return which==='center'||owner==='you';}
// the player's own rows for selecting/deploying, in board order back→front→center
const MINE=['youBack','youFront','center'];
function mineKey(which){return which==='center'?'center':(which==='front'?'youFront':'youBack');}
// ---- minion pools (workers live in a per-row pool, NOT in board slots) ----
function minPool(owner,which){return G.P[owner].min[which]||[];} // 'raid' has no pool — no support behind enemy lines
// every minion logically occupying row `key`, as {owner,which,c}. Center holds both sides' minions.
function minionsInRow(key){
  const out=[];
  const push=(owner,which)=>minPool(owner,which).forEach(c=>out.push({owner,which,c}));
  if(key==='foeBack')push('foe','back');
  else if(key==='foeFront')push('foe','front');
  else if(key==='youFront')push('you','front');
  else if(key==='youBack')push('you','back');
  else if(key==='center'){push('you','center');push('foe','center');}
  return out;
}
function whichForKey(owner,key){ // which pool of `owner` corresponds to row key (or null)
  if(key==='center')return 'center';
  if(owner==='you')return key==='youFront'?'front':(key==='youBack'?'back':null);
  return key==='foeFront'?'front':(key==='foeBack'?'back':null);
}
// ---- worker capacity (structures +support, monsters -upkeep; minions live within the cap) ----

function ownUnits(owner){ const out=[]; ROWS.forEach(k=>rowArr(k).forEach(o=>{if(o&&o.owner===owner)out.push(o);})); return out; } // fronts are contested — always filter by the unit's own tag
function structuresOf(owner){ return ownUnits(owner).filter(o=>o.kind==='building'); }
function structSupport(owner){ return structuresOf(owner).reduce((s,b)=>s+(b.sup||0),0); }
function monsterUpkeep(owner){ return ownUnits(owner).reduce((s,o)=>s+((o.kind==='creature'&&!o.worker)?(o.up||0):0),0); }
function workerCap(owner){ return structSupport(owner) - monsterUpkeep(owner); } // CC supplies the base via its support
// ===== NEW MODEL: workers are a per-row figure = Σ(structure support) − Σ(monster upkeep) in that row.
//   They are NOT trained and do NOT move; the rail pool is auto-derived from the cards in each row.
// Worker ZONES per owner: back / front / center / raid — 'raid' is the ENEMY front row, where your
// units stand with no structures behind them: its figure is never positive, so an army camped there
// must be paid for (or pulled back) at every upkeep. zoneKey maps a zone to the global row it reads.
const ZONES=['back','front','center','raid'];
// 'raid' spans BOTH enemy rows now that the enemy back row is enterable — deep sieges pay the same keep
function raidKeys(owner){ return owner==='you'?['foeFront','foeBack']:['youFront','youBack']; }
function zoneKeys(owner,z){ return z==='raid'?raidKeys(owner):[zoneKey(owner,z)]; }
function zoneKey(owner,z){ return z==='center'?'center':z==='raid'?(owner==='you'?'foeFront':'youFront'):rowKeyFor(owner,z); }
function rowWorkers(owner,which){
  let s=0;
  zoneKeys(owner,which).forEach(k=>rowArr(k).forEach(o=>{ if(!o||o.owner!==owner)return;
    if(o.kind==='building')s+=(o.sup||0)+(o.eff==='villager'?(o.val||0):0);
    else if(o.kind==='creature'&&!o.worker)s-=(o.up||0); }));
  if(which==='back') s+=CCS[G.P[owner].cc].wk;  // the homeland itself staffs the back row (the old keep's workers)
  return s;
}
function totalWorkers(owner){ return ['back','front','center'].reduce((s,w)=>s+Math.max(0,rowWorkers(owner,w)),0); }
// rebuild a row's worker pool to match its derived figure (preserving tapped/extracted state on survivors)
function syncWorkers(owner){
  ['back','front','center'].forEach(which=>{
    const target=Math.max(0,rowWorkers(owner,which));
    const pool=G.P[owner].min[which];
    while(pool.length>target) pool.pop();
    while(pool.length<target){ const w=mkVil(owner); w.sick=true; pool.push(w); } // new workers are summoning-sick: a structure can't harvest the turn it makes them
  });
}
// workers settle (become harvestable) at the START of the turn, AFTER upkeep + deficit balancing.
// Because this runs only at turn start, workers a structure adds mid-turn stay sick until next turn.
function readyWorkers(owner){ ['back','front','center'].forEach(w=>G.P[owner].min[w].forEach(m=>{m.sick=false;m.tapped=false;m.moved=false;})); }
// zones of `owner` that are short on workers (settled — moved, paid, or sacrificed — at upkeep).
// A zone's EFFECTIVE deficit is its raw shortfall minus what's been explicitly paid this upkeep (P.upaid).
function zoneDeficit(owner,z){ const paid=(G.P[owner].upaid||{})[z]||0; return Math.max(0, Math.max(0,-rowWorkers(owner,z))-paid); }
function deficitRows(owner){ return ZONES.filter(w=>zoneDeficit(owner,w)>0); }
function totalDeficit(owner){ return ZONES.reduce((s,w)=>s+zoneDeficit(owner,w),0); }
function creaturesInRow(owner,which){
  const out=[];
  zoneKeys(owner,which).forEach(k=>rowArr(k).forEach((o,i)=>{if(o&&o.owner===owner&&o.kind==='creature'&&!o.worker)out.push({which,key:k,i,o});}));
  return out;
}
