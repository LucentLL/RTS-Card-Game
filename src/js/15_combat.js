/* ---------- combat (row-distance targeting · interception · First Strike · simultaneous, no spillover) ---------- */
function sumA(a){return a.reduce((s,c)=>s+(c.a||0),0);}
function whichOf(key){ return key==='center'?'center':(key.slice(-5)==='Front'?'front':'back'); }
function rowsStrictlyBetween(aIdx,tIdx){ const lo=Math.min(aIdx,tIdx),hi=Math.max(aIdx,tIdx),o=[]; for(let r=lo+1;r<hi;r++) if(r>=0&&r<ROWS.length) o.push(ROWS[r]); return o; }
// untapped creatures in `key` NOT owned by the attacker (enemy or contesting-center units) — eligible to interpose
function untappedInterceptors(key,attackerOwner,aCol){ const out=[];
  // a slotted defender may interpose only if its column is within ±1 of the attacker's column
  // a creature may block on the opponent's turn even if it tapped (attacked) or is summoning-sick — summoning sickness only bars ATTACKING; blocking is gated once-per-turn by the `blocked` flag
  rowArr(key).forEach((c,i)=>{ if(c&&c.kind==='creature'&&!c.blocked&&c.owner!==attackerOwner&&(aCol==null||colReach(i,aCol))) out.push({key,i,c}); });
  // worker stacks have no column — they screen their whole row
  minionsInRow(key).forEach(g=>{ if(g.owner!==attackerOwner&&!g.c.tapped&&!g.c.sick) out.push({key,c:g.c}); });
  return out;
}
function eligibleInterceptors(attackerOwner,aIdx,tIdx,aCol){ let out=[]; rowsStrictlyBetween(aIdx,tIdx).forEach(key=>{ out=out.concat(untappedInterceptors(key,attackerOwner,aCol)); }); return out; }

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
  const aIdx=rowIdx(attackerRowKey()), tIdx=rowIdx(key); const aCol=G.atk.length?G.atk[0].i:0;
  attackers.forEach(a=>a.tapped=true);
  if(Math.abs(aIdx-tIdx)>1){
    const elig=eligibleInterceptors('you',aIdx,tIdx,aCol);
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

