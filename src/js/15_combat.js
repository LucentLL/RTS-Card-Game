/* ---------- combat (row-distance targeting · interception · First Strike · simultaneous, no spillover) ---------- */
function sumA(a){return a.reduce((s,c)=>s+(c.a||0),0);}
function whichOf(key){ return key==='center'?'center':(key.slice(-5)==='Front'?'front':'back'); }
// rows an attack CROSSES INTO: every row past the attacker's, up to and INCLUDING the target row
// (same row = none — a point-blank duel can't be interposed). tIdx may be a virtual WALL index
// (-1 beyond foeBack / ROWS.length beyond youBack): walls have no slots, so only real rows count.
function rowsCrossedInto(aIdx,tIdx){ const o=[];
  if(aIdx===tIdx) return o;
  const step=tIdx>aIdx?1:-1;
  for(let r=aIdx+step; r!==tIdx+step; r+=step) if(r>=0&&r<ROWS.length) o.push(ROWS[r]);
  return o; }
// creatures in `key` NOT owned by the attacker (enemy or contesting units) — eligible to interpose.
// COLUMNS NEVER MATTER IN COMBAT — any defender in a crossed row may block, whatever its column.
// A creature may block even tapped or summoning-sick; blocking is gated once-per-turn by `blocked`.
function untappedInterceptors(key,attackerOwner){ const out=[];
  rowArr(key).forEach((c,i)=>{ if(c&&c.kind==='creature'&&!c.blocked&&c.owner!==attackerOwner) out.push({key,i,c}); });
  // worker stacks screen their whole row
  minionsInRow(key).forEach(g=>{ if(g.owner!==attackerOwner&&!g.c.tapped&&!g.c.sick) out.push({key,c:g.c}); });
  return out;
}
function eligibleInterceptors(attackerOwner,aIdx,tIdx){ let out=[]; rowsCrossedInto(aIdx,tIdx).forEach(key=>{ out=out.concat(untappedInterceptors(key,attackerOwner)); }); return out; }

// Assign each dealer to ONE target (no spillover). Greedy lethal-first: secure as many kills as possible,
// then dump any leftover hitters onto the toughest target as chip damage. Returns Map<target, damage>.
function focusFire(dealers, targets){
  const dmg=new Map(); targets.forEach(t=>dmg.set(t,0));
  if(!targets.length) return dmg;
  const avail=[...dealers].filter(d=>effA(d)>0).sort((a,b)=>effA(b)-effA(a));
  const used=new Set();
  const order=[...targets].sort((a,b)=>a.h-b.h); // kill the cheapest-to-kill first → most kills
  for(const t of order){
    let need=t.h-dmg.get(t);
    if(need<=0)continue;
    // commit the SMALLEST set of unused hitters that still finishes this target, so big hitters stay
    // free for tougher targets (maximizes total kills); if nothing reaches lethal, commit none here.
    const free=avail.filter(d=>!used.has(d)).sort((a,b)=>effA(a)-effA(b)); // ascending: spend just enough
    const tryUse=[]; let n=need;
    for(const d of free){ if(n<=0)break; tryUse.push(d); n-=effA(d); }
    if(n<=0){ tryUse.forEach(d=>{ used.add(d); dmg.set(t,dmg.get(t)+effA(d)); }); }
  }
  // leftover hitters chip the toughest target
  const leftover=avail.filter(d=>!used.has(d));
  if(leftover.length){ const t=[...targets].sort((a,b)=>b.h-a.h)[0]; leftover.forEach(d=>dmg.set(t,dmg.get(t)+effA(d))); }
  return dmg;
}
function applyDmg(map){ map.forEach((d,t)=>{ t.h-=d; }); }

