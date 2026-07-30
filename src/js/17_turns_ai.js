/* ---------- turns ---------- */
function buildingUpkeep(owner){
  const P=G.P[owner]; let revived=false;
  const tick=o=>{ if(o&&o.kind==='building'){
    if(o.eff==='mana'){P.mana=Math.min(99,P.mana+o.val);log(`<span class="${owner==='you'?'y':'e'}">${o.nm} yields ◆${o.val}.</span>`,owner==='you'?'y':'e');}
    else if(o.eff==='damage'){buildingDamage(owner,o.val||0,o.nm);}
    else if(o.eff==='revive'){ if(!revived) revived=reviveFromGrave(owner); } // once per turn, regardless of count
  }};
  ['front','back'].forEach(w=>P[w].forEach(tick));
  G.center.forEach(o=>{ if(o&&o.owner===owner) tick(o); });
}
// Reliquary: return the most recently fallen non-token creature from the grave to its owner's hand
function reviveFromGrave(owner){
  const g=G.P[owner].grave;
  for(let k=g.length-1;k>=0;k--){ const r=g[k];
    if(r&&r.type==='creature'&&!r.token){ g.splice(k,1);
      G.P[owner].hand.push({kind:'handcard',id:uid++,type:'creature',color:r.color||G.P[owner].color,nm:r.nm,a:r.a,h:r.h,c:r.c,fs:r.fs,up:r.up,art:r.art,
        kw:r.kw,det:r.det,ward:r.ward,wardhp:r.wardhp,reap:r.reap,grow:r.grow,hatch:r.hatch,into:r.into,entrench:r.entrench,tribe:r.tribe,subtype:r.subtype});
      log(`<span class="${owner==='you'?'y':'e'}">The Reliquary returns ${r.nm} to ${owner==='you'?'your':'their'} hand.</span>`,owner==='you'?'y':'e');
      return true; }
  }
  return false;
}
// a damage structure (tower) strikes the nearest enemy creature at the start of its owner's turn
function buildingDamage(owner,val,nm){
  if(val<=0)return; const foe=owner==='you'?'foe':'you';
  let tgt=null;
  for(const w of ['front','center','back']){ const arr=w==='center'?G.center:G.P[foe][w];
    for(const x of arr){ if(x&&x.owner===foe&&x.kind==='creature'&&!x.worker){ tgt=x; break; } } if(tgt)break; }
  if(tgt){ tgt.h-=val; log(`<span class="${owner==='you'?'y':'e'}">${nm} fires for ${val} — enemy ${tgt.nm} is hit.</span>`,owner==='you'?'y':'e'); }
}
// mana-storage structures (vaults) keep up to their total `val` in generic mana across the drain
function vaultCap(owner){ return ownUnits(owner).filter(o=>o.kind==='building'&&o.eff==='vault').reduce((s,o)=>s+(o.val||0),0); }
function drainMana(owner){ const P=G.P[owner]; const cap=vaultCap(owner); const lost=Math.max(0,P.mana-cap);
  P.mana=Math.min(P.mana,cap); // vaults keep up to their cap; the rest drains
  return {keep:P.mana,lost}; }
