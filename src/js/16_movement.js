/* ---------- movement (once/turn; the three MIDDLE rows are contested — both sides may enter,
   neither may enter the opponent's back row) ---------- */
function moveChainOf(owner){ return owner==='you'?['youBack','youFront','center','foeFront']:['foeBack','foeFront','center','youFront']; }
// a real, standable slot exists everywhere on the side rows; the center only has slots at its lanes
function slotExists(w,i){ return i>=0&&i<SLOTS&&(w!=='center'||isLane(i)); }  // works for zone names AND global keys
// legal one-step destinations for `owner`: ONE square in any direction — sideways, forward, back,
// or diagonal (diagonals matter: they're how a creature in an even column reaches the center's lanes)
function adjCells(owner,key,i){ const out=[];
  for(const dj of [-1,1]){ const j=i+dj; if(slotExists(key,j)) out.push([key,j]); }   // lateral: one column either side
  const ch=moveChainOf(owner), k=ch.indexOf(key);
  for(const dk of [-1,1]){ const nk=(k>=0)?ch[k+dk]:null; if(!nk)continue;
    for(const dj of [-1,0,1]){ const j=i+dj; if(slotExists(nk,j)) out.push([nk,j]); } }  // one row along the chain: straight or diagonal
  return out;
}
function adjacentK(owner,k1,i1,k2,i2){ return adjCells(owner,k1,i1).some(([k,i])=>k===k2&&i===i2); }
// first free, deployable slot in an own row (center only has lane slots for creatures)
function freeDeploySlot(owner,which){ const a=cellArr(owner,which); if(!a)return -1;
  return a.findIndex((x,i)=>!x&&!(which==='center'&&!isLane(i))); }
// AI deploy preference: push the front toward the middle columns
function aiPickDeploySlot(owner,which){ const a=cellArr(owner,which); if(!a)return -1;
  const order=which==='center'?[3,1,5]:which==='front'?[3,4,2,5,1,6,0]:[2,4,3,1,5,0,6];
  for(const i of order){ if(i<SLOTS&&!a[i]&&slotExists(which,i)) return i; }
  return freeDeploySlot(owner,which); }
// a creature's move is spent once used — EXCEPT during upkeep, where a second forced move is
// allowed at the price of its other action (the second move taps it: two moves = its whole turn)
function moveSpent(c){ return !!c.moved && !(G.upkeep && !c.moved2 && !c.tapped); }
function canMoveCard(key,i){ const c=rowArr(key)[i]; if(!c||c.kind!=='creature'||c.owner!=='you'||moveSpent(c))return false; return adjCells('you',key,i).some(([k,j])=>!rowArr(k)[j]); }
// standee pose — a creature stands "up" when it can do something relevant this turn, lies "down" when it can't.
function canBlockNow(o){ return !!(o&&o.kind==='creature'&&!o.blocked); }   // summoning-sick may block; tapped may block once; the `blocked` flag gates it
function canActNow(o,key,i){
  if(!o||o.kind!=='creature'||o.worker) return true;         // non-creatures / workers have no up-down pose
  if(G.turn===o.owner){                                      // its controller's turn — can it move or attack?
    if(o.tapped) return false;
    if(!o.sick) return true;                                 // ready to attack (and maybe move)
    return !moveSpent(o) && adjCells(o.owner,key,i).some(([k,j])=>!rowArr(k)[j]);  // summoning-sick, but can still reposition
  }
  return canBlockNow(o);                                     // opponent's turn — still available as a blocker?
}
function moveBtn(key,i){ if(!canMoveCard(key,i))return ''; const c=rowArr(key)[i];
  return ` <button onclick="startMove('${key}',${i})">⤧ Move${(c&&c.moved)?' again (taps it)':''}</button>`; }