// Full simultaneous clash between two groups of CREATURES, honouring First Strike and one-target-per-unit.
function resolveCombat(groupA, groupB){
  applyUndertow(groupA, groupB); // Water defenders shove the strongest attacker back to hand (before any blows land)
  const live=arr=>arr.filter(c=>c&&c.h>0);
  // First Strike pre-step: FS units on both sides strike at once; anything killed here never strikes back.
  const aFS=groupA.filter(c=>c.fs), bFS=groupB.filter(c=>c.fs);
  if(aFS.length||bFS.length){
    const dA=focusFire(aFS, live(groupB)); const dB=focusFire(bFS, live(groupA));
    applyDmg(dA); applyDmg(dB);
  }
  // Main step: surviving NON-FS units strike simultaneously (FS units already struck and don't strike again).
  const mainA=groupA.filter(c=>!c.fs&&c.h>0);
  const mainB=groupB.filter(c=>!c.fs&&c.h>0);
  const dA=focusFire(mainA, live(groupB));
  const dB=focusFire(mainB, live(groupA));
  applyDmg(dA); applyDmg(dB);
  cleanup();
}
// thin compatibility shim
function resolveClash(attackers,blockers){ resolveCombat(attackers,blockers); }

// AI (as defender) decides whether to interpose against the player's strike.
function aiChooseInterceptors(attackers, info){
  const elig=info.elig||[]; if(!elig.length) return [];
  const P=info.power!=null?info.power:sumA(attackers);
  if(info.cc){ // defend the command center — throw bodies if the hit is real
    if(!(P>=G.P.foe.life || P>=4)) return [];
    const survivor=elig.filter(r=>r.c.h>P).sort((a,b)=>a.c.h-b.c.h)[0];
    if(survivor) return [survivor];
    return elig.sort((a,b)=>a.c.h-b.c.h).slice(0,2);
  }
  if(info.kind==='charge'){ // worth a body to save a funded face-down
    const survivor=elig.filter(r=>r.c.h>P).sort((a,b)=>a.c.h-b.c.h)[0];
    if(survivor) return [survivor];
  }
  return []; // otherwise let it land — don't trade to save a single creature
}

// A face-down card is ATTACKED → flips up. Under-funded = interrupted (destroyed, banked ◆ lost); funded = resolves and fights back.
function provokeFaceDown(defOwner, key, slot, attackers){
  const arr=rowArr(key); const o=arr[slot]; if(!o||o.kind!=='charge')return;
  const ey=defOwner==='you'?'y':'e', them=defOwner==='you'?'e':'y';
  if(o.inv < o.card.c){
    log(`<span class="${them}">The strike catches a half-formed card — interrupted! ◆${o.inv} banked is lost.</span>`,them);
    toGrave(defOwner,o); arr[slot]=null; cleanup(); return;
  }
  log(`<span class="${ey}">Provoked! ${defOwner==='you'?'Your':'Their'} face-down erupts to meet the attack.</span>`,ey);
  flip(defOwner, key, slot);                   // consumes cost, banks surplus, becomes a live unit
  const now=arr[slot];
  if(now&&now.kind==='creature'){ resolveCombat(attackers,[now]); }      // it swings back, simultaneous
  else if(now){ applyDmg(focusFire(attackers,[now])); cleanup(); }       // a structure just takes the hit
}
// A face-down TRAP is ATTACKED (or otherwise provoked) → springs on the attacking group.
function springTrap(defOwner, key, slot, attackers){
  const arr=rowArr(key); const t=arr[slot]; if(!t||t.kind!=='trap')return;
  const ey=defOwner==='you'?'y':'e'; const card=t.card;
  log(`<span class="${ey}">${card.nm} springs on the attacker${attackers.length>1?'s':''}!</span>`,ey);
  if(card.effect==='pitfall'){ const v=[...attackers].sort((a,b)=>b.a-a.a)[0]; if(v){ log(`&nbsp;&nbsp;${v.nm} is dragged down — destroyed.`); v.h=0; } }
  else if(card.effect==='burn'){ attackers.forEach(a=>a.h-=card.val); log(`&nbsp;&nbsp;${card.val} damage to ${attackers.length} attacker(s).`); }
  // thornmail has no creature defender when the trap card itself is struck — it simply fizzles
  G.P[defOwner].grave.push(spellRec(card)); arr[slot]=null; cleanup();
}
// A trigger:'attack' trap on defOwner's side springs the moment their line is struck (auto-resolves; it only helps the defender).
function springAttackTrap(defOwner,attackers,defender){
  const t=findArmedTrap(defOwner,'attack'); if(!t)return;
  const card=t.o.card; const ey=defOwner==='you'?'y':'e';
  log(`<span class="${ey}">${card.nm} springs as ${defOwner==='you'?'your':'their'} line is struck!</span>`,ey);
  if(card.effect==='thornmail'){ if(defender&&defender.kind==='creature'&&!defender.cc){ defender.a+=500; defender.maxh+=1000; defender.h+=1000; log(`&nbsp;&nbsp;${defender.nm} hardens to ⚔${defender.a}/♥${defender.h}.`); } }
  else if(card.effect==='burn'){ attackers.forEach(a=>{ if(a)a.h-=(card.val||0); }); log(`&nbsp;&nbsp;${card.val} damage to ${attackers.length} attacker(s).`); }
  G.P[defOwner].grave.push(spellRec(card)); cellArr(defOwner,t.w)[t.i]=null;
}