// end-of-turn drain with its log line — unspent mana evaporates except what the vaults hold
function endTurnDrain(owner){ const {keep,lost}=drainMana(owner); const cls=owner==='you'?'y':'e';
  if(lost>0) log(`<span class="${cls}">◆${lost} unspent mana drains away${keep>0?` — ${owner==='you'?'your':'their'} vaults keep ◆${keep}`:''}.</span>`,cls);
  else if(keep>0) log(`<span class="${cls}">${owner==='you'?'Your':'Their'} vaults keep ◆${keep} through the turn.</span>`,cls);
}
// ----- PHASES: upkeep → draw → action (combat is a sub-phase of action) → end -----
const PHASE_ORDER=['upkeep','draw','action','end'];
const PHASE_LABEL={draw:'Draw',upkeep:'Upkeep',action:'Action',combat:'Combat',end:'End'};
function setPhase(p){ G.phase=p; G.upkeep=(p==='upkeep'); }
function acting(){ return G.turn==='you'&&!G.busy&&!G.over&&G.phase==='action'; }  // when the player may summon / move / attack / build
// the phase shown in the tracker: while attackers are declared during the action phase we're in the Combat sub-phase
function shownPhase(){ return (G.phase==='action'&&(G.atk.length||(G.decls&&G.decls.length))) ? 'combat' : G.phase; }
function startTurn(owner){
  G.turnNo++; G.turn=owner;const P=G.P[owner]; G.cardMenu=null; G.moveMana=null; G.decls=[];
  P.firstExtract=true; // no generic income — mana comes from worker harvest + forge yields (all colored)
  P.upaid={back:0,front:0,center:0,raid:0};   // last turn's keep payments expire — shortfalls are settled anew each upkeep
  ownUnits(owner).forEach(o=>{if(o.kind==='creature'){o.sick=false;o.tapped=false;o.moved=false;o.moved2=false;o.paid=false;o.blocked=false;o._dis=0;}});
  chrysalisUpkeep(owner);   // cocoons swell (and re-sick so they can't attack) or hatch into their grown form
  overchargeUpkeep(owner);  // Electric creatures bank ◆ for their next discharge
  buildingUpkeep(owner);
  cleanup(); // sweep anything a damage-tower just killed
  syncWorkers(owner); // workers re-derived from the cards now in each row
  readyWorkers(owner); // settle this turn's workers so they can harvest (workers a later build adds stay sick)
  if(owner==='you'){
    setPhase('upkeep');   // the player's turn opens at Upkeep — balance the workforce + ⛏ Harvest, then Draw
    log(`<span class="y">— Your turn · Upkeep — settle any shortfall (Move / Pay / Sacrifice), then ⛏ Harvest —</span>`);
    upkeepHint();
    { const off=upkeepOffender(); if(off) upkeepPick(off.key,off.i); }   // pop the Move/Pay/Sacrifice menu on the first over-extended creature
  } else if(typeof MPNET!=='undefined'&&MPNET.active&&MP.started){
    setPhase('upkeep');   // MP: the remote player drives 'foe' through upkeep/draw/action via intents — no AI
  } else {
    drawCard('foe');    // the AI draws automatically at the start of its turn
    aiFixDeficit('foe'); readyWorkers('foe'); // re-settle after the AI balances any negative rows
  }
}
function drawHint(){ setHint(`<b>Draw phase.</b> Click your <b>deck</b> to draw a card and begin your Action phase.`); }
// clicking the player's deck: draw during the draw phase, otherwise browse it
window.youDeckClick=function(){
  if(G.turn==='you'&&!G.busy&&!G.over&&G.phase==='draw'){ doDraw(); return; }
  openViewer('deck','you');
};
function doDraw(){
  if(G.turn!=='you'||G.busy||G.over||G.phase!=='draw')return;
  if(G.P.you.deck.length){ drawCard('you'); log(`<span class="y">You draw a card.</span>`,'y'); }
  else log(`<span class="y">Your deck is empty — nothing to draw.</span>`,'y');
  setPhase('action');   // draw done → the Action phase begins
  defaultHint(); render();
}
// ----- UPKEEP (start of your turn): every over-extended creature must be settled EXPLICITLY —
//       ⤧ Move (spends its actions; a 2nd move also taps), ◆ Pay its keep, or ✖ Sacrifice.
//       ⛏ Harvest stays locked until every row is balanced. -----
function upkeepHint(){
  const dz=deficitRows('you'); const owe=totalDeficit('you');
  setHint(dz.length
    ? `<b style="color:#ff8a7a">Upkeep — shortfall ⚒${owe} (${dz.map(z=>rowName(zoneKey('you',z))).join(', ')}).</b> Settle each flagged creature — <b>⤧ Move</b> (spends its actions), <b>◆ Pay</b> its keep, or <b>✖ Sacrifice</b> — then ⛏ Harvest.`
    : `<b>Upkeep.</b> Reposition creatures now if you wish (spends their move), then press <b>⛏ Harvest</b> to collect mana and begin.`);
}
// the next creature that still owes keep (highest upkeep first, in the first short zone)
function upkeepOffender(){
  for(const z of ZONES){ if(zoneDeficit('you',z)<=0)continue;
    const cres=creaturesInRow('you',z).filter(r=>!r.o.paid).sort((a,b)=>(b.o.up||0)-(a.o.up||0));
    if(cres.length)return cres[0];
  } return null;
}
// shortfall living in zones with NO settle-able creature (e.g. a tower whose support was razed) —
// nothing to move or sacrifice there, so ⛏ Harvest is allowed to pay this portion directly
function orphanDeficit(owner){
  return ZONES.reduce((s,z)=>{ if(zoneDeficit(owner,z)<=0)return s;
    const cres=creaturesInRow(owner,z).filter(r=>!r.o.paid);
    return s+(cres.length?0:zoneDeficit(owner,z)); },0);
}
// after each settle (move / pay / sacrifice): refresh the hint and pop the menu on the next offender
function upkeepNext(){
  if(!G.upkeep||G.turn!=='you'){ render(); return; }
  upkeepHint();
  const off=upkeepOffender();
  if(off&&!G.moveFrom){ upkeepPick(off.key,off.i); return; }   // upkeepPick renders
  render();
}
window.upkeepPick=function(key,i){
  if(!G.upkeep)return;
  const o=rowArr(key)[i]; if(!o||o.kind!=='creature'||o.owner!=='you')return;
  const z=zoneForRow('you',key); const owe=z?zoneDeficit('you',z):0;
  const payN=Math.min(o.up||0,owe);
  const pay=(payN>0&&!o.paid)?`<button onclick="upkeepPay('${key}',${i})"${manaTotal('you')<payN?' disabled title="not enough mana"':''}>◆ Pay ${payN}</button>`:'';
  const hint=owe>0?`shortfall ⚒${owe} here — move, pay, or sacrifice`:'upkeep — reposition or sacrifice';
  G.cardMenu={k:key,i,html:`${moveBtn(key,i)}${pay}<button class="set" onclick="upkeepSac('${key}',${i})">✖ Sacrifice</button><span class="taphint">${hint}</span>`};
  render();
};
// ◆ Pay: cover this creature's share of the row's shortfall from your banked mana — it holds its post
window.upkeepPay=function(key,i){
  if(!G.upkeep||G.turn!=='you'||G.busy||G.over)return;
  const o=rowArr(key)[+i]; if(!o||o.kind!=='creature'||o.owner!=='you'||o.paid)return;
  const z=zoneForRow('you',key); if(!z)return;
  const cost=Math.min(o.up||0,zoneDeficit('you',z));
  if(cost<=0){ G.cardMenu=null; upkeepNext(); return; }
  if(manaTotal('you')<cost){ setHint(`<b style="color:#ff8a7a">Its keep is ◆${cost} — you have ◆${manaTotal('you')}.</b> Move or sacrifice it instead.`); render(); return; }
  payAny('you',cost); G.P.you.upaid[z]=(G.P.you.upaid[z]||0)+cost; o.paid=true;
  log(`<span class="y">You pay ◆${cost} to keep ${o.nm} fed at its post.</span>`,'y');
  G.cardMenu=null; upkeepNext();
};
window.upkeepSac=function(key,i){
  if(!G.upkeep||G.turn!=='you'||G.busy||G.over)return;
  const o=rowArr(key)[i]; if(!o||o.kind!=='creature'||o.owner!=='you')return;
  rowArr(key)[i]=null; toGrave('you',o);
  log(`<span class="y">${o.nm} is sacrificed to ease the workforce.</span>`,'y');
  G.cardMenu=null; syncWorkers('you'); upkeepNext();
};
// ⛏ Harvest: only once every shortfall is settled — then every settled worker extracts
// automatically into the single generic mana pool — no colour choice — and the turn advances to Draw.
window.doHarvest=function(){
  if(G.phase!=='upkeep'||G.turn!=='you'||G.busy||G.over)return;
  const owe=totalDeficit('you');
  if(owe>0){   // no silent auto-pay — each creature is settled by an explicit Move / Pay / Sacrifice
    const off=upkeepOffender();
    if(off){ setHint(`<b style="color:#ff8a7a">Shortfall ⚒${owe} unsettled.</b> Move, pay, or sacrifice the flagged creatures first — then harvest.`); upkeepPick(off.key,off.i); return; }
  }
  let sum=0;
  for(const z of ['back','front','center']){
    const pool=minPool('you',z); const up=pool.filter(m=>!m.tapped&&!m.sick).length;
    if(up<=0)continue;
    const total=up*minYield(z);
    pool.forEach(m=>{ if(!m.sick) m.tapped=true; });
    G.P.you.mana=Math.min(99,G.P.you.mana+total); sum+=total;   // generic — no colour split
  }
  if(owe>0){   // purely STRUCTURAL shortfall (nothing left to move/pay/sac) — settled out of the
    // harvest proceeds so it can never dead-lock the turn; an unpayable remainder simply goes unpaid
    const pay=Math.min(owe,G.P.you.mana);
    if(pay>0)payAny('you',pay);
    ZONES.forEach(z=>{ const d=zoneDeficit('you',z); if(d>0)G.P.you.upaid[z]=(G.P.you.upaid[z]||0)+d; });
    log(pay>=owe?`<span class="y">You pay ◆${owe} to keep your unsupported works running.</span>`
               :`<span class="y">Your unsupported works cost ◆${owe} — you could only spare ◆${pay}; the crews idle unpaid.</span>`,'y');
  }
  setPhase('draw'); G.moveFrom=null; G.cardMenu=null;   // upkeep resolved → the Draw phase (click your deck)
  if(sum>0) log(`<span class="y">Harvest: ◆${sum} (total ◆${manaTotal('you')}).</span>`,'y');
  else log(`<span class="y">— No workers to harvest (◆${manaTotal('you')} banked) —</span>`,'y');
  drawHint(); render();
};
window.hvCancel=function(){ $('harvestPanel').style.display='none'; };
// ----- AI upkeep: rebalance by moving, then PAY the rest (from vaulted mana), sacrificing only when broke -----
const MOVE_ADJ={back:['front'],front:['back','center'],center:['front'],raid:['center']}; // zone graph ('raid' = the enemy rows)
function aiMoveCreature(owner,fromKey,i,toZ){          // fromKey is a GLOBAL row key — raid spans two rows now
  const arr=rowArr(fromKey); const o=arr&&arr[i]; if(!o)return false;
  if(o.moved&&(o.moved2||o.tapped))return false;       // two moves max — the same budget the player gets
  const dstKey=zoneKey(owner,toZ); const dst=rowArr(dstKey);
  let slot=-1; for(const j of [i,i-1,i+1]){ if(j>=0&&j<SLOTS&&!dst[j]&&slotExists(dstKey,j)&&adjacentK(owner,fromKey,i,dstKey,j)){slot=j;break;} }  // one REAL square, same as the player
  if(slot<0)return false;
  arr[i]=null;
  if(o.moved){ o.moved2=true; o.tapped=true; } else o.moved=true;   // a second forced move spends its turn
  dst[slot]=o; return true;
}
function aiFixDeficit(owner){
  let guard=0;
  while(deficitRows(owner).length&&guard++<40){       // 1) rebalance into rows that can absorb the upkeep
    const which=deficitRows(owner)[0];
    const cres=creaturesInRow(owner,which).sort((a,b)=>(b.o.up||0)-(a.o.up||0));
    if(!cres.length)break;
    const {key,i,o}=cres[0]; let moved=false;
    for(const to of (MOVE_ADJ[which]||[])){
      if(to==='raid')continue;                         // never rebalance INTO the enemy rows
      if(rowWorkers(owner,to)-(o.up||0)>=0&&aiMoveCreature(owner,key,i,to)){
        log(`<span class="e">${o.nm} repositions to ${rowName(zoneKey(owner,to))} to balance the workforce.</span>`,'e');
        moved=true; break;
      }
    }
    if(!moved)break;
    syncWorkers(owner);
  }
  guard=0;                                             // 2) sacrifice only while the bill is unaffordable
  while(totalDeficit(owner)>manaTotal(owner)&&guard++<40){
    const which=deficitRows(owner)[0]; if(!which)break;
    const cres=creaturesInRow(owner,which).sort((a,b)=>(b.o.up||0)-(a.o.up||0));
    if(!cres.length)break;
    const {key,i,o}=cres[0];
    rowArr(key)[i]=null; toGrave(owner,o);
    log(`<span class="e">The enemy sacrifices ${o.nm} — it cannot pay its keep.</span>`,'e');
    syncWorkers(owner);
  }
  const owe=totalDeficit(owner);                       // 3) pay what remains (recorded per zone, like the player's explicit Pay)
  if(owe>0&&manaTotal(owner)>=owe){ payAny(owner,owe);
    ZONES.forEach(z=>{ const d=zoneDeficit(owner,z); if(d>0)G.P[owner].upaid[z]=(G.P[owner].upaid[z]||0)+d; });
    log(`<span class="e">The enemy pays ◆${owe} to sustain its over-extended lines.</span>`,'e'); }
}
$('endBtn').addEventListener('click',endTurn);
// Train Worker removed — workers are now auto-derived from the cards in each row.
function endTurn(){
  if(G.turn!=='you'||G.busy||G.over)return;
  if(G.phase==='draw'){ drawHint(); render(); return; }              // must draw first
  if(G.phase==='upkeep'){ upkeepHint(); render(); return; }          // harvest first — the turn begins at ⛏
  if(G.phase!=='action')return;
  if(typeof CMB!=='undefined'&&CMB.hasDecls()){ CMB.hint(); render(); return; }   // resolve the declared combat first
  // ACTION → END phase: resolve any end-of-turn effects, then hand off to the opponent
  G.sel=null;G.atk=[];G.moveFrom=null;G.moveMana=null;
  setPhase('end'); log('<span class="y">— End phase —</span>','y');
  endPhaseEffects('you');
  endTurnDrain('you');   // unspent mana drains — vaults keep up to their capacity
  if(typeof MPNET!=='undefined'&&MPNET.active&&MP.started){    // MP: hand off to the remote player — no AI, no G.busy latch (cleared only in foeTurn, which never runs in MP)
    if(MP.role==='guest')MP.intent({a:'end'});
    startTurn('foe'); log('<span class="e">— Opponent\'s turn —</span>','e'); render();
    return;
  }
  G.busy=true; render();
  setTimeout(()=>{
    startTurn('foe');log('<span class="e">— Opponent\'s turn —</span>','e');render();
    setTimeout(foeTurn,650);
  },380);   // brief End-phase beat before the opponent acts
}
// end-of-turn effects hook — resolves anything that fires as the owner's turn ends (before the hand-off)
function endPhaseEffects(owner){ /* reserved for end-of-turn keyword triggers */ }
/* enumerate AI attackers (any untapped foe creature — the middle rows are all contested now) */
function aiAttackers(){ const out=[];
  ROWS.forEach(key=>rowArr(key).forEach((c,i)=>{ if(c&&c.owner==='foe'&&c.kind==='creature'&&!c.worker&&!c.sick&&!c.tapped) out.push({key,i}); }));
  return out;
}
/* everything of the player's that an attack could land on — anywhere on the board */
function yourFieldTargets(){ const out=[];
  ROWS.forEach(key=>rowArr(key).forEach((o,i)=>{ if(o&&o.owner==='you') out.push({key,i,o}); }));
  return out;
}
function aiPickTarget(m,aCol){
  const fld=yourFieldTargets();                          // columns never matter in combat — full board reach
  const ch=fld.filter(t=>t.o.kind==='charge'&&t.o.inv>=2).sort((a,b)=>b.o.inv-a.o.inv)[0];
  if(ch&&Math.random()<0.6) return ch;
  const kill=fld.filter(t=>t.o.kind==='creature'&&!t.o.worker&&m.a>=t.o.h).sort((a,b)=>a.o.h-b.o.h)[0];
  if(kill) return kill;
  const bld=fld.filter(t=>t.o.kind==='building').sort((a,b)=>a.o.h-b.o.h)[0];
  if(bld&&Math.random()<0.3) return bld;
  // otherwise storm the castle wall itself for life damage — interceptors may still meet the strike
  return {key:'youBack',i:Math.max(0,Math.min(SLOTS-1,aCol)),base:true,o:null};   // i is FX-only
}
async function foeTurn(){
  if(typeof MPNET!=='undefined'&&MPNET.active&&MP.started)return;   // MP: there is no AI — the remote player drives 'foe'
  const F=G.P.foe,Y=G.P.you;
  // fuel charges (front line + contested center)
  for(let i=0;i<SLOTS;i++){const ch=F.front[i];if(ch&&ch.owner==='foe'&&ch.kind==='charge'){const pour=Math.min(manaTotal('foe'),ch.card.c-ch.inv);payAny('foe',pour);ch.inv+=pour;if(ch.inv>=ch.card.c)flip('foe','foeFront',i);}}
  for(let i=0;i<SLOTS;i++){const ch=G.center[i];if(ch&&ch.owner==='foe'&&ch.kind==='charge'){const pour=Math.min(manaTotal('foe'),ch.card.c-ch.inv);payAny('foe',pour);ch.inv+=pour;if(ch.inv>=ch.card.c)flip('foe','center',i);}}
  // AUTOMATIC harvest — every settled worker extracts ◆1 (same in every row) into the generic pool
  for(const w of ['back','front','center']){
    const ups=F.min[w].filter(c=>!c.sick&&!c.tapped);
    if(!ups.length)continue;
    const total=ups.length*minYield(w);
    ups.forEach(c=>c.tapped=true);
    log(`<span class="e">Enemy workers harvest ◆${total} from ${w==='center'?'the center':('the '+w)}.</span>`,'e');
    applyRes(total,'foe',null); render();
  }
  cleanup();
  // build a structure sometimes
  // RTS building: the AI techs up from its commander's build menu (Foundry -> Forge -> ...), up to twice a turn
  if(aiBuild('foe')) aiBuild('foe');
  aiUpgrade('foe');                       // level an existing structure up in place when it can afford it
  // cast a raze spell on one of your structures
  let rzi=F.hand.findIndex(c=>c.type==='spell'&&c.effect==='raze'&&canPay('foe',c));
  if(rzi>=0){ let tk=null,ti=-1; ROWS.forEach(key=>rowArr(key).forEach((o,j)=>{if(o&&o.owner==='you'&&o.kind==='building'){tk=key;ti=j;}}));
    if(tk!==null){ const card=F.hand[rzi]; payCost('foe',card); F.hand.splice(rzi,1); resolveSpell(card,tk,ti); F.grave.push(spellRec(card)); render(); checkWin(); if(G.over)return; } }
  // burn your strongest soldier — wherever it stands
  let bni=F.hand.findIndex(c=>c.type==='spell'&&c.effect==='burn'&&canPay('foe',c));
  if(bni>=0){ let best=-1,tk=null,ti=-1;
    ROWS.forEach(key=>rowArr(key).forEach((o,j)=>{if(o&&o.owner==='you'&&o.kind==='creature'&&!o.worker&&o.a>best){best=o.a;tk=key;ti=j;}}));
    if(tk!==null){ const card=F.hand[bni]; payCost('foe',card); F.hand.splice(bni,1); resolveSpell(card,tk,ti); F.grave.push(spellRec(card)); render(); checkWin(); if(G.over)return; } }
  // arm a trap if it holds one, a slot is open, and it can afford the ◆1 set price
  { const tpi=F.hand.findIndex(c=>c.type==='spell'&&c.trap);
    if(tpi>=0&&manaTotal('foe')>=1){ let w='back',s=F.back.findIndex(x=>!x); if(s<0){w='front';s=F.front.findIndex(x=>!x);}
      if(s>=0){ const card=F.hand[tpi]; F.hand.splice(tpi,1); payAny('foe',1);
        G.P.foe[w][s]={kind:'trap',owner:'foe',w,card:{nm:card.nm,c:card.c,effect:card.effect,trigger:card.trigger,val:card.val,ic:card.ic,art:card.art,trap:true},setTurn:G.turnNo};
        log('<span class="e">Opponent sets a face-down card (◆1 placed on it).</span>','e'); render(); } } }
  // summon soldiers — sometimes pushing into the contested center to fight for it
  let guard=0;
  let cands=F.hand.map((c,i)=>({c,i})).filter(x=>x.c.type==='creature'&&canPay('foe',x.c)).sort((a,b)=>b.c.c-a.c.c);
  for(const {c} of cands){ if(guard++>6)break;
    const idx=F.hand.indexOf(c); if(idx<0||!canPay('foe',c))continue;
    let key='foeFront', empty=aiPickDeploySlot('foe','front');           // new cards enter only the AI's own back + front
    if(empty<0){ key='foeBack'; empty=aiPickDeploySlot('foe','back'); }
    if(empty<0)continue;
    payCost('foe',c);F.hand.splice(idx,1);const cr=mkCre(c,'foe',false);cr.sick=true;rowArr(key)[empty]=cr;
    log(`<span class="e">Opponent summons ${c.nm} (⚔${c.a}/♥${c.h}).</span>`,'e'); onCreatureEnter(cr,'foe'); syncWorkers('foe'); render();
    await playerTrapOnSummon(cr,whichOf(key),empty); if(G.over)return; }
  render();
  // attack — ALTERNATING DECLARATIONS: the AI declares every strike up front (visible), you assign
  // blockers with full information, retaliations are directed, then ALL damage lands at once.
  const declared=[];
  for(const atk of aiAttackers()){
    const m=unitAt(atk.key,atk.i); if(!m||m.tapped)continue;
    const tref=aiPickTarget(m,atk.i); if(!tref)continue;
    m.tapped=true;
    declared.push({m,a:{k:atk.key,i:atk.i},aIdx:rowIdx(atk.key),tIdx:tref.base?ROWS.length:rowIdx(tref.key),tref,blockers:[]});
    const nm=tref.base?'your CASTLE WALL':'your '+(tref.o.kind==='charge'?'face-down card':(tref.o.kind==='trap'?'set card':tref.o.nm));
    log(`<span class="e">⚔ ${m.nm} (⚔${effA(m)}/♥${m.h}) declares an attack on ${nm} from ${rowName(atk.key)}.</span>`,'e');
  }
  if(declared.length){
    render();
    // PAUSE-TO-RESPOND: one anti-tell priority window over the whole declaration set
    let springRef=await RESP.defendWindow('attack',{desc:`${declared.length} attack${declared.length===1?'':'s'} declared — you may assign blockers next.`});
    if(G.over)return;
    // your blocks, one declaration at a time (skip fliers and point-blank duels)
    let bn=0;
    for(const d of declared){ bn++;
      if(kwOf(d.m)==='scour'||d.aIdx===d.tIdx)continue;
      const elig=eligibleInterceptors('foe',d.aIdx,d.tIdx).filter(r=>r.c!==d.tref.o);   // the target itself fights back, it doesn't "block"
      if(!elig.length)continue;
      const tgtName=d.tref.base?'your CASTLE WALL':'your '+(d.tref.o.kind==='charge'?'face-down card':(d.tref.o.kind==='trap'?'set card':d.tref.o.nm));
      const blk=await askBlock({attacker:d.m,elig,title:`Incoming attack ${bn}/${declared.length}`,
        desc:`${d.m.nm} (⚔${effA(d.m)}/♥${d.m.h}) strikes from ${rowName(d.a.k)} toward ${tgtName}.`});
      if(G.over)return;
      blk.forEach(r=>{ const c=r.c||unitAt(r.key,r.i); if(c){ c.blocked=true; d.blockers.push({...r,c}); } });
      if(blk.length){ log(`<span class="y">You interpose ${blk.length} against ${d.m.nm}.</span>`,'y'); render(); }
    }
    // ——— simultaneous resolution (same engine as your own attacks) ———
    dischargeOvercharge(declared.map(d=>d.m));
    const blockedD=declared.filter(d=>d.blockers.some(r=>r.c&&r.c.h>0));
    const openD=declared.filter(d=>!blockedD.includes(d));
    for(const d of blockedD){                        // pair fights — the AI picks its absorber itself
      const blks=d.blockers.map(r=>r.c).filter(b=>b&&b.h>0); if(!blks.length)continue;
      let ab=0;
      if(blks.length>1){ const kill=blks.map((b,ix)=>({b,ix})).filter(x=>x.b.h<=effA(d.m)).sort((x,y)=>x.b.h-y.b.h)[0];
        ab=kill?kill.ix:blks.map((b,ix)=>({b,ix})).sort((x,y)=>y.b.h-x.b.h)[0].ix; }
      log(`<span class="y">Your interceptors meet ${d.m.nm} midway.</span>`,'y');
      await CMB.pairFight(d.m,d.blockers.filter(r=>r.c&&r.c.h>0),ab,d.a);
      if(G.over)return;
    }
    const byT=new Map();                             // unblocked strikes on your creatures, grouped: ONE retaliation each
    for(const d of openD){ const o=d.tref.o;
      if(!d.tref.base&&o&&o.kind==='creature'&&d.m.h>0){ if(!byT.has(o))byT.set(o,[]); byT.get(o).push(d); } }
    for(const [T,ds] of byT){
      const grp=ds.map(d=>d.m).filter(a=>a.h>0); if(!grp.length||T.h<=0)continue;
      if(springRef){ RESP.springAttackTrapRef('you',springRef,grp,T); springRef=null; }
      let ri=0;
      if(grp.length>1){ ri=await askRetaliate(T,grp); if(G.over)return; }
      log(`<span class="e">${grp.length===1?grp[0].nm+' attacks':grp.length+' enemies attack'} your ${T.nm}.</span>`,'e');
      await CMB.targetFight(grp,T,ri,rowCellEl($(ds[0].tref.key),ds[0].tref.i),ds.map(d=>d.a));
      if(G.over)return;
      ds.forEach(d=>{ if(kwOf(d.m)==='scour'&&d.m.h>0){ scourStrike(d.m,'you'); } }); cleanup();
    }
    let wallDmg=0; const scourHits=[];               // walls, structures, face-downs, traps
    for(const d of openD){ if(d.m.h<=0)continue; const o=d.tref.o;
      if(d.tref.base){ wallDmg+=effA(d.m); if(kwOf(d.m)==='scour')scourHits.push(d.m); continue; }
      if(o.kind==='creature')continue;               // fought above
      if(o.kind==='building'){ if(springRef){ RESP.springAttackTrapRef('you',springRef,[d.m],o); springRef=null; }
        log(`<span class="e">${d.m.nm} raids your ${o.nm}.</span>`,'e'); applyDmg(focusFire([d.m],[o])); cleanup(); }
      else if(o.kind==='charge'){ provokeFaceDown('you',d.tref.key,d.tref.i,[d.m]); }
      else if(o.kind==='trap'){ springTrap('you',d.tref.key,d.tref.i,[d.m]); }
      if(kwOf(d.m)==='scour'&&d.m.h>0)scourHits.push(d.m);
    }
    if(wallDmg>0){ G.P.you.life=Math.max(0,G.P.you.life-wallDmg);
      log(`<span class="e">The enemy storms your castle wall — ⚔${wallDmg}! (♥${G.P.you.life} remains)</span>`,'e');
      try{ ELEMFX.elemBurst(fxRect($('youCmd')),declared[0].m.color,true); FX.shake(); }catch(e){} }
    scourHits.forEach(a=>{ if(a.h>0)scourStrike(a,'you'); }); if(scourHits.length)cleanup();
    clearDischarge(declared.map(d=>d.m));
    render(); checkWin(); if(G.over)return;
  }
  cleanup();render();checkWin();
  if(G.over)return;
  endTurnDrain('foe');   // unspent mana drains — vaults keep up to their capacity
  setTimeout(()=>{G.busy=false;startTurn('you');render();},650);
}

function checkWin(){
  if(G.over)return;
  // the duel ends the moment a stronghold's life pool empties
  const youOut=G.P.you.life<=0;
  const foeOut=G.P.foe.life<=0;
  if(foeOut||youOut){
    G.over=true; const win=foeOut&&!youOut;
    $('bannerMsg').textContent=win?'VICTORY':'DEFEAT';
    $('bannerMsg').style.color=win?'var(--gold)':'#e35b4f';
    let sub=win?'The enemy stronghold has fallen.':'Your stronghold has fallen.';
    const bm=$('bannerMsg'); if(bm.nextElementSibling&&bm.nextElementSibling.className==='bsub')bm.nextElementSibling.remove();
    if(sub){ const d=document.createElement('div'); d.className='bsub'; d.style.cssText='font-size:14px;color:var(--ink);margin-top:6px;font-style:italic'; d.textContent=sub; bm.after(d); }
    $('banner').style.display='flex';
    if(typeof CAMPAIGN!=='undefined' && CAMPAIGN && CAMPAIGN.target!=null) campResolve(win);   // campaign duel → seize/lose the territory (id 0 is valid — test !=null), then route back to the map
  }
}