window.startMove=(key,i)=>{ if(G.turn!=='you'||G.busy||G.over)return; i=+i; if(!canMoveCard(key,i))return; G.moveFrom={k:key,i}; G.sel=null; G.atk=[]; G.cardMenu=null; G.moveMana=null;
  const c=rowArr(key)[i];
  setHint((c&&c.moved)?'Second move this upkeep — it will <b>tap</b> the creature (both actions spent). Tap an open space one square away, or tap the unit again to cancel.'
    :'Tap an open space one square away — sideways, forward, back, or diagonal (never the enemy back row). Tap the unit again to cancel.'); render(); };
window.cancelMove=()=>{ G.moveFrom=null; if(G.upkeep)upkeepHint(); else defaultHint(); render(); };
function doMove(toK,toI){
  if(!G.moveFrom)return; const {k,i}=G.moveFrom; const c=rowArr(k)[i];
  if(!c||c.kind!=='creature'||moveSpent(c)){ G.moveFrom=null; defaultHint(); render(); return; }
  if(rowArr(toK)[toI]||!adjacentK('you',k,i,toK,toI)){ setHint('Pick an open space one square away.'); return; }
  rowArr(k)[i]=null;
  if(c.moved){ c.moved2=true; c.tapped=true; }   // upkeep second move — spends the creature's whole turn
  else c.moved=true;
  rowArr(toK)[toI]=c;
  log(`<span class="y">${c.nm} repositions to ${rowName(toK)}${c.moved2?' — its turn is spent':''}.</span>`,'y');
  G.moveFrom=null; syncWorkers('you');
  if(G.upkeep){ upkeepNext(); } else { defaultHint(); render(); }
}
function attackerRowKey(){ return G.atk.length?G.atk[0].k:'youFront'; }
function doAttack(tgtKey,ti){
  const attackers=selCres().filter(x=>!x.worker&&!x.sick&&!x.tapped);
  if(!attackers.length){clearAtk();render();return;}
  const aCol=G.atk.length?G.atk[0].i:ti;                 // the attacker's column governs reach
  const tIdx=rowIdx(tgtKey); const aIdx=rowIdx(attackerRowKey());
  const tgt=unitAt(tgtKey,ti); if(!tgt){clearAtk();render();return;}
  attackers.forEach(a=>a.tapped=true);
  const scour=groupIsScour(attackers);                   // Wind fliers ignore interceptors
  dischargeOvercharge(attackers);                         // Electric attackers spend their banked ◆
  // Not adjacent → the strike travels; the enemy may interpose units in any intervening row within ±1 column.
  if(!scour && Math.abs(aIdx-tIdx)>1){
    const elig=eligibleInterceptors('you',aIdx,tIdx,aCol);
    const chosen=aiChooseInterceptors(attackers,{kind:tgt.kind,cc:!!tgt.cc,elig});
    if(chosen.length){
      chosen.forEach(r=>{r.c.tapped=true;r.c.blocked=true;});
      log(`<span class="e">The enemy interposes ${chosen.length===1?chosen[0].c.nm:chosen.length+' interceptors'} — your strike is met midway, the target spared.</span>`,'e');
      resolveCombat(attackers,chosen.map(r=>r.c)); clearDischarge(attackers); clearAtk(); render(); checkWin(); return;
    }
  }
  if(tgt.kind==='creature'||tgt.kind==='building') springAttackTrap('foe',attackers,tgt); // foe's attack-trigger trap
  if(tgt.kind==='charge'){ provokeFaceDown('foe',tgtKey,ti,attackers); }
  else if(tgt.kind==='trap'){ springTrap('foe',tgtKey,ti,attackers); }
  else if(tgt.kind==='building'){ log(`<span class="y">You strike the enemy ${tgt.nm}.</span>`,'y'); clashFx(attackers,[tgt]); applyDmg(focusFire(attackers,[tgt])); cleanup(); }
  else { log(`<span class="y">You attack ${tgt.nm} with ${attackers.length} creature(s).</span>`,'y'); resolveCombat(attackers,[tgt]); }
  if(scour && attackers[0]){ scourStrike(attackers[0],'foe'); cleanup(); }
  clearDischarge(attackers);
  clearAtk(); render(); checkWin();
}
// strike an OPEN column of the enemy back row — the stronghold itself. Same row-distance /
// interception rules as any traveling attack; undefended, the blow drains the defender's life pool.
function attackBackRow(defOwner,col){
  const attackers=selCres().filter(x=>!x.worker&&!x.sick&&!x.tapped);
  if(!attackers.length){clearAtk();render();return;}
  const aCol=G.atk.length?G.atk[0].i:col;
  const tgtKey=defOwner==='foe'?'foeBack':'youBack'; const tIdx=rowIdx(tgtKey); const aIdx=rowIdx(attackerRowKey());
  attackers.forEach(a=>a.tapped=true);
  const scour=groupIsScour(attackers);
  dischargeOvercharge(attackers);
  if(!scour&&Math.abs(aIdx-tIdx)>1){
    const elig=eligibleInterceptors('you',aIdx,tIdx,aCol);
    const chosen=aiChooseInterceptors(attackers,{kind:'base',cc:true,elig});
    if(chosen.length){
      chosen.forEach(r=>{r.c.tapped=true;r.c.blocked=true;});
      log(`<span class="e">The enemy interposes ${chosen.length===1?chosen[0].c.nm:chosen.length+' interceptors'} — your strike at the stronghold is met midway.</span>`,'e');
      resolveCombat(attackers,chosen.map(r=>r.c)); clearDischarge(attackers); clearAtk(); render(); checkWin(); return;
    }
  }
  const dmg=sumA(attackers);
  G.P[defOwner].life=Math.max(0,G.P[defOwner].life-dmg);
  log(`<span class="y">You breach the enemy line — ⚔${dmg} strikes the stronghold! (♥${G.P[defOwner].life} remains)</span>`,'y');
  if(scour&&attackers[0]){ scourStrike(attackers[0],defOwner); cleanup(); }
  clearDischarge(attackers);
  clearAtk(); render(); checkWin();
}
/* The player chooses interceptors when the AI's strike travels through rows they hold. Refs are {key,i}. */
function askBlock(opts){
  return new Promise(resolve=>{
    const elig=(opts.elig||[]).map((r,idx)=>({ref:r,idx,c:r.c||unitAt(r.key,r.i)})).filter(x=>x.c);
    if(!elig.length){resolve([]);return;}
    const sel=new Set(); const A=opts.attacker.a;
    const box=$('contestPanel').querySelector('.box');
    box.innerHTML=`<div class="ptitle" style="color:var(--tide)">${opts.title||'Incoming Attack'}</div>`+
      `<div class="pmeta" style="margin-bottom:6px;color:var(--ink)">${opts.desc||''}</div>`+
      `<div class="pmeta" style="margin-bottom:8px;font-style:italic;opacity:.7">Interpose untapped units from a single intervening row — they clash with the attacker and the original target is spared.</div>`+
      `<div class="pmeta" id="bkMeta"></div><div class="cgrid" id="bkGrid"></div>`+
      `<div class="pacts"><button class="flip" id="bkGo"></button></div>`+
      `<button class="pclose" id="bkPass">Let it through</button>`;
    const grid=box.querySelector('#bkGrid'); const btns=[];
    elig.forEach(e=>{
      const c=e.c;
      const b=document.createElement('button'); b.className='cbtn '+(c.worker?'vil':(clsOf[c.color]||'crt'));
      b.innerHTML=`<div class="nm">${c.worker?'⚒ Minion':c.nm}</div><div class="stats"><span class="atk">⚔${c.a}</span><span class="hp">♥${c.h}</span></div><div style="font-size:9px;opacity:.55">${rowName(e.ref.key)}</div>`;
      b.addEventListener('click',()=>{
        if(sel.has(e.idx)){sel.delete(e.idx);b.classList.remove('bon');}
        else{ if(sel.size){ const lockRow=elig.find(x=>x.idx===[...sel][0]).ref.key; if(e.ref.key!==lockRow)return; } sel.add(e.idx); b.classList.add('bon'); }
        upd();
      });
      btns.push({b,row:e.ref.key}); grid.appendChild(b);
    });
    const go=box.querySelector('#bkGo'), pass=box.querySelector('#bkPass'), meta=box.querySelector('#bkMeta');
    function chosen(){return [...sel].map(ix=>elig.find(x=>x.idx===ix));}
    function upd(){
      const lockRow=sel.size?elig.find(x=>x.idx===[...sel][0]).ref.key:null;
      btns.forEach(({b,row})=>{const cross=lockRow&&row!==lockRow;b.disabled=cross;b.style.opacity=cross?'.32':'';});
      const D=chosen().reduce((s,e)=>s+(e.c?e.c.a:0),0);
      go.textContent='Interpose '+sel.size+(sel.size?` (deal ⚔${D})`:''); go.disabled=sel.size===0;
      meta.textContent=`your interceptors ⚔${D} · incoming ⚔${A}`+(lockRow?` · ${rowName(lockRow)}`:'');
    }
    let _bkTo=null,_bkCt=null;
    function close(){$('contestPanel').style.display='none'; if(_bkTo){clearTimeout(_bkTo);_bkTo=null;} if(_bkCt){clearInterval(_bkCt);_bkCt=null;}}
    go.addEventListener('click',()=>{const r=chosen().map(e=>({key:e.ref.key,i:e.ref.i,c:e.c}));close();resolve(r);});
    pass.addEventListener('click',()=>{close();resolve([]);});
    upd(); $('contestPanel').style.display='flex';
    if(opts.ms>0){ let left=Math.ceil(opts.ms/1000);   // MP: a visible deadline so the remote host's auto-pass never silently discards a block
      const paint=()=>{ pass.textContent='Let it through ('+left+'s)'; };
      paint(); _bkCt=setInterval(()=>{ if(--left<=0){clearInterval(_bkCt);_bkCt=null;} else paint(); },1000);
      _bkTo=setTimeout(()=>{ close(); resolve([]); },opts.ms); }
  });
}