function doExtract(type){
  if(!canExtract()){setHint('Extract with a single creature.');return;}
  const s=G.atk[0]; const me=G.P.you[s.w][s.i];
  if(!me||me.kind!=='creature'){clearAtk();render();return;}
  if(me.sick){setHint('Summoning-sick — it can act next turn.');return;}
  if(me.tapped){setHint('Already tapped this turn.');return;}
  me.tapped=true;
  const base=extractYield(s.w);
  const where=s.w==='center'?'the contested center':(s.w==='front'?'the front line':'the base');
  log(`<span class="s">${me.nm} extracts from ${where}.</span>`,'s');
  applyRes(base,'you',me,type);
  clearAtk(); render(); checkWin();
}
function extractChoiceHTML(which,fn){
  const cols=extractColors('you',which);
  return ['gen',...cols].map(t=>`<button onclick="${fn}('${t}')">${manaGlyph(t)} ${t==='gen'?'Generic':cap(t)}</button>`).join(' ');
}
window.extractSel=()=>{ if(!(G.turn==='you'&&!G.busy&&!G.over&&canExtract()))return;
  const s=G.atk[0]; const cols=extractColors('you',s.w);
  if(!cols.length){doExtract('gen');return;}
  setHint(`Extract ◆${extractYield(s.w)} as — ${extractChoiceHTML(s.w,'doExtractAs')}`);
};
window.doExtractAs=t=>{ if(G.turn==='you'&&!G.busy&&!G.over&&canExtract())doExtract(t); };

