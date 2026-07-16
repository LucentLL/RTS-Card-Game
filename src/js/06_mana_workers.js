/* ---------- mana (generic) ----------
   One generic pool per player (P.mana). Element / card.color is a synergy +
   art attribute only (Yugioh-style) — it no longer gates cost. The legacy
   per-colour pool (P.cmana) is left seeded but inert. */
function manaTotal(o){return G.P[o].mana;}
function colorNeed(card){return false;}                       // element no longer gates cost
function canPay(o,card){return G.P[o].mana>=card.c;}
function payAny(o,n){const P=G.P[o];const g=Math.min(P.mana,n);P.mana-=g;return g>=n;}
function payCost(o,card){payAny(o,card.c);}
function manaGlyph(t){return '◆';}                            // mana is colourless now
function extractColors(owner,which){ return []; }   // mana is generic — no colour sources to enumerate
function minionCount(owner){ const m=G.P[owner].min; return m.back.length+m.front.length+m.center.length; }
function canTrain(owner){ return minionCount(owner) < workerCap(owner); }
// cull minions down to the cap (exposed front/center first) — the army "feeds" on the workforce
function enforceCap(owner){
  let over=minionCount(owner)-Math.max(0,workerCap(owner));
  if(over<=0)return;
  for(const w of ['front','center','back']){ const pool=G.P[owner].min[w];
    for(let i=pool.length-1;i>=0&&over>0;i--){ toGrave(owner,pool[i]); pool.splice(i,1); over--; }
  }
  log(`<span class="${owner==='you'?'e':'y'}">${owner==='you'?'Your':'Enemy'} workforce thins — Minions are pulled to sustain the army.</span>`);
}
function afterDeploy(owner){ syncWorkers(owner); }
const $=id=>document.getElementById(id);
const rng=a=>a[Math.floor(Math.random()*a.length)];
function deckOf(colors){ // colors: array — 40 cards of creatures + neutral spells (structures are built, not drawn)
  const d=[]; const n=colors.length;
  colors.forEach(col=>{
    const src=poolFor(col);
    for(let i=0;i<Math.round(28/n);i++){const t=rng(src);d.push({type:'creature',color:col,...t});}
    for(let i=0;i<Math.round(12/n);i++){const t=rng(SPELL_NEUTRAL);d.push({type:'spell',color:null,...t});}
  });
  while(d.length<DECK_SIZE){const t=rng(poolFor(colors[0]));d.push({type:'creature',color:colors[0],...t});}
  for(let i=d.length-1;i>0;i--){const j=Math.floor(Math.random()*(i+1));[d[i],d[j]]=[d[j],d[i]];}
  return d.slice(0,DECK_SIZE);}
