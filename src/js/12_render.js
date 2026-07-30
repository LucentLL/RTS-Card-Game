/* ---------- render ---------- */
function manaStr(o){ // one generic pool + the vault capacity chip (what survives the end-of-turn drain)
  const cap=vaultCap(o);
  return `<b class="mc" style="color:var(--spawn)">◆${G.P[o].mana}</b>`+
    (cap>0?`<b class="mc" style="color:var(--spawn);opacity:.7;margin-left:4px" title="Mana Vault capacity — unspent mana above ◈${cap} drains at end of turn">◈${cap}</b>`:'');}
function render(){
  // life is a standalone pool now (no command-center card) — undefended back-row strikes drain it
  $('foeLife').textContent=G.P.foe.life; $('youLife').textContent=G.P.you.life;
  $('foeManaStr').innerHTML=manaStr('foe'); $('youManaStr').innerHTML=manaStr('you');
  $('foeStruct').textContent=structuresOf('foe').length; $('youStruct').textContent=structuresOf('you').length;
  $('foeCap').textContent=totalWorkers('foe'); $('youCap').textContent=totalWorkers('you');
  {const a=$('foeDeckN');if(a)a.textContent=G.P.foe.deck.length;} {const b=$('youDeckN');if(b)b.textContent=G.P.you.deck.length;}
  {const a=$('foeGraveN');if(a)a.textContent=G.P.foe.grave.length;} {const b=$('youGraveN');if(b)b.textContent=G.P.you.grave.length;}
  $('turnLabel').textContent=G.turn==='you'?'Your Turn':'Opponent…';
  $('endBtn').disabled=!acting();   // End Turn only in the Action phase (draw/upkeep resolve via their own steps)
  { const hb=$('harvestBtn'); if(hb){ const show=(G.turn==='you'&&G.phase==='upkeep'&&!G.busy&&!G.over);
      hb.style.display=show?'':'none';
      // lock only on the CREATURE-settleable shortfall — a purely structural one (e.g. an unsupported
      // tower, nothing to move or sacrifice) is paid by Harvest itself, so the button must stay live
      if(show){ const locked=(totalDeficit('you')-orphanDeficit('you'))>0; hb.disabled=locked; hb.title=locked?'Settle the worker shortfall first — Move, Pay, or Sacrifice the flagged creatures':''; } } }
  renderPhaseTrack();
  const cb=$('conscriptBtn'); if(cb)cb.style.display='none';
  const aiming=(G.turn==='you'&&!G.busy&&!G.over)&&canAttack();
  renderRow('foeBack','foe','back'); renderRow('foeFront','foe','front');
  renderCenter();
  renderRow('youFront','you','front'); renderRow('youBack','you','back'); renderHand(); renderFoeHand();
  renderCmdZone('foe'); renderCmdZone('you');
  renderWalls();
  placeCardMenu();
}
// deck + graveyard in the castle walls as REAL PILES: one stacked layer per card (capped at 10),
// the count on a badge riding the top card; the graveyard's top card lies face-up (desaturated);
// an empty pile is a flat vacant slot. Rest-state counts stay on the .decline lines below.
function renderWalls(){
  const fill=(id,owner,kind)=>{
    const el=$(id); if(!el)return;
    const arr=kind==='deck'?G.P[owner].deck:G.P[owner].grave; const n=arr.length;
    const pile=kind==='deck'?'deckpile':'gravepile';
    let stack;
    if(!n){
      stack=`<div class="wzfull empty ${pile}"><span class="wzbadge">0</span></div>`;
    }else{
      const layers=Math.min(n,10);                 // 1..10 cheap divs — depth reads, DOM stays light
      let lay='';
      for(let i=0;i<layers-1;i++) lay+=`<div class="wzlayer" style="--li:${i}"></div>`;
      const face=kind==='grave'?cardArtImg(arr[n-1]):'';
      lay+=`<div class="wzlayer top" style="--li:${layers-1}">${face}<span class="wzbadge">${n}</span></div>`;
      stack=`<div class="wzfull ${pile}">${lay}</div>`;
    }
    el.innerHTML=`<span class="wzl">${kind==='deck'?'Deck':'RIP'}</span>${stack}`;
    el.title=`${owner==='you'?'Your':"Opponent's"} ${kind==='deck'?'deck':'graveyard'} — ${n} card${n===1?'':'s'}`;
  };
  fill('youWallDeck','you','deck'); fill('youWallGrave','you','grave');
  fill('foeWallDeck','foe','deck'); fill('foeWallGrave','foe','grave');
  const fw=$('foeWorkerChips'); if(fw){ fw.innerHTML=''; fw.appendChild(workerChipRow('foe')); }  // foe per-row workers on their wall
  const dl=$('youDeckLine'); if(dl) dl.innerHTML=`Deck: <b>${G.P.you.deck.length}</b> &nbsp; GY: <b>${G.P.you.grave.length}</b>`;
  const fl=$('foeDeckLine'); if(fl) fl.innerHTML=`Deck: <b>${G.P.foe.deck.length}</b> &nbsp; GY: <b>${G.P.foe.grave.length}</b>`;
}
// the phase tracker: lights the current phase (Combat lit alongside Action while attackers are declared)
function renderPhaseTrack(){
  const track=$('phaseTrack'); if(!track)return;
  const yours=G.turn==='you'&&!G.over;   // stays lit through the busy End-phase beat; blank only on the opponent's turn
  const cur=yours?shownPhase():null;   // no highlight while the opponent acts
  track.querySelectorAll('.phstep').forEach(el=>{
    const p=el.dataset.phase;
    const on = yours && ( p===cur || (cur==='combat'&&p==='action') );   // combat is a sub-phase of action → both light up
    el.classList.toggle('on',!!on);
    el.classList.toggle('done', yours && p!=='combat' && PHASE_ORDER.indexOf(p)>-1 && PHASE_ORDER.indexOf(p)<PHASE_ORDER.indexOf(G.phase));
  });
  // body phase class drives the draw-phase deck pulse + keeps the wall open so the deck is reachable
  const b=document.body.classList;
  ['phase-draw','phase-upkeep','phase-action','phase-end'].forEach(c=>b.remove(c));
  if(yours) b.add('phase-'+G.phase);
}
function renderMinions(){
  const wells=[['wellFoeBack','foeBack'],['wellFoeFront','foeFront'],['wellCenter','center'],['wellYouFront','youFront'],['wellYouBack','youBack']];
  const aiming=(G.turn==='you'&&!G.busy&&!G.over)&&canAttack();
  wells.forEach(([elId,key])=>{
    const el=$(elId); if(!el)return; el.innerHTML='';
    const groups=[];
    if(key==='center'){ if(minPool('you','center').length)groups.push(['you','center']); if(minPool('foe','center').length)groups.push(['foe','center']); }
    else { const owner=key.startsWith('wellFoe')||key.startsWith('foe')?'foe':'you'; const which=key.endsWith('Front')?'front':'back'; if(minPool(owner,which).length)groups.push([owner,which]); }
    groups.forEach(([owner,which])=>el.appendChild(workerTokEl(owner,which,key,aiming)));
    // shortfall badge: a row whose monsters outweigh its support shows the deficit
    ['you','foe'].forEach(owner=>{
      const which=key==='center'?'center':(key.endsWith('Front')?'front':'back');
      const isOwnerWell=key==='center'||(owner==='you')===key.startsWith('wellYou');
      if(!isOwnerWell)return;
      const n=rowWorkers(owner,which);
      if(n<0){ const d=document.createElement('div'); d.className='wtok short'+(owner==='you'?'':' foe'); d.innerHTML=`⚒<b>${n}</b>`; d.title='worker shortfall — fix at start of your turn'; el.appendChild(d); }
    });
  });
}
function workerTokEl(owner,which,key,aiming){
  const list=minPool(owner,which); const me=owner==='you';
  const up=list.filter(m=>!m.tapped&&!m.sick).length;
  const tok=document.createElement('div');
  tok.className='wtok'+(me?'':' foe');
  tok.innerHTML=`⚒<b>${list.length}</b>`+(up<list.length?`<span class="wr">${up}✓</span>`:'');
  tok.title=`${list.length} worker${list.length===1?'':'s'} (${up} ready) — tap to harvest`;
  if(G.turn==='you'&&!G.busy&&!G.over){
    if(me&&up>0&&!G.sel&&!G.moveFrom&&!G.moveMana){ tok.classList.add('tappable'); tok.addEventListener('click',()=>harvestRow(which)); }
    else if(!me&&aiming){ tok.classList.add('target'); tok.addEventListener('click',()=>attackMinionStack(key,owner,which)); }
  }
  tok.addEventListener('contextmenu',e=>{e.preventDefault(); inspectMinion(owner,which);});
  return tok;
}
function placeCardMenu(){
  const el=$('cardActions'); if(!el)return;
  const m=G.cardMenu;
  if(!m||G.turn!=='you'||G.busy||G.over){ el.style.display='none'; el.classList.remove('handmenu'); return; }
  let cell;
  if(m.hand){
    cell=$('hand').children[m.i];
    if(!cell||!G.P.you.hand[m.i]){ el.style.display='none'; G.cardMenu=null; return; }
    el.classList.add('handmenu');
  } else {
    el.classList.remove('handmenu');
    const row=$(m.k);                        // card menus anchor by GLOBAL row key (fronts are contested)
    cell=rowCellEl(row,m.i);
    if(!cell||!rowArr(m.k)[m.i]){ el.style.display='none'; G.cardMenu=null; return; }
  }
  el.innerHTML=m.html;
  el.style.display='block';
  const r=cell.getBoundingClientRect();
  const ew=el.offsetWidth, eh=el.offsetHeight;
  let left=Math.max(6,Math.min(r.left+r.width/2-ew/2, window.innerWidth-ew-6));
  let top=r.top-eh-12; el.classList.remove('below');
  if(top<6){ top=r.bottom+12; el.classList.add('below'); }
  el.style.left=left+'px'; el.style.top=top+'px';
}
function cardHTML(o,me){
  if(o.kind==='building'&&o.cc){
    const pips=(o.colors||[]).map(c=>`<span class="cdot" style="background:var(--${c});width:6px;height:6px"></span>`).join('');
    return `<div class="card bld ccx"><div class="nm">${o.nm}</div><div class="artwin">${cardArtImg(o)}</div><div class="ccpips">${pips}</div><div class="stats"><span></span><span class="hp">♥${o.h}</span></div><div class="ccb">COMMAND</div></div>`;
  }
  if(o.kind==='creature'){
    const cls=o.worker?'vil':('crt '+clsOf[o.color]);
    const art=o.art?`<div class="artwin">${cardArtImg(o)}</div>`:'';
    const wd=(!o.worker&&o.up)?`<span class="cap neg">⚒-${o.up}</span>`:'';
    const stch=(!o.worker&&(o.moved||o.tapped||o.sick))
      ?`<div class="stch">${o.sick?'<span title="summoning-sick">💤</span>':''}${o.moved?`<span title="moved${o.moved2?' twice':''} this turn">⤧${o.moved2?'²':''}</span>`:''}${o.tapped?'<span title="has acted">⟳</span>':''}</div>`:'';
    return `<div class="card ${cls} ${o.sick?'sick':''} ${o.tapped?'tapped':''}">
      ${o.bank>0?`<div class="bank">◆${o.bank}</div>`:''}
      ${o.fs?'<div class="fsbadge">FS</div>':''}${stch}
      ${o.worker?'<div class="wk">⚒</div>':''}
      <div class="nm">${o.nm}</div>${art}<div class="stats"><span class="atk">${o.a}</span><span class="mid">${o.worker?'':`<span class="ebw">${elemGem(o.color,14)}</span>`}${wd}</span><span class="hp">♥${o.h}</span></div></div>`;
  }
  if(o.kind==='building'){
    const bart=o.art?`<div class="artwin">${cardArtImg(o)}</div>`:`<div class="bic">${o.ic||'⌂'}</div>`;
    const eff=o.eff==='mana'?('◆+'+o.val):o.eff==='villager'?'⚒train':o.eff==='damage'?('⚔'+o.val):o.eff==='vault'?('◈'+o.val):o.eff==='wall'?'▣':o.eff==='revive'?'☩':'⌂';
    const wd=o.sup?`<span class="cap plus">⚒+${o.sup}</span>`:'';
    return `<div class="card bld">${o.bank>0?`<div class="bank">◆${o.bank}</div>`:''}<div class="nm">${o.nm}</div>${bart}
      <div class="stats"><span class="eff">${eff}</span><span class="mid">${o.color?`<span class="ebw">${elemGem(o.color,14)}</span>`:''}${wd}</span><span class="hp">♥${o.h}</span></div></div>`;
  }
  if(o.kind==='charge'){
    const ready=o.inv>=o.card.c;
    return me?`<div class="card charge mine ${ready?'ready':''}"><div class="bank">◆${o.inv}${ready?' ✓':''}</div><div class="q" style="font-size:13px;text-align:center">${o.card.nm}</div><div class="inv">◆${o.inv}/${o.card.c}${ready?' ✓':''}</div></div>`
            :`<div class="card charge"><div class="bank">◆${o.inv}</div><div class="q">?</div><div class="inv">◆${o.inv} invested</div></div>`;
  }
  if(o.kind==='trap'){
    return me?`<div class="card charge trap mine"><div class="q" style="font-size:12px">⚠ ${o.card.nm}</div><div class="inv">trap · armed</div></div>`
            :`<div class="card charge trap"><div class="q">⚠</div><div class="inv">set</div></div>`;
  }
  return '';
}
// a floating standee figure that hovers above a creature's card (Duel Links style)
function attachSprite(cell,o,me,key,i){
  // every on-field card gets the _fieldart standee scaffolding (field → card art → placeholder),
  // not just creatures — structures included. Face-downs (traps/charges) and worker tokens stay hidden.
  if(!window.SPRITES_ON||!o||o.worker) return;
  if(o.kind!=='creature'&&o.kind!=='building') return;
  cell.classList.add('hasSprite'); if(o.kind==='building') cell.classList.add('bldSprite');
  // a creature that can't act right now lies down (up = able to act, down = idle); structures never do
  if(o.kind==='creature'&&!canActNow(o,key,i)) cell.classList.add('laid');
  const w=document.createElement('div'); w.className='spritewrap'+(me?'':' foe')+(o.kind==='building'?' bld':'');
  w.innerHTML=`<div class="spriteshadow"></div><div class="spritebob">${spriteImg(o)}</div>`;
  cell.appendChild(w);
}
window.toggleSprites=()=>{ window.SPRITES_ON=!window.SPRITES_ON; const b=$('sprBtn'); if(b)b.classList.toggle('off',!window.SPRITES_ON); render(); };
// Workers now live on each player's castle wall (see workerChipRow). The board only shows a
// FLOATING chip over a row's outer edge when it's actionable right there: an enemy stack you can
// strike mid-aim, or a shortfall warning. Returns null otherwise — rows keep their full width.
function zoneForRow(owner,key){ // which of `owner`'s worker zones lives in global row `key` (null if none)
  if(key==='center')return 'center';
  if(owner==='you') return key==='youBack'?'back':key==='youFront'?'front':(key==='foeFront'||key==='foeBack')?'raid':null;   // raid spans BOTH enemy rows
  return key==='foeBack'?'back':key==='foeFront'?'front':(key==='youFront'||key==='youBack')?'raid':null;
}
function rowFloatChips(key,aiming){
  const out=[];
  ['you','foe'].forEach(owner=>{
    const z=zoneForRow(owner,key); if(!z)return;
    const c=wkSlotEl(owner,z,aiming); if(c)out.push(c);
  });
  return out;
}
function wkSlotEl(owner,which,aiming){
  if(!owner) return null;
  const n=rowWorkers(owner,which); const me=owner==='you';
  const pool=minPool(owner,which);
  const targetable=!me&&aiming&&pool.length&&G.turn==='you'&&!G.busy&&!G.over&&G.phase==='action';
  if(!targetable&&n>=0) return null;
  const slot=document.createElement('div'); slot.className='wkslot '+(me?'youSide':'foeSide');
  const tok=document.createElement('div');
  tok.className='wtok'+(me?'':' foe')+(n<0?' short':'');
  tok.innerHTML=`⚒<b>${n}</b>`;
  tok.title=n<0?(me?'worker shortfall — fix at the start of your turn':'enemy worker shortfall'):`enemy workers here: ${n} — tap to strike the stack`;
  if(targetable){ tok.classList.add('target'); const rowId=which==='center'?'center':(owner==='foe'?'foe':'you')+(which==='front'?'Front':'Back'); tok.addEventListener('click',()=>attackMinionStack(rowId,owner,which)); }
  tok.addEventListener('contextmenu',e=>{e.preventDefault();inspectMinion(owner,which);});
  slot.appendChild(tok); return slot;
}
// the per-row worker chips shown on a player's castle wall: Back / Front / Center counts,
// tappable to harvest (yours), right-click to inspect the stack — the old rail pills' duties
function workerChipRow(owner){
  const box=document.createElement('div'); box.className='cmdworkers'+(owner==='you'?'':' foe');
  const LBL={back:'Back',front:'Front',center:'Center'};
  const me=owner==='you';
  for(const which of ['back','front','center']){
    const n=rowWorkers(owner,which);
    const pool=minPool(owner,which); const up=pool.filter(m=>!m.tapped&&!m.sick).length;
    const tok=document.createElement('div');
    tok.className='wtok'+(me?'':' foe')+(n<0?' short':'')+(n===0?' none':'');
    tok.innerHTML=`<span class="wklab">${LBL[which]}</span>⚒<b>${n}</b>`+(n>0&&up<pool.length?`<span class="wr">${up}✓</span>`:'');
    tok.title=me?(n<0?'worker shortfall — settle it at your upkeep (move, sacrifice, or pay)':(up>0?`${n} worker${n===1?'':'s'} — harvests ◆${up*minYield(which)} automatically at your upkeep`:`${n} worker${n===1?'':'s'} — already harvested this turn`)):`enemy ${LBL[which].toLowerCase()} row workers: ${n}`;
    tok.addEventListener('contextmenu',e=>{e.preventDefault();inspectMinion(owner,which);});
    box.appendChild(tok);
  }
  { const raid=rowWorkers(owner,'raid');   // an army camped in the ENEMY front row — pure upkeep, paid or pulled back
    if(raid<0){ const tok=document.createElement('div');
      tok.className='wtok short'+(me?'':' foe');
      tok.innerHTML=`<span class="wklab">Raid</span>⚒<b>${raid}</b>`;
      tok.title=(me?'your':'enemy')+' creatures behind enemy lines — their keep (◆'+(-raid)+') is paid at each upkeep';
      box.appendChild(tok); } }
  return box;
}
// the player's worker readout for the castle wall's LEFT tower: FIVE rows stacked vertically, aligned
// to the board top→bottom (enemy base · raid · center · your front · your base) for at-a-glance reading.
function workerColumn(){
  const box=document.createElement('div'); box.className='cmdworkers vcol';
  // [label, zone, harvests?] — zone null = the enemy base row, where you can never staff workers
  const rows=[['Enemy Base',null,false],['Raid','raid',false],['Center','center',true],['Front','front',true],['Base','back',true]];
  for(const [lab,z,harvests] of rows){
    const row=document.createElement('div');
    if(z===null){ row.className='wtok vrow none'; row.innerHTML=`<span class="wklab">${lab}</span><span class="wval">—</span>`; row.title='the enemy stronghold row — units besieging it count toward your Raid upkeep'; box.appendChild(row); continue; }
    const n=rowWorkers('you',z);
    const pool=harvests?minPool('you',z):[]; const up=pool.filter(m=>!m.tapped&&!m.sick).length;
    row.className='wtok vrow'+(n<0?' short':'')+(n===0?' none':'');
    const ready=(harvests&&n>0&&up<pool.length)?`<span class="wr">${up}✓</span>`:'';
    row.innerHTML=`<span class="wklab">${lab}</span><span class="wval">⚒<b>${n}</b></span>${ready}`;
    row.title = z==='raid'
      ? (n<0?`creatures behind enemy lines — their upkeep (◆${-n}) is paid every turn`:'no units raiding the enemy front')
      : (n<0?'worker shortfall — settle it at upkeep (move, sacrifice, or pay)'
           : (up>0?`${n} worker${n===1?'':'s'} — harvests ◆${up*minYield(z)} at upkeep`
                 : `${n} worker${n===1?'':'s'}`));
    box.appendChild(row);
  }
  return box;
}
// pin the deck/graveyard zones to the floor's corner cells so they ride the tilted
// plane (your deck off the bottom-right corner, opponent's off the top-left corner)
// pin the deck directly beside the outer back-line card slot, on the perspective plane
// (the outer worker well on that side of the back row is empty, so the deck tucks in next to the cards)
function positionDeck(deck,row,owner){
  const cells=[...row.querySelectorAll('.cell:not(.deckslot)')];
  if(!cells.length)return;
  const c=owner==='you'?cells[cells.length-1]:cells[0];   // outer card slot of the back line
  const w=c.offsetWidth,h=c.offsetHeight;
  const gap=(parseFloat(getComputedStyle(row).columnGap)||6)+2;
  deck.style.width=w+'px'; deck.style.height=h+'px'; deck.style.top=c.offsetTop+'px';
  deck.style.left=(owner==='you'? (c.offsetLeft+w+gap) : (c.offsetLeft-gap-w))+'px';
}
// the graveyard tombstone stacks just ABOVE the deck (toward each player's front line), riding the same plane
function positionGrave(grave,deck,owner){
  const w=parseFloat(deck.style.width)||deck.offsetWidth;
  const dh=parseFloat(deck.style.height)||deck.offsetHeight;
  const dtop=parseFloat(deck.style.top)||deck.offsetTop;
  const dleft=parseFloat(deck.style.left)||deck.offsetLeft;
  const gh=Math.round(dh*0.72), gap=8;
  grave.style.width=w+'px'; grave.style.height=gh+'px'; grave.style.left=dleft+'px';
  grave.style.top=(owner==='you'? (dtop-gh-gap) : (dtop+dh+gap))+'px';
}
function renderRow(elId,owner,which){
  const el=$(elId);el.innerHTML='';
  const aiming=(G.turn==='you'&&!G.busy&&!G.over)&&canAttack();
  for(let i=0;i<SLOTS;i++){
    const o=G.P[owner][which][i];
    const me=!!(o&&o.owner==='you');
    const cell=document.createElement('div');cell.className='cell'+(which==='back'?' backcell':'');
    cell.dataset.key=elId; cell.dataset.owner=o?o.owner:owner; cell.dataset.which=which; cell.dataset.slot=i;   // drag-drop coordinates
    if(o){ cell.classList.add(me?'mineHere':'foeHere'); cell.innerHTML=cardHTML(o,me); addInspect(cell,()=>inspectRef(owner,which,i),o.owner); attachSprite(cell,o,me,elId,i); }
    decorate(cell,elId,i,o);
    el.appendChild(cell);
  }
  rowFloatChips(elId,aiming).forEach(c=>el.appendChild(c)); // floating chips only when actionable
  // (deck + graveyard now live in the castle walls — see renderWalls — not on the board plane)
}
const GUARDIAN_SVG=`<svg viewBox="0 0 48 62" xmlns="http://www.w3.org/2000/svg">
  <defs><linearGradient id="cg" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="var(--fc)"/><stop offset="1" stop-color="#160f22"/></linearGradient></defs>
  <rect x="36" y="8" width="3" height="48" rx="1.5" fill="#7a663f"/>
  <circle cx="37.5" cy="8" r="4.2" fill="var(--fc)" stroke="#fff2cf" stroke-width="1"/>
  <path d="M24 16 C15 16 11.5 22 10.5 32 L7.5 54 C7.5 58 14 59 24 59 C34 59 40.5 58 40.5 54 L37.5 32 C36.5 22 33 16 24 16 Z" fill="url(#cg)" stroke="#090610" stroke-width="1.1"/>
  <path d="M24 7 C17.5 7 14.5 12 15.5 18.5 C18 22.5 30 22.5 32.5 18.5 C33.5 12 30.5 7 24 7 Z" fill="var(--fc)" stroke="#090610" stroke-width="1.1"/>
  <ellipse cx="24" cy="15.5" rx="5.2" ry="6.2" fill="#090610" opacity=".8"/>
  <circle cx="21.6" cy="15.2" r="1.4" fill="#fff2cf"/><circle cx="26.4" cy="15.2" r="1.4" fill="#fff2cf"/>
  <path d="M16 36 L24 33 L32 36 L24 39 Z" fill="#fff2cf" opacity=".35"/></svg>`;