/* ---------- minions (a per-row pool, not board cards) ---------- */
function minYield(which){return 1;}   // every row harvests the same — no front/center bonus
/* ---------- worker harvest: one tap harvests the whole row, a pop-up distributes the haul across colors ---------- */
let hv=null;
window.harvestRow=(which)=>{
  if(G.turn!=='you'||G.busy||G.over||G.deficit)return;
  const ready=minPool('you',which).filter(m=>!m.tapped&&!m.sick);
  if(!ready.length){setHint('No ready workers in this row.');return;}
  G.sel=null; G.atk=[]; G.moveFrom=null; G.moveMana=null; G.cardMenu=null; G.minSel=null;
  applyHarvest(which,null,ready.length*minYield(which));   // generic mana — harvest is automatic, no colour popup
};
function applyHarvest(which,alloc,total){
  const P=G.P.you;
  minPool('you',which).filter(m=>!m.tapped&&!m.sick).forEach(m=>m.tapped=true);
  P.mana=Math.min(99,P.mana+total);
  const where=which==='center'?'the contested center':(which==='front'?'the front line':'the base');
  log(`<span class="s">Workers harvest ${where} — ◆${total} (total ◆${manaTotal('you')}).</span>`,'s');
  P.firstExtract=false;
  hv=null; $('harvestPanel').style.display='none'; defaultHint(); render(); checkWin();
}
function inspectMinion(owner,which){
  const list=minPool(owner,which); const up=list.filter(m=>!m.tapped&&!m.sick).length;
  const where=which==='center'?'the contested center':(which==='front'?(owner==='you'?'your front line':'the enemy front'):(owner==='you'?'your base':'the enemy base'));
  const net=rowWorkers(owner,which);
  showInspect(`⚒ Workers · ${where}`,
    `This row's workers are <b>set by its cards</b>: every structure adds its <b>⚒ support</b>, every monster subtracts its <b>⚒ upkeep</b>. Here that nets to <b>${net}</b> worker${net===1?'':'s'} (${up} ready).<br><b>Harvester.</b> Tap to harvest the <b>whole row at once</b> — <b>◆1 per worker</b>, the same in every row — straight into your <b>generic</b> mana pool, automatically.<br>Workers are <b>not trained and do not move</b> — they simply mirror the cards in the row. They still screen the line: a worker can <b>intercept</b> a strike and can be <b>raided</b>.<br><i>If a row's monsters outweigh its support and it goes <b>negative</b>, you must move or sacrifice monsters at the <b>start of your turn</b> until it is 0 or positive.</i>`);
}
// player's attackers strike an enemy minion pool (a stack of 0/2 bodies)
const WELL2ROW={wellFoeBack:'foeBack',wellFoeFront:'foeFront',wellCenter:'center',wellYouFront:'youFront',wellYouBack:'youBack'};
function attackMinionStack(key,owner,which){
  if(WELL2ROW[key])key=WELL2ROW[key];
  const attackers=selCres().filter(x=>!x.worker&&!x.sick&&!x.tapped);
  if(!attackers.length){clearAtk();render();return;}
  const list=minPool(owner,which); if(!list.length){clearAtk();render();return;}
  const aIdx=rowIdx(attackerRowKey()), tIdx=rowIdx(key);
  attackers.forEach(a=>a.tapped=true);
  if(aIdx!==tIdx){
    const elig=eligibleInterceptors('you',aIdx,tIdx).filter(r=>!list.includes(r.c));   // the targeted stack can't screen itself
    const chosen=aiChooseInterceptors(attackers,{kind:'creature',elig});
    if(chosen.length){
      chosen.forEach(r=>{r.c.tapped=true;r.c.blocked=true;});
      log(`<span class="e">The enemy interposes ${chosen.length===1?chosen[0].c.nm:chosen.length+' interceptors'} — your strike is met midway.</span>`,'e');
      resolveCombat(attackers,chosen.map(r=>r.c)); clearAtk(); render(); checkWin(); return;
    }
  }
  log(`<span class="y">You strike the enemy Minions with ${attackers.length} creature(s).</span>`,'y');
  resolveCombat(attackers, list.slice());
  clearAtk(); render(); checkWin();
}

/* ═══════════ COMBAT v3 — alternating declarations · universal retaliation · simultaneous damage ═══════════
   The player declares one attacker + target at a time (a selected group = several declarations at
   once, a joint attack); the DEFENDER answers each declaration immediately with its blockers — the
   alternating step — then ⚔ Resolve lands ALL damage. Every attacked creature strikes back with its
   full power (that is retaliation, not blocking); each creature deals its damage to exactly ONE
   enemy; a blocked attacker hits ONE chosen blocker; walls and structures never strike back.
   MP still runs the single-shot legacy path (doAttack/attackBackRow/attackMinionStack). */