/* ===== deck-building data: registry, saved decks (localStorage), validation, expander ===== */
const DECK_SIZE=40, MAX_COPIES=3, MAX_DECKS=5, DECKS_KEY='srd.decks.v1';
const CARD_REG=(function(){
  const reg=[]; const add=(arr,type,color)=>(arr||[]).forEach(t=>reg.push({key:(color||'neutral')+'|'+t.nm,type,color,nm:t.nm,tpl:t}));
  for(const el of COLORS){ add(POOLS[el],'creature',el); } // creatures only — structures are built, not drawn
  add(SPELL_NEUTRAL,'spell',null); // spells & traps are neutral — keyed 'neutral|<nm>', color null
  return reg;
})();
const CARD_BY_KEY=Object.fromEntries(CARD_REG.map(e=>[e.key,e]));
const SPELL_NAMES=new Set(SPELL_NEUTRAL.map(s=>s.nm));
function ccColors(ccId){ return (CCS[ccId]&&CCS[ccId].colors)||[]; }
function regForCC(ccId){ const cols=ccColors(ccId); return CARD_REG.filter(e=>e.color===null||cols.includes(e.color)); }
function cardColorOK(key,ccId){ const e=CARD_BY_KEY[key]; return !!e&&(e.color===null||ccColors(ccId).includes(e.color)); }
function escHtml(s){ return String(s==null?'':s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;').replace(/'/g,'&#39;'); }
function jsq(s){ return String(s==null?'':s).replace(/\\/g,'\\\\').replace(/'/g,"\\'"); } // safe inside a single-quoted JS string in an onclick
function isWellFormedDeck(d){ return d&&typeof d.name==='string'&&CCS[d.cc]&&d.cards&&typeof d.cards==='object'; }
// migrate saved decks across the rename: old color prefixes (ember/tide/verdant) -> element ids,
// old leader ids (emberbastion/tidespire/thornwall) -> new ids, and old spell keys -> neutral.
const COLOR_ALIAS={ember:'fire',tide:'water',verdant:'wind'};
const CC_ALIAS={emberbastion:'fire',tidespire:'water',thornwall:'fire_water'};
function migrateCC(id){ return CC_ALIAS[id]||id; }
function migrateKey(key){ const b=String(key).indexOf('|'); if(b<0)return key; let color=key.slice(0,b); const nm=key.slice(b+1);
  if(color!=='neutral'&&SPELL_NAMES.has(nm))return 'neutral|'+nm;            // spells are neutral
  if(COLOR_ALIAS[color])color=COLOR_ALIAS[color];                            // ember|X -> fire|X, etc.
  return color+'|'+nm; }
function migrateDeckCards(cards){ const out={}; for(const [k,v] of Object.entries(cards||{})){ const nk=migrateKey(k); if(!CARD_BY_KEY[nk])continue; /* drop structures & retired cards */ out[nk]=Math.min(MAX_COPIES,(out[nk]||0)+(v|0)); } return out; }
function loadDecks(){ try{ const raw=localStorage.getItem(DECKS_KEY); if(!raw)return []; const arr=JSON.parse(raw); if(!Array.isArray(arr))return [];
  return arr.map(d=>(d&&typeof d==='object')?{...d,cc:migrateCC(d.cc)}:d).filter(isWellFormedDeck).slice(0,MAX_DECKS).map(d=>({...d,cards:migrateDeckCards(d.cards)})); }catch(e){ return []; } }
function saveDecks(arr){ try{ localStorage.setItem(DECKS_KEY,JSON.stringify(arr.slice(0,MAX_DECKS))); return true; }catch(e){ return false; } }
function deleteDeck(i){ const a=loadDecks(); if(i>=0&&i<a.length){ a.splice(i,1); saveDecks(a); } }
function deckTotal(cards){ return Object.values(cards||{}).reduce((s,n)=>s+(n|0),0); }
function deckErrors(deck){
  const errs=[]; const cc=CCS[deck.cc]; if(!cc) return ['Unknown leader'];
  let total=0;
  for(const [key,cnt] of Object.entries(deck.cards||{})){
    const e=CARD_BY_KEY[key];
    if(!e){ errs.push('Unknown card in deck'); continue; }
    if(e.color!==null && !cc.colors.includes(e.color)) errs.push(e.nm+' is off-color');
    if(cnt<1||cnt>MAX_COPIES) errs.push(e.nm+' must be 1–'+MAX_COPIES);
    total+=cnt;
  }
  if(total!==DECK_SIZE) errs.push('Need exactly '+DECK_SIZE+' cards (have '+total+')');
  return errs;
}
function deckValid(deck){ return deckErrors(deck).length===0; }
function expandDeck(deck){
  const d=[];
  for(const [key,cnt] of Object.entries((deck&&deck.cards)||{})){
    const e=CARD_BY_KEY[key]; if(!e) continue;
    for(let i=0;i<cnt;i++) d.push({type:e.type,color:e.color,...e.tpl});
  }
  for(let i=d.length-1;i>0;i--){const j=Math.floor(Math.random()*(i+1));[d[i],d[j]]=[d[j],d[i]];}
  return d;
}
function mkCre(t,owner,worker){return {kind:'creature',id:uid++,owner,worker:!!worker,color:t.color||G.P[owner].color,nm:t.nm,a:t.a,h:t.h,maxh:t.h,c:t.c,fs:!!t.fs,up:t.up||0,sick:false,tapped:false,moved:false,bank:0,art:t.art,
  kw:t.kw||null,det:t.det||0,ward:t.ward||0,wardhp:t.wardhp||2,reap:t.reap||0,grow:t.grow||0,hatch:t.hatch||0,into:t.into||null,cnt:t.cnt||0,oc:t.oc||0,entrench:!!t.entrench,token:!!t.token,blocked:false,
  tribe:t.tribe||null,subtype:t.subtype||null};}
function mkVil(owner){return mkCre({nm:'Worker',a:0,h:1000,c:0,up:0,art:ART.villager},owner,true);}
function mkBld(t,owner){return {kind:'building',id:uid++,owner,color:t.color||G.P[owner].color,nm:t.nm,h:t.h,maxh:t.h,c:t.c,eff:t.eff,val:t.val||0,sup:t.sup||0,ic:t.ic,art:t.art,bank:0,bid:t.bid||null};}

/* ===== CREATURE KEYWORDS — element identities =====
   Hooks: enter (summon/flip) · death (cleanup) · defend (resolveCombat groupB) · upkeep (startTurn). */
function kwOf(o){ return (o&&o.kind==='creature'&&!o.worker)?(o.kw||null):null; }
function ekc(o){ return (o&&o.owner==='you')?'y':'e'; }
function enemyOf(owner){ return owner==='you'?'foe':'you'; }
function liveEnemyCreatures(owner){ const f=enemyOf(owner);
  return ownUnits(f).filter(o=>o.kind==='creature'&&!o.worker&&o.h>0); }
function liveEnemyStructures(owner){ const f=enemyOf(owner);
  return ownUnits(f).filter(o=>o.kind==='building'&&o.h>0); }
function firstEmptyCell(owner){
  for(const w of ['back','front']){ const a=G.P[owner][w]; const i=a.findIndex(x=>!x); if(i>=0)return {arr:a,i}; }
  const ci=G.center.findIndex((x,idx)=>!x&&isLane(idx)); if(ci>=0)return {arr:G.center,i:ci}; return null; }
function removeUnitFromBoard(unit){
  for(const key of ROWS){ const a=rowArr(key); const i=a.indexOf(unit); if(i>=0){ a[i]=null; return unit.owner; } }
  for(const o of ['you','foe']) for(const w of ['back','front','center']){ const p=G.P[o].min[w]; const i=p.indexOf(unit); if(i>=0){p.splice(i,1);return o;} }
  return null; }
function handcardFromCreature(cr){ return {kind:'handcard',id:uid++,type:'creature',color:cr.color,nm:cr.nm,a:cr.a,h:cr.maxh??cr.h,c:cr.c,fs:cr.fs,up:cr.up,art:cr.art,
  kw:cr.kw,det:cr.det,ward:cr.ward,wardhp:cr.wardhp,reap:cr.reap,grow:cr.grow,hatch:cr.hatch,into:cr.into,entrench:cr.entrench,tribe:cr.tribe,subtype:cr.subtype}; }
function mkToken(owner,nm,a,h,color){ const t=mkCre({nm,a,h,c:0,up:0},owner,false); t.token=true; t.color=color||G.P[owner].color; return t; }
function effA(c){ return (c?(c.a||0):0)+((c&&c._dis)||0); } // effective attack (Overcharge discharge bonus)

// ENTER (after a creature is summoned or flipped)
function onCreatureEnter(cr,owner){
  if(kwOf(cr)==='ward'){ const spot=firstEmptyCell(owner);
    if(spot){ const tok=mkToken(owner,'Lumen',0,cr.wardhp||2,cr.color); tok.sick=true; spot.arr[spot.i]=tok;
      log(`<span class="${ekc(cr)}">${cr.nm} conjures a Lumen ward (0/${tok.h}).</span>`,ekc(cr)); }
    else log(`<span class="${ekc(cr)}">${cr.nm} would ward, but there is no room.</span>`,ekc(cr)); }
}
// DEATH (called inside cleanup the moment a creature is removed; its cell is already freed)
function onCreatureDeath(cr,owner){
  if(kwOf(cr)==='detonate'){ const n=cr.det||0; if(n>0){
    const cres=liveEnemyCreatures(owner).sort((a,b)=>(b.a-a.a)||(a.h-b.h));
    let tgt=cres[0]||liveEnemyStructures(owner).sort((a,b)=>a.h-b.h)[0];
    if(tgt){ tgt.h-=n; log(`<span class="${ekc(cr)}">Detonate! ${cr.nm} bursts for ${n} into ${tgt.nm}.</span>`,ekc(cr)); } } }
  else if(kwOf(cr)==='reap'){ const spot=firstEmptyCell(owner);
    if(spot){ const a=cr.reap||1, tok=mkToken(owner,'Shade',a,a,cr.color); tok.sick=true; spot.arr[spot.i]=tok;
      log(`<span class="${ekc(cr)}">Reap. ${cr.nm} drags a Shade (${a}/${a}) up from the grave.</span>`,ekc(cr)); } }
}
// DEFEND (a Water creature in the blocking group shoves one attacker back to hand; Entrench is immune)
function applyUndertow(groupA,groupB){
  const wardens=groupB.filter(c=>c&&kwOf(c)==='undertow'&&c.h>0); if(!wardens.length)return;
  const marks=groupA.filter(c=>c&&c.kind==='creature'&&c.h>0&&!c.worker&&!c.token&&!c.entrench&&!c.cc).sort((a,b)=>(b.c||0)-(a.c||0));
  const a=marks[0]; if(!a)return;
  const ow=removeUnitFromBoard(a);
  if(ow){ G.P[ow].hand.push(handcardFromCreature(a)); const k=groupA.indexOf(a); if(k>=0)groupA.splice(k,1);
    log(`<span class="${ekc(wardens[0])}">Undertow! ${wardens[0].nm} hurls ${a.nm} back to ${ow==='you'?'your':'their'} hand.</span>`,ekc(wardens[0])); }
}
// UPKEEP (Chrysalis cocoons swell and hatch)
function chrysalisUpkeep(owner){
  const all=ownUnits(owner);
  all.forEach(o=>{ if(kwOf(o)==='chrysalis'){
    o.cnt=(o.cnt||0)+(o.grow||1);
    if(o.cnt>=(o.hatch||3)){ const into=o.into||{};
      o.nm=into.nm||o.nm; o.a=into.a??o.a; o.maxh=into.h??o.maxh; o.h=into.h??o.maxh??o.h; o.up=into.up??o.up; o.fs=into.fs??o.fs; o.kw=into.kw||null; o.sick=true;
      log(`<span class="${ekc(o)}">${o.nm} hatches! (⚔${o.a}/♥${o.h})</span>`,ekc(o));
    } else { o.sick=true; log(`<span class="${ekc(o)}">A cocoon swells (${o.cnt}/${o.hatch}).</span>`,ekc(o)); } }});
}
// UPKEEP (Overcharge creatures bank ◆ each turn, up to 3) — Electric identity, simple v1
function overchargeUpkeep(owner){
  const all=ownUnits(owner);
  all.forEach(o=>{ if(kwOf(o)==='overcharge'){ o.oc=Math.min(3,(o.oc||0)+1); log(`<span class="${ekc(o)}">${o.nm} overcharges (◆${o.oc}).</span>`,ekc(o)); } });
}
// ATTACK prep: Overcharge attackers discharge their banked ◆ as bonus attack for this strike only
function dischargeOvercharge(attackers){
  attackers.forEach(a=>{ if(a&&kwOf(a)==='overcharge'&&(a.oc||0)>0){ a._dis=a.oc; a.oc=0;
    log(`<span class="${ekc(a)}">Overcharge! ${a.nm} discharges +${a._dis}⚔.</span>`,ekc(a)); } });
}
function clearDischarge(units){ if(units)units.forEach(a=>{ if(a)a._dis=0; }); }
// ON-HIT: a Scour flier shatters one enemy back-row card on a connecting strike (face-down preferred)
function scourStrike(att,defOwner){
  const back=G.P[defOwner].back;
  let idx=back.findIndex(o=>o&&(o.kind==='charge'||o.kind==='trap'));
  if(idx<0) idx=back.findIndex(o=>o&&o.kind==='building'&&!o.cc);
  if(idx<0) return;
  const tgt=back[idx];
  log(`<span class="${ekc(att)}">Scour! ${att.nm} shatters ${defOwner==='you'?'your':'their'} ${tgt.kind==='trap'?'set trap':(tgt.kind==='charge'?'face-down card':tgt.nm)}.</span>`,ekc(att));
  if(tgt.kind==='charge'||tgt.kind==='trap'){ toGrave(defOwner,tgt); back[idx]=null; } else { tgt.h=0; }
}
function groupIsScour(attackers){ return attackers.length>0 && attackers.every(a=>kwOf(a)==='scour'); }
// Inspect text (so each card explains itself in the ⓘ panel)
function kwText(o){ switch(o&&o.kw){
  case 'detonate': return `<b>Detonate ${o.det}.</b> When destroyed, deals ${o.det} to the deadliest enemy creature (or an enemy structure). Never hits a command center.`;
  case 'undertow': return `<b>Undertow.</b> When this blocks or is attacked, the strongest attacking creature is hurled back to its owner's hand (re-summoning-sick).`;
  case 'entrench': return `<b>Entrench.</b> Immovable — cannot be bounced or pushed; effects like Undertow slide off.`;
  case 'ward': return `<b>Ward.</b> On entry, conjures a 0/${o.wardhp||2} Lumen token blocker beside it.`;
  case 'reap': return `<b>Reap ${o.reap}.</b> When destroyed, raises a ${o.reap}/${o.reap} Shade token in its place.`;
  case 'chrysalis': { const i=o.into||{}; return `<b>Chrysalis ${o.cnt||0}/${o.hatch||3}.</b> Cannot attack; swells +${o.grow||1} each of your turns, then hatches into ${i.nm} (⚔${i.a}/♥${i.h}).`; }
  case 'scour': return `<b>Scour.</b> Flier — ignores interceptors and shatters an enemy back-row card on attack.`;
  case 'overcharge': return `<b>Overcharge.</b> Banks ◆ each of your turns (up to 3); when it attacks it discharges them as bonus ⚔.`;
} return ''; }

/* ===== RTS structure building — commander build menu (cost = mana; prerequisite tech tree) ===== */
function ownBuildings(owner){ return ownUnits(owner).filter(o=>o.kind==='building'&&!o.cc); }
// a structure's tier lineage: its own bid plus every base it was UPGRADED from (via `from`), so an
// upgraded tier still satisfies tech-tree prereqs its base unlocked (e.g. Keep still counts as a Foundry).
function bidLineage(b){ const out=[]; let cur=b&&b.bid, g=0; while(cur&&g++<8){ out.push(cur); const d=resolveStruct(cur,b.color); cur=d&&d.from; } return out; }
function hasBuild(owner,bid){ return ownBuildings(owner).some(b=>bidLineage(b).indexOf(bid)>=0); }
function prereqMet(owner,def){ return (def.prereq||[]).every(p=>hasBuild(owner,p)); }
function hasEmptyDeploy(owner){ return ['back','front','center'].some(w=>{ const a=cellArr(owner,w); return a&&a.some(x=>!x); }); }
// a worker-COSTING building (negative sup, e.g. a tower) may only go in a row that stays non-negative
function placeRowOK(owner,which,def){ return (def.sup||0)>=0 || (rowWorkers(owner,which)+(def.sup||0))>=0; }
function hasPlacement(owner,def){ return ['back','front','center'].some(w=>{ const a=cellArr(owner,w); return a&&a.some(x=>!x)&&placeRowOK(owner,w,def); }); }
function canBuild(owner,def){ return manaTotal(owner)>=def.c && prereqMet(owner,def) && hasPlacement(owner,def); }
function resolveStruct(bid,color){ if(bid==='forge')return forgeDef(color); if(bid==='grandforge')return grandForgeDef(color); return STRUCT_DEFS[bid]||null; }
window.openBuildMenu=function(){ if(!acting())return; G.sel=null;G.atk=[];G.moveFrom=null;G.moveMana=null;G.cardMenu=null;G.build=null; $('buildPanel').style.display='flex'; drawBuild(); render(); };
function drawBuild(){
  const list=buildList(G.P.you.cc), have=manaTotal('you');
  const rows=list.map(def=>{
    const ok=canBuild('you',def);
    const PRN={foundry:'a Foundry',forge:'a Forge',longhouse:'a Longhouse',encampment:'an Encampment',outpost:'an Outpost'};
    const why=!prereqMet('you',def)?('needs '+def.prereq.map(p=>PRN[p]||('a '+p)).join(' + ')):(have<def.c?('need ◆'+def.c):(!hasPlacement('you',def)?((def.sup||0)<0?'no row with ⚒ to spare':'no open space'):''));
    const dot=def.color?`<span class="cdot" style="background:var(--${def.color})"></span>`:'';
    return `<div class="bdrow${ok?'':' off'}"><div class="bdic">${def.ic}</div>`+
      `<div class="bdmid"><div class="bdnm">${dot}${escHtml(def.nm)}</div><div class="bddesc">${escHtml(def.desc)}</div></div>`+
      `<button class="bdbtn" ${ok?`onclick="buildPick('${def.bid}','${def.color||''}')"`:`disabled title="${escHtml(why)}"`}>◆${def.c}</button></div>`;
  }).join('');
  $('buildPanel').querySelector('.box').innerHTML=
    `<div class="ptitle">⚒ Build</div><div class="pmeta">Raise structures — pay mana, follow the tech tree. <b>◆${have}</b> available.</div>`+
    `<div class="bdlist">${rows}</div><button class="pclose" onclick="closeBuild()">Done</button>`;
}
window.closeBuild=function(){ $('buildPanel').style.display='none'; defaultHint(); render(); };
window.buildPick=function(bid,color){ const def=resolveStruct(bid,color||null); if(!def||!canBuild('you',def))return;
  G.build=def; $('buildPanel').style.display='none';
  setHint(`Raising <b>${escHtml(def.nm)}</b> (◆${def.c}) — tap an open space in your rows. <button onclick="cancelBuild()">cancel</button>`); render(); };
window.cancelBuild=function(){ G.build=null; defaultHint(); render(); };
function placeBuild(which,i){ const def=G.build; if(!def)return;
  if(which==='center'&&isLane(i)){ setHint(`Build on the dark flanking slots — the glowing lanes are for monsters. <button onclick="cancelBuild()">cancel</button>`); render(); return; }
  if(cellArr('you',which)[i]||!placeRowOK('you',which,def)){ setHint(`That row can't support <b>${escHtml(def.nm)}</b> (needs ⚒ to spare). Pick another space. <button onclick="cancelBuild()">cancel</button>`); render(); return; }
  if(!canBuild('you',def)){ G.build=null; defaultHint(); render(); return; }
  payAny('you',def.c); cellArr('you',which)[i]=mkBld(def,'you');
  log(`<span class="y">You raise a ${escHtml(def.nm)}.</span>`,'y');
  G.build=null; afterDeploy('you'); defaultHint(); render(); checkWin(); }