function renderCmdZone(owner){
  const el=$(owner+'Cmd'); if(!el)return;
  const P=G.P[owner]; const def=CCS[P.cc];
  if(!def){ el.innerHTML=''; el.className='cmdzone '+owner; el.style.pointerEvents='none'; return; }
  const me=owner==='you';
  el.className='cmdzone '+owner;
  // no command-center card: the cluster shows the player's element, LIFE POOL, vitals + Build entry
  const canBuild=acting();
  const vit=me?(`<div class="cmdvit">${manaStr('you')}</div>`
    +`<div class="cmdvit sub"><span class="bct">⌂</span><b>${structuresOf('you').length}</b><span class="wct">⚒</span><b>${totalWorkers('you')}</b></div>`
    +`<button class="cmdbuild" ${canBuild?'':'disabled'} onclick="openBuildMenu()" title="Build a structure">⚒ Build</button>`):'';
  const fc=`var(--${def.colors[0]})`;
  // the ENEMY life pool is a direct attack target ("the face") — always available while you have
  // attackers, even if their back row is full of bodies. Interceptors may still meet the strike.
  const aimLife=!me&&(G.turn==='you'&&!G.busy&&!G.over&&canAttack());
  el.innerHTML=`<div class="keepname" style="color:${fc}" title="${def.name}">♜ ${escHtml(def.name)}</div>`+
    `<div class="keephp${aimLife?' lifeaim':''}"${aimLife?' title="Strike the enemy life directly"':''}><span class="heart">♥</span>${Math.max(0,P.life)}</div>`+vit;
  if(me) el.appendChild(workerColumn());   // five-row vertical worker readout in the left tower
  el.style.pointerEvents=(me||aimLife)?'auto':'none';
  if(aimLife){ const hp=el.querySelector('.keephp'); if(hp){ hp.style.cursor='pointer';
    hp.addEventListener('click',()=>{ if(G.turn==='you'&&!G.busy&&!G.over&&canAttack()) attackBackRow('foe',G.atk.length?G.atk[0].i:BASE_COL); }); } }
}
function renderCenter(){
  const el=$('center'); el.innerHTML='';
  const aiming=(G.turn==='you'&&!G.busy&&!G.over)&&canAttack();
  for(let i=0;i<SLOTS;i++){
    // the contested center is a mountain pass: 3 monster lanes (cols 1/3/5) flanked by 4 structure slots (cols 0/2/4/6)
    const cell=document.createElement('div'); cell.className='cell centercell '+(isLane(i)?'centerlane':'centerstruct');
    const o=G.center[i];
    cell.dataset.key='center'; cell.dataset.owner=o?o.owner:'you'; cell.dataset.which='center'; cell.dataset.slot=i;   // drag-drop coords
    if(o){ const me=o.owner==='you'; cell.classList.add(me?'mineHere':'foeHere'); cell.innerHTML=cardHTML(o,me); addInspect(cell,()=>inspectRef(o.owner,'center',i),o.owner); attachSprite(cell,o,me,'center',i); }
    decorate(cell,'center',i,o);
    el.appendChild(cell);
  }
  rowFloatChips('center',aiming).forEach(c=>el.appendChild(c));   // floating chips only when actionable
}
function renderFoeHand(){
  const el=$('foeHand'); if(!el)return;
  const n=G.P.foe.hand.length; el.innerHTML='';
  const max=Math.min(n,10);
  for(let i=0;i<max;i++){ const b=document.createElement('div'); b.className='fb'; el.appendChild(b); } // flat row of backs (no fan)
  if(n){ const c=document.createElement('div'); c.className='fbcount'; c.textContent=n; el.appendChild(c); }
  el.title=`Opponent's hand — ${n} card${n===1?'':'s'}`;
}
function renderHand(){
  const el=$('hand');el.innerHTML='';
  G.P.you.hand.forEach((card,i)=>{
    const d=document.createElement('div'); d.dataset.hand=i;   // drag-drop source id
    const isB=card.type==='building'; const isS=card.type==='spell';
    d.className=`hc ${isB?'hcb':isS?'hcs':clsOf[card.color]}`+(G.sel&&G.sel.kind==='hand'&&G.sel.idx===i?' selected':'');
    const tl=isS?(card.trap?'Trap':'Spell'):isB?'Structure':(typeLine(card)||'Creature');
    const gem=card.color?`<span class="costgem">${elemGem(card.color,16)}</span>`:'';   // element rides beside the cost
    const head=`<div class="hchead"><div class="cost">${card.c}</div>${gem}<div class="nmw"><div class="nm">${card.nm}</div><div class="tl">${tl}</div></div></div>`;
    const art=card.art?`<div class="artwin">${cardArtImg(card)}</div>`
                      :`<div class="artwin ph"><span class="bic">${isS?(card.trap?'⚠':'✦'):isB?(card.ic||'⌂'):'⚔'}</span></div>`;
    const ribbon=`<div class="ribbon">${isS?(card.trap?'⚠ TRAP':'✦ SPELL'):isB?'STRUCTURE':'CREATURE'}</div>`;
    const wd=isB?(card.sup?`<span class="cap plus">⚒+${card.sup}</span>`:'')
              :(!isS&&card.up?`<span class="cap neg">⚒-${card.up}</span>`:'');
    // DM layout: art → type plate → white ability box → footer (power left · ⚒chip center · ♥ right)
    const rules=`<div class="rules">${abilityBrief(card)}</div>`;
    const stats=isS
      ?`<div class="stats"><span class="atk"></span><span class="mid"></span><span class="hp"></span></div>`
      :`<div class="stats">${isB?`<span class="eff">${card.eff==='mana'?('◆+'+card.val):'⚒train'}</span>`:`<span class="atk">${card.a}</span>`}<span class="mid">${wd}</span><span class="hp">♥${card.h}</span></div>`;
    d.innerHTML=`${head}<div class="hcbody">${art}${ribbon}${rules}${stats}</div>`;
    if(G.turn==='you'&&!G.busy&&!G.over)d.addEventListener('click',()=>onHand(i));
    addInspect(d,()=>inspectHand(i),'you');
    el.appendChild(d);
  });
}
function selCres(){return G.atk.map(s=>rowArr(s.k)[s.i]).filter(x=>x&&x.kind==='creature'&&x.owner==='you');}
function canAttack(){const c=selCres();return c.length>0&&c.every(x=>!x.worker&&!x.sick&&!x.tapped);}
function canExtract(){return false;} // creatures no longer extract mana — only workers harvest their row
function inAtk(key,i){return G.atk.some(s=>s.k===key&&s.i===i);}
function decorate(cell,key,i,o){
  if(G.turn!=='you'||G.busy||G.over)return;
  if(G.phase==='draw'||G.phase==='end')return;   // no board interaction during draw / end phases
  const mine=o&&o.owner==='you', foe=o&&o.owner==='foe';
  const which=whichOf(key);
  const deployKey=key==='youBack'||key==='youFront';   // new cards enter only your back + front rows
  if(G.moveFrom){
    const mf=G.moveFrom;
    if(mine&&key===mf.k&&i===mf.i){ cell.classList.add('selected'); cell.addEventListener('click',cancelMove); return; }
    if(!o&&adjacentK('you',mf.k,mf.i,key,i)){ cell.classList.add('tappable'); cell.addEventListener('click',()=>doMove(key,i)); }
    return;
  }
  if(G.upkeep){ // upkeep: settle each creature — Move (spends its actions) / Pay its keep / Sacrifice — before Harvest
    if(mine&&o.kind==='creature'){ cell.classList.add('tappable'); cell.addEventListener('click',()=>upkeepPick(key,i)); }
    return;
  }
  if(G.moveMana){
    const mm=G.moveMana;
    if(mine&&key===mm.k&&i===mm.i){ cell.classList.add('selected'); cell.addEventListener('click',cancelSendMana); return; }
    if(mine&&(o.kind==='creature'||o.kind==='building')){ cell.classList.add('target'); cell.addEventListener('click',()=>doSendMana(key,i)); return; }
    cell.addEventListener('click',cancelSendMana); return;
  }
  if(G.build){ const ok=(deployKey||(key==='center'&&!isLane(i)))&&!o&&placeRowOK('you',which,G.build); if(ok) cell.classList.add('tappable'); cell.addEventListener('click',()=>onCell(key,i,o)); return; }
  const handSel=G.sel&&G.sel.kind==='hand';
  if(mine&&o.kind==='creature'&&inAtk(key,i)) cell.classList.add('atksel');
  if(mine&&o.kind==='creature'&&!o.sick&&!o.tapped&&!handSel) cell.classList.add('tappable');
  if(mine&&o.kind==='charge'&&G.atk.length===0&&!handSel) cell.classList.add('tappable');
  if(handSel&&G.sel.mode&&G.sel.mode!=='cast'&&!o&&handDeployOK(key,i)) cell.classList.add('tappable');
  if(handSel&&(G.sel.mode==='summon'||G.sel.mode==='build')&&mine&&o.bank>0&&deployKey) cell.classList.add('target');
  if(handSel&&G.sel.mode==='cast'&&foe){ const sc=G.P.you.hand[G.sel.idx]; if(sc&&validSpellTarget(sc,o)) cell.classList.add('target'); }
  if(G.atk.length&&canAttack()){                       // any enemy field object is targetable (a body, a structure, a face-down); the defender's counterplay is interception, not column reach
    if(foe) cell.classList.add('target');              // (the castle wall is struck via the enemy ♥ — open cells are no longer a life target)
  }
  cell.addEventListener('click',()=>onCell(key,i,o));
}
function setHint(html){$('hint').innerHTML=html;}
function extractYield(which){return 1;}   // all rows equal — no positional bonus
function defaultHint(){setHint('');}