(function(){ const s=document.createElement('style'); s.id='combatV3CSS'; s.textContent=
  '.cell.declAtk{outline:2px solid #d4af37;outline-offset:-2px;}'+
  '.cell.declTgt{outline:2px solid #e35b4f;outline-offset:-2px;}'+
  '.cell.declBlk{outline:2px dashed #7fd0f5;outline-offset:-2px;}'+
  /* aiming: the enemy ♥ must be an easy thumb target — ghost the turn label that overlaps it,
     raise the foe command cluster above the board chrome, and pad the heart's hit area out */
  'body.targeting #turnLabel{opacity:.15;pointer-events:none;}'+
  'body.targeting .cmdzone.foe{z-index:46;}'+
  '.keephp.lifeaim{position:relative;padding:12px 16px;margin:-12px -16px;cursor:pointer;}'+
  /* placing: the hand strip overlaps the near rows on phones — every card but the selected one goes
     inert and dim so board taps land on the board; tapping the selected card still cancels */
  'body.placing .hc:not(.selected){pointer-events:none;opacity:.35;}';
  document.head.appendChild(s); })();
function inMPGame(){ return typeof MPNET!=='undefined'&&MPNET.active&&typeof MP!=='undefined'&&MP.started; }
// one router for every attack click-site: solo → declaration combat; MP → the legacy single-shot path
window.routeAttack=function(kind,a,b,c){
  if(inMPGame()){
    if(kind==='unit')doAttack(a,b); else if(kind==='wall')attackBackRow('foe',a); else attackMinionStack(a,b,c);
    return;
  }
  if(kind==='unit')CMB.declare('unit',a,b);
  else if(kind==='wall')CMB.declare('wall',null,null);
  else CMB.declare('workers',WELL2ROW[a]||a,null,c);
};
// fxLunge as a promise (no-op when the FX layer isn't loaded / elements are missing)
function lungeP(srcEls,tEl,col){ return new Promise(res=>{ try{
  if(typeof fxLunge==='function'&&tEl&&srcEls&&srcEls.length){ fxLunge(srcEls,tEl,res,col); return; }
}catch(e){} res(); }); }
const CMB={};
window.CMB=CMB;
CMB.hasDecls=()=>!!(G.decls&&G.decls.length);
CMB.hint=function(){ const n=G.decls.length;
  setHint(`<b>${n}</b> attack${n===1?'':'s'} declared — add more attackers and tap targets to join, then <button onclick="CMB.resolve()">⚔ Resolve combat</button>`); };
/* declare: each selected attacker commits to the tapped target; the AI answers with its blockers at once */
CMB.declare=function(kind,tk,ti,wWhich){
  if(G.turn!=='you'||G.busy||G.over||G.phase!=='action')return;
  const refs=G.atk.slice();
  const tgt=kind==='unit'?unitAt(tk,ti):null;
  if(kind==='unit'&&!tgt){clearAtk();render();return;}
  let any=false;
  refs.forEach(ref=>{
    const A=rowArr(ref.k)[ref.i];
    if(!A||A.kind!=='creature'||A.owner!=='you'||A.worker||A.sick||A.tapped)return;
    A.tapped=true; any=true;
    const d={a:ref,kind,tk,ti,wWhich,blockers:[]};
    G.decls.push(d);
    const nm=kind==='wall'?'the castle wall':(kind==='workers'?'the enemy workers':tgt.nm);
    log(`<span class="y">⚔ ${A.nm} declares an attack on ${nm}.</span>`,'y');
    // the defender's alternating answer — its blockers, committed and visible, Arena-style
    const aIdx=rowIdx(ref.k); const tIdx=kind==='wall'?-1:rowIdx(tk);
    if(kwOf(A)!=='scour'&&aIdx!==tIdx){
      const elig=eligibleInterceptors('you',aIdx,tIdx)
        .filter(r=>r.c!==tgt&&!(kind==='workers'&&minPool('foe',wWhich).includes(r.c)));
      const chosen=aiChooseInterceptors([A],{kind:kind==='wall'?'base':(tgt?tgt.kind:'creature'),cc:kind==='wall',elig,power:effA(A)});
      chosen.forEach(r=>{ r.c.blocked=true; d.blockers.push(r);
        log(`<span class="e">The enemy interposes ${r.c.worker?'a Minion':r.c.nm} against ${A.nm}!</span>`,'e'); });
    }
  });
  clearAtk();
  if(any)CMB.hint(); else defaultHint();
  render();
};
/* pair fight: a blocked attacker vs its gang — A's blow lands on ONE chosen blocker, every blocker
   strikes A back; First Strike blows land in a pre-tier; all blows inside a tier are simultaneous */
