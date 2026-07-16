/* ---------- spells & traps ---------- */
function resolveSpell(card,key,i){
  const arr=rowArr(key); const o=arr[i]; if(!o)return false;
  const towner=o.owner;
  const ownerWord=towner==='you'?'your':'the enemy';
  if(card.effect==='burn'){
    if(o.kind==='charge'){ log(`<span class="${towner==='you'?'e':'y'}">${card.nm} bursts ${ownerWord} face-down card!</span>`,towner==='you'?'e':'y'); toGrave(towner,o); arr[i]=null; }
    else { o.h-=card.val; log(`<span class="${towner==='you'?'e':'y'}">${card.nm} sears ${o.nm} for ${card.val}.</span>`,towner==='you'?'e':'y'); }
  } else if(card.effect==='raze'){
    if(o.kind!=='building')return false;
    log(`<span class="${towner==='you'?'e':'y'}">${card.nm} brings down ${ownerWord} ${o.nm}!</span>`,towner==='you'?'e':'y'); toGrave(towner,o); arr[i]=null;
  } else if(card.effect==='chain'){
    if(o.kind!=='creature'||o.worker)return false;
    const caster=enemyOf(towner);
    const cres=liveEnemyCreatures(caster).sort((a,b)=>(b.a-a.a)||(a.h-b.h)).slice(0,2);
    if(!cres.length)return false;
    cres.forEach(t=>{ t.h-=card.val; });
    log(`<span class="${towner==='you'?'e':'y'}">${card.nm} arcs through ${cres.map(c=>c.nm).join(' &amp; ')} for ${card.val}.</span>`,towner==='you'?'e':'y');
  } else if(card.effect==='bounce'){
    if(o.kind!=='creature'||o.worker)return false;
    if(o.entrench){ log(`<span class="${towner==='you'?'e':'y'}">${o.nm} is entrenched — ${card.nm} slides off.</span>`,towner==='you'?'e':'y'); }
    else { const ow=removeUnitFromBoard(o); if(ow){ G.P[ow].hand.push(handcardFromCreature(o)); log(`<span class="${towner==='you'?'e':'y'}">${card.nm} drags ${o.nm} back to ${ow==='you'?'your':'their'} hand.</span>`,towner==='you'?'e':'y'); } }
  } else return false;
  cleanup(); return true;
}
function castSpell(idx,key,i){
  const card=G.P.you.hand[idx]; if(!card||card.type!=='spell'||card.trap)return;
  if(manaTotal('you')<card.c){setHint('Not enough mana.');return;}
  if(!canPay('you',card)){setHint(`${card.nm} needs ◆${card.c}.`);return;}
  if(!resolveSpell(card,key,i)){setHint('Not a legal target for that spell.');return;}
  payCost('you',card); G.P.you.hand.splice(idx,1); G.P.you.grave.push(spellRec(card));
  G.sel=null; defaultHint(); render(); checkWin();
}
function findArmedTrap(owner,trigger){
  for(const w of ['front','back']) for(let i=0;i<SLOTS;i++){ const o=G.P[owner][w][i];
    if(o&&o.kind==='trap'&&o.card.trigger===trigger&&G.turnNo>(o.setTurn??0)) return {o,w,i}; }
  for(let i=0;i<SLOTS;i++){ const o=G.center[i];
    if(o&&o.kind==='trap'&&o.owner===owner&&o.card.trigger===trigger&&G.turnNo>(o.setTurn??0)) return {o,w:'center',i}; }
  return null;
}
// foe's trap auto-springs when YOU summon
function foeTrapOnSummon(cr,w,i){
  const t=findArmedTrap('foe','summon'); if(!t)return;
  const arr=cellArr('you',w);
  if(!cr||arr[i]!==cr)return;
  log(`<span class="e">${t.o.card.nm} springs! ${cr.nm} is dragged down as it forms.</span>`,'e');
  toGrave('you',cr); arr[i]=null;
  G.P.foe.grave.push(spellRec(t.o.card)); cellArr('foe',t.w)[t.i]=null;
  cleanup();
}
// your trap may spring when the FOE summons (during foeTurn) — you choose
function playerTrapOnSummon(cr,w,i){
  return new Promise(resolve=>{
    const t=findArmedTrap('you','summon');
    const arr=cellArr('foe',w);
    if(!t||!cr||arr[i]!==cr){resolve();return;}
    const box=$('contestPanel').querySelector('.box');
    box.innerHTML=`<div class="ptitle" style="color:#f0b89a">Trap! · ${t.o.card.nm}</div>`+
      `<div class="pmeta" style="margin-bottom:10px;color:var(--ink)">The opponent summons <b>${cr.nm}</b> (⚔${cr.a}/♥${cr.h}). ${spellText(t.o.card)} Spring it now?</div>`+
      `<div class="pacts"><button class="flip" id="trYes">Spring it</button><button class="pour" id="trNo">Hold</button></div>`;
    box.querySelector('#trYes').addEventListener('click',()=>{
      $('contestPanel').style.display='none';
      if(arr[i]===cr){ log(`<span class="y">${t.o.card.nm} springs — ${cr.nm} is destroyed!</span>`,'y'); toGrave('foe',cr); arr[i]=null; }
      G.P.you.grave.push(spellRec(t.o.card)); cellArr('you',t.w)[t.i]=null; cleanup(); render(); resolve();
    });
    box.querySelector('#trNo').addEventListener('click',()=>{ $('contestPanel').style.display='none'; log('<span class="y">You hold your trap.</span>','y'); resolve(); });
    $('contestPanel').style.display='flex';
  });
}