function applyRes(base,owner,creature,type){   // deposit harvested mana into the single generic pool (player + AI)
  const P=G.P[owner]; P.mana=Math.min(99,P.mana+base);   // in-turn cap only — unspent mana drains at end of turn (vaults keep their share)
  log(`&nbsp;&nbsp;<span class="s">+${base} ◆.</span>`,'s'); P.firstExtract=false;
}

/* commander crest, passives, and once-per-game powers removed — the deck does the talking (v19) */

// Settling loop: remove the dead, fire each creature's death keyword (its cell already freed), then
// re-sweep so chained kills (Detonate, etc.) resolve in one pass. The command center is never swept —
// checkWin ends the duel instead of graving the keep.
function cleanup(){
  let any=true, guard=0;
  while(any && guard++<40){
    any=false;
    ROWS.forEach(key=>{const b=rowArr(key);
      for(let i=0;i<SLOTS;i++){const c=b[i];
        if(c&&(c.kind==='creature'||c.kind==='building')&&c.h<=0){const o=c.owner;   // fronts are contested — attribute by the unit's own tag
          log(`&nbsp;&nbsp;${o==='you'?'Your':'Their'} ${c.nm} ${c.kind==='building'?'is razed':'falls'}.`);
          b[i]=null; if(c.kind==='creature'&&!c.worker) onCreatureDeath(c,o); toGrave(o,c); any=true; }}});
    ['you','foe'].forEach(o=>['back','front','center'].forEach(w=>{const pool=G.P[o].min[w];
      for(let i=pool.length-1;i>=0;i--){if(pool[i].h<=0){toGrave(o,pool[i]);pool.splice(i,1);any=true;}}}));
  }
}