CMB.pairFight=async function(A,blkRefs,ab,aRef){
  const blks=blkRefs.map(r=>r.c||r).filter(b=>b&&b.h>0);
  if(!blks.length||!A||A.h<=0)return;
  blks.forEach(b=>{ b.tapped=true; });
  try{ const src=aRef?[rowCellEl($(aRef.k),aRef.i)].filter(Boolean):[];
    const t0=(blkRefs[0]&&blkRefs[0].key!=null&&blkRefs[0].i!=null)?rowCellEl($(blkRefs[0].key),blkRefs[0].i):null;
    await lungeP(src,t0,A.color); }catch(e){}
  const group=[A];
  applyUndertow(group,blks);                       // an Undertow warden may hurl A back to hand
  if(!group.length||A.h<=0){ cleanup(); render(); return; }
  const absorber=blks[Math.max(0,Math.min(ab||0,blks.length-1))];
  const dmg=new Map(); const hit=(u,d)=>dmg.set(u,(dmg.get(u)||0)+d);
  const tier=fs=>{
    if(!!A.fs===fs&&A.h>0&&absorber.h>0) hit(absorber,effA(A));
    blks.forEach(b=>{ if(!!b.fs===fs&&b.h>0&&A.h>0) hit(A,b.a); });
    dmg.forEach((d,u)=>u.h-=d); dmg.clear();
  };
  tier(true); tier(false);
  cleanup(); render();
};
/* target fight: unblocked joint attack on one creature — every attacker's blow lands on the target,
   the target retaliates against ONE chosen attacker; First Strike pre-tier, tiers simultaneous */