/* ---------- move banked ◆ from one board card to another ---------- */
window.startSendMana=(key,i)=>{ if(G.turn!=='you'||G.busy||G.over)return; i=+i; const c=rowArr(key)[i]; if(!c||!(c.bank>0))return; G.moveMana={k:key,i}; G.sel=null; G.atk=[]; G.cardMenu=null; G.moveFrom=null; setHint(`Move ◆${c.bank} — tap one of your creatures or structures to store it there (or tap this card to cancel).`); render(); };
window.cancelSendMana=()=>{ G.moveMana=null; defaultHint(); render(); };
function doSendMana(toK,toI){
  if(!G.moveMana)return; const {k,i}=G.moveMana; const src=rowArr(k)[i]; const dst=rowArr(toK)[toI];
  if(!src||!dst||(k===toK&&i===toI)||dst.owner!=='you'||!(dst.kind==='creature'||dst.kind==='building')){ G.moveMana=null; defaultHint(); render(); return; }
  const amt=src.bank||0; dst.bank=(dst.bank||0)+amt; src.bank=0;
  log(`<span class="y">Moved ◆${amt} of stored mana from ${src.nm} to ${dst.nm}.</span>`,'y');
  G.moveMana=null; defaultHint(); render();
}

/* ---------- charging panel (creatures & structures, either row) ---------- */
let cs=null, camt=0;
function chSel(){ return cs?rowArr(cs.k)[cs.i]:null; }
window.openCharge=function(key,slot){ if(G.turn!=='you'||G.busy||G.over)return; cs={k:key,i:slot}; camt=0; $('cpanel').style.display='flex'; drawPanel(); };
function drawPanel(){
  const ch=chSel(); if(!ch||ch.kind!=='charge'){closePanel();return;}
  const mana=manaTotal('you'); if(camt>mana)camt=mana; if(camt<0)camt=0;
  const ready=ch.inv>=ch.card.c; const excess=Math.max(0,ch.inv-ch.card.c);
  const isB=ch.ctype==='building';
  const meta=isB?`structure · raise cost ◆${ch.card.c} · ♥${ch.card.h} · ${ch.card.eff==='mana'?('yields ◆+'+ch.card.val+'/turn'):'trains a minion/turn'}`
                :`charging spell · summon cost ◆${ch.card.c} · ⚔${ch.card.a}/♥${ch.card.h}`;
  $('cpanel').querySelector('.box').innerHTML=`
    <div class="ptitle">${ch.card.nm}</div>
    <div class="pmeta">${meta}</div>
    <div class="pinv">Invested <b style="color:var(--spawn)">◆${ch.inv}</b>${(!isB&&excess)?` <span style="color:var(--gold)">(◆${excess} would bank)</span>`:''}</div>
    <div class="pmeta">Your mana: ◆${mana}</div>
    <div class="stepper"><button onclick="camtAdj(-1)">−</button><div class="amt">◆${camt}</div><button onclick="camtAdj(1)">+</button></div>
    <div class="quick"><button onclick="camtFill()">Fill to cost</button><button onclick="camtMax()">All ◆${mana}</button></div>
    <div class="pacts"><button class="pour" ${camt<=0?'disabled':''} onclick="camtPour()">Pour ◆${camt}</button>
      <button class="flip" ${ready?'':'disabled'} onclick="camtFlip()">${isB?'Raise':'Flip up'}${(!isB&&excess)?` (bank ◆${excess})`:''}</button></div>
    <button class="pclose" onclick="closePanel()">Done</button>`;
}
window.camtAdj=d=>{camt+=d;drawPanel();};
window.camtFill=()=>{const ch=chSel();camt=Math.max(0,Math.min(manaTotal('you'),ch.card.c-ch.inv));drawPanel();};
window.camtMax=()=>{camt=manaTotal('you');drawPanel();};
window.camtPour=()=>{const ch=chSel();const p=Math.min(camt,manaTotal('you'));if(p<=0)return;payAny('you',p);ch.inv+=p;log(`<span class="y">Poured ◆${p} (now ◆${ch.inv}/${ch.card.c}).</span>`,'y');camt=0;render();drawPanel();};
window.camtFlip=()=>{const ch=chSel();if(!ch||ch.inv<ch.card.c)return;flip('you',cs.k,cs.i);closePanel();render();};
window.closePanel=()=>{$('cpanel').style.display='none';cs=null;camt=0;};
function flip(owner,key,slot){
  const arr=rowArr(key);
  const ch=arr[slot];
  if(ch.ctype==='building'){
    const b=mkBld(ch.card,owner); b.bank=Math.max(0,ch.inv-ch.card.c);
    arr[slot]=b;
    log(`<span class="${owner==='you'?'y':'e'}">${owner==='you'?'Your':'Their'} ${ch.card.nm} rises — structure online (♥${ch.card.h}).${b.bank?` ◆${b.bank} banked.`:''}</span>`,owner==='you'?'y':'e');
    return;
  }
  const bank=Math.max(0,ch.inv-ch.card.c);
  const sick=G.turnNo<=(ch.setTurn??G.turnNo);
  const cr=mkCre(ch.card,owner,false); cr.bank=bank; cr.sick=sick;
  arr[slot]=cr;
  log(`<span class="${owner==='you'?'y':'e'}">${owner==='you'?'Your':'Their'} ${cr.nm} surges into being! (⚔${cr.a}/♥${cr.h})${cr.fs?' First Strike.':''}${bank?` ◆${bank} banked.`:''}${sick?' Must rest this turn.':' Battle-ready!'}</span>`,owner==='you'?'y':'e');
  onCreatureEnter(cr,owner);
  syncWorkers(owner);
  return cr;
}
function trainVillager(owner){
  if(!canTrain(owner))return false;
  const v=mkVil(owner); v.sick=true; minPool(owner,'back').push(v);
  log(`<span class="${owner==='you'?'y':'e'}">${owner==='you'?'You train':'Opponent trains'} a Worker (⚒) at the base.</span>`,owner==='you'?'y':'e');
  return true;
}