CMB.targetFight=async function(grp,T,ri,fxTo,srcRefs){
  applyUndertow(grp,[T]);                          // an Undertow target may hurl the strongest attacker away
  grp=grp.filter(a=>a&&a.h>0);
  if(!grp.length||!T||T.h<=0){ cleanup(); render(); return; }
  try{ const srcs=(srcRefs||[]).map(r=>rowCellEl($(r.k),r.i)).filter(Boolean);
    await lungeP(srcs,fxTo,grp[0]&&grp[0].color); }catch(e){}
  const back=grp[Math.max(0,Math.min(ri||0,grp.length-1))];
  const dmg=new Map(); const hit=(u,d)=>dmg.set(u,(dmg.get(u)||0)+d);
  const tier=fs=>{
    grp.forEach(a=>{ if(!!a.fs===fs&&a.h>0&&T.h>0) hit(T,effA(a)); });
    if(!!T.fs===fs&&T.h>0&&back.h>0) hit(back,T.a);
    dmg.forEach((d,u)=>u.h-=d); dmg.clear();
  };
  tier(true); tier(false);
  cleanup(); render();
};
/* resolve: all declarations land at once (the anti-tell response window runs first, as ever) */
CMB.resolve=function(){
  if(G.turn!=='you'||G.over||G.phase!=='action'||!CMB.hasDecls())return;
  const run=()=>{ CMB._resolveNow(); };
  if(typeof RESP!=='undefined'&&RESP.actingGate)RESP.actingGate('attack',run); else run();
};
CMB._resolveNow=async function(){
  const decls=G.decls; G.decls=[];
  G.busy=true;
  const live=decls.map(d=>({...d,A:rowArr(d.a.k)[d.a.i],tgt:d.kind==='unit'?unitAt(d.tk,d.ti):null}))
    .filter(x=>x.A&&x.A.kind==='creature'&&x.A.h>0);
  const attackers=live.map(x=>x.A);
  dischargeOvercharge(attackers);
  // partition FIRST: a blocked attacker stays blocked even if it kills its whole gang in the fight
  const blocked=live.filter(x=>x.blockers.some(r=>r.c&&r.c.h>0));
  const open=live.filter(x=>!blocked.includes(x));
  // 1) blocked declarations = pair fights; the ATTACKER (you) picks who eats each gang-blocked blow
  for(const x of blocked){
    const blks=x.blockers.map(r=>r.c).filter(b=>b&&b.h>0);
    if(!blks.length)continue;
    let ab=0;
    if(blks.length>1){ G.busy=false; ab=await askAbsorb(x.A,blks); G.busy=true; }
    await CMB.pairFight(x.A,x.blockers.filter(r=>r.c&&r.c.h>0),ab,x.a);
    if(G.over){G.busy=false;return;}
  }
  // 2) unblocked strikes on creatures, grouped by target — a joint attack draws ONE retaliation
  const byT=new Map();
  for(const x of open){ if(x.kind==='unit'&&x.tgt&&x.tgt.kind==='creature'&&x.A.h>0){
    if(!byT.has(x.tgt))byT.set(x.tgt,[]); byT.get(x.tgt).push(x); } }
  for(const [T,xs] of byT){
    const grp=xs.map(x=>x.A).filter(a=>a.h>0);
    if(!grp.length||T.h<=0)continue;
    springAttackTrap('foe',grp,T);                 // the foe's attack-trigger trap, as before
    log(`<span class="y">You attack ${T.nm} with ${grp.length} creature(s).</span>`,'y');
    await CMB.targetFight(grp,T,0,rowCellEl($(xs[0].tk),xs[0].ti),xs.map(x=>x.a));   // AI retaliation is auto (its own pick)
    if(G.over){G.busy=false;return;}
  }
  // 3) everything else unblocked: structures, face-downs, traps, worker stacks, the wall
  let wallDmg=0; const scourHits=[];
  for(const x of open){
    if(x.A.h<=0)continue;
    if(x.kind==='wall'){ wallDmg+=effA(x.A); if(kwOf(x.A)==='scour')scourHits.push(x.A); continue; }
    if(x.kind==='workers'){ log(`<span class="y">${x.A.nm} strikes the enemy Minions.</span>`,'y');
      resolveCombat([x.A],minPool('foe',x.wWhich).slice());
      if(kwOf(x.A)==='scour'&&x.A.h>0)scourHits.push(x.A); continue; }
    const o=x.tgt; if(!o)continue;
    if(o.kind==='creature'){ if(kwOf(x.A)==='scour'&&x.A.h>0)scourHits.push(x.A); continue; }   // fought above
    if(o.kind==='building'){ springAttackTrap('foe',[x.A],o);
      log(`<span class="y">You strike the enemy ${o.nm}.</span>`,'y');
      try{clashFx([x.A],[o]);}catch(e){} applyDmg(focusFire([x.A],[o])); cleanup(); }
    else if(o.kind==='charge'){ provokeFaceDown('foe',x.tk,x.ti,[x.A]); }
    else if(o.kind==='trap'){ springTrap('foe',x.tk,x.ti,[x.A]); }
    if(kwOf(x.A)==='scour'&&x.A.h>0)scourHits.push(x.A);
  }
  if(wallDmg>0){
    G.P.foe.life=Math.max(0,G.P.foe.life-wallDmg);
    log(`<span class="y">You storm the castle wall — ⚔${wallDmg} strikes the enemy stronghold! (♥${G.P.foe.life} remains)</span>`,'y');
    try{ ELEMFX.elemBurst(fxRect($('foeCmd')),attackers[0]&&attackers[0].color,true); FX.shake(); }catch(e){}
  }
  scourHits.forEach(a=>{ if(a.h>0)scourStrike(a,'foe'); }); if(scourHits.length)cleanup();
  clearDischarge(attackers);
  G.busy=false;
  defaultHint(); render(); checkWin();
};

