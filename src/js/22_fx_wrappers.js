/* DOM cell for a live unit object — board slots first, then minion pill strips */
const FX_STRIP={foeBack:'wellFoeBack',foeFront:'wellFoeFront',center:'wellCenter',youFront:'wellYouFront',youBack:'wellYouBack'};
// the nth board CELL of a row element — skips the worker-slot(s) so the index lines up with the column
function rowCellEl(row,i){ return (row && row.querySelectorAll('.cell')[i])||null; }
function cellElFor(obj){
  if(!obj)return null;
  for(const key of ROWS){ const i=rowArr(key).indexOf(obj); if(i>=0){ return rowCellEl($(key),i); } }
  for(const o of ['you','foe']) for(const w of ['back','front','center']){
    if(G.P[o].min[w].indexOf(obj)>=0){
      const key=w==='center'?'center':(o==='you'?(w==='front'?'youFront':'youBack'):(w==='front'?'foeFront':'foeBack'));
      const s=$(FX_STRIP[key]); return (s&&s.firstElementChild)||null; } }
  return null;
}
const fxRect=el=>{ try{return el?el.getBoundingClientRect():null;}catch(e){return null;} };

/* ---------- wrappers: hook the existing flow ---------- */
// damage numbers — captured while the DOM still shows the pre-hit board
const _applyDmg=applyDmg;
applyDmg=function(map){
  try{ map.forEach((d,t)=>{ if(d>0)FX.pop(fxRect(cellElFor(t)),'-'+d,'dmg'); }); }catch(e){}
  _applyDmg(map);
};
// ---- battle cut-in: flash the cards involved, DS-Yu-Gi-Oh style ----
let BATTLE_CUTINS=(()=>{ try{return localStorage.getItem('srd.cutins')!=='off';}catch(e){return true;} })();
function battleCardHTML(o){ const art=o.art?cardArtImg(o):''; const tl=(typeof typeLine==='function'?typeLine(o):'');
  const lead=o.kind==='building'?(o.eff==='mana'?('◆+'+(o.val||0)):o.eff==='damage'?('⚔'+(o.val||0)):'⌂'):('⚔'+(o.a||0));
  return `<div class="bvcard ${clsOf[o.color]||''}"><div class="bvname">${o.nm}</div><div class="bvart">${art}</div>`+
    `${tl?`<div class="bvtype">${tl}</div>`:''}<div class="bvstats"><span>${lead}</span><span>♥${o.h}</span></div></div>`; }
function showBattle(A,B){
  if(!BATTLE_CUTINS)return;
  const live=arr=>(arr||[]).filter(c=>c&&(c.kind==='creature'||c.kind==='building')&&!c.worker&&!c.token);
  const a=live(A).slice(0,3), b=live(B).slice(0,3);
  if(!a.length||!b.length)return;                         // need a card on each side (creatures or a struck structure/keep)
  let ov=$('battleView'); if(!ov){ ov=document.createElement('div'); ov.id='battleView'; document.body.appendChild(ov); }
  ov.innerHTML=`<div class="bvinner"><div class="bvside left">${a.map(battleCardHTML).join('')}</div>`+
    `<div class="bvvs">⚔</div><div class="bvside right">${b.map(battleCardHTML).join('')}</div></div>`;
  ov.classList.remove('show'); void ov.offsetWidth; ov.classList.add('show');
  clearTimeout(showBattle._t); showBattle._t=setTimeout(()=>{ ov.classList.remove('show'); },1100);
}
// clash flash + sound + battle cut-in at the defender's cell
function clashFx(A,B){ try{ showBattle(A,B); SFX.clash(); FX.shake(); const b0=B&&B[0]&&cellElFor(B[0]);
  if(b0){ const r=fxRect(b0); FX.slash(r); ELEMFX.elemBurst(r, A&&A[0]&&A[0].color); } }catch(e){} }
const _resolveCombat=resolveCombat;
resolveCombat=function(A,B){ clashFx(A,B); _resolveCombat(A,B);
  // a defender that survives the clash rings out a parry — the "block" beat
  try{ const d=B&&B[0]; if(d&&d.h>0&&(d.kind==='creature'||d.kind==='building')&&!d.worker){ SFX.block(); const el=cellElFor(d); if(el){ const r=fxRect(el); FX.flash(r,'#bfe0ff',72); FX.slash(r); } } }catch(e){}
};
// every death routes through toGrave → one place for death FX
const _toGrave=toGrave;
toGrave=function(owner,obj){
  try{ if(obj){ const r=fxRect(cellElFor(obj));
    if(obj.kind==='building'){ SFX.raze(); FX.burstRect(r,'#cfcfe0',14); FX.shake(); }
    else if(obj.kind==='charge'){ SFX.trap(); FX.burstRect(r,'#9ad0e8'); }
    else if(obj.kind==='creature'){ const dc=(obj.color&&ELEMENTS[obj.color]&&ELEMENTS[obj.color].color)||'#ff9a8a';
      FX.burstRect(r, obj.worker?'#e6cf86':dc); }
  }}catch(e){}
  _toGrave(owner,obj);
};
// player strikes: wind-up lunge + arrow, then the untouched resolution
let _atkBusy=false;
function fxLunge(srcEls,tEl,after,elKey){
  const tr=fxRect(tEl);
  if(!tr||!srcEls.length){ after(); return; }
  _atkBusy=true;
  srcEls.forEach((el,i)=>setTimeout(()=>FX.flyRect(fxRect(el),tr,el.innerHTML,260),i*70));
  FX.arrow(fxRect(srcEls[0]),tr); FX.clearAim(); SFX.swing();
  try{ ELEMFX.elemShot(fxRect(srcEls[0]),tr,elKey,240); }catch(e){}
  setTimeout(()=>{ _atkBusy=false; after(); },280);
}
const fxAtkSrcs=()=>G.atk.map(s=>rowCellEl($(s.k),s.i)).filter(Boolean);
const fxAtkEl=()=>{ try{ const s=G.atk&&G.atk[0]; const u=s&&rowArr(s.k)[s.i]; return (u&&u.color)||null; }catch(e){ return null; } };
const _doAttack=doAttack;
doAttack=function(tgtKey,ti){
  if(_atkBusy)return;
  const row=$(tgtKey); const tEl=rowCellEl(row,ti);
  fxLunge(fxAtkSrcs(),tEl,()=>_doAttack(tgtKey,ti),fxAtkEl());
};
const _attackBackRow=attackBackRow;
attackBackRow=function(defOwner,col){
  if(_atkBusy)return;
  const row=$(defOwner==='foe'?'foeBack':'youBack'); const tEl=rowCellEl(row,col);
  const elKey=fxAtkEl();
  fxLunge(fxAtkSrcs(),tEl,()=>{
    const tr=fxRect(tEl), l0=G.P[defOwner].life;
    _attackBackRow(defOwner,col);
    if(G.P[defOwner].life<l0){ try{ ELEMFX.elemBurst(tr,elKey,true); FX.shake(); }catch(e){} }   // base breach — heavy
  },elKey);
};
const _attackMinionStack=attackMinionStack;
attackMinionStack=function(key,owner,which){
  if(_atkBusy)return;
  const wellId=FX_STRIP[key]||key; const s=$(wellId); const tEl=(s&&s.firstElementChild)||s;
  fxLunge(fxAtkSrcs(),tEl,()=>_attackMinionStack(key,owner,which),fxAtkEl());
};
// deploys: card flies from hand to the slot
const _place=place;
place=function(idx,mode,which,slot){
  const handEl=document.querySelector('.hc.selected');
  const row=$(mineKey(which)); const destEl=rowCellEl(row,slot);
  const fr=fxRect(handEl), tr=fxRect(destEl), html=handEl?handEl.outerHTML:null;
  const n0=G.P.you.hand.length;
  _place(idx,mode,which,slot);
  if(G.P.you.hand.length<n0){
    if(fr&&tr&&html)FX.flyRect(fr,tr,html,300);
    SFX[(mode==='set'||mode==='settrap')?'set':(mode==='build'?'raise':'place')]();
    if(tr&&mode!=='set'&&mode!=='settrap'){ FX.ring(tr);
      const o2=rowArr(mineKey(which))[slot]; const col=(o2&&o2.color&&ELEMENTS[o2.color]&&ELEMENTS[o2.color].color)||'#ffe6a8';
      FX.flash(tr,col,92); FX.burstRect(tr,col,9); }
  }
};
// flips: summon ring; big or First-Strike reveals get the Master-Duel splash
const _flip=flip;
flip=function(owner,key,slot){
  const arr=rowArr(key);
  const r=_flip(owner,key,slot);
  try{ const now=arr[slot]; const cellR=fxRect(cellElFor(now));
    if(now&&now.kind==='creature'){ SFX.summon(); FX.ring(cellR); const col=(ELEMENTS[now.color]&&ELEMENTS[now.color].color)||'#ffe6a8'; FX.flash(cellR,col,96); FX.burstRect(cellR,col,10); if(now.c>=4||now.fs)FX.splash(now,owner); }
    else if(now&&now.kind==='building'){ SFX.raise(); FX.ring(cellR); FX.flash(cellR,'#cfe3ff',96); if(now.c>=4)FX.splash(now,owner); }
  }catch(e){}
  return r;
};
// spells, traps, powers
const _castSpell=castSpell;
castSpell=function(idx,key,i){
  const n0=G.P.you.hand.length;
  _castSpell(idx,key,i);
  if(G.P.you.hand.length<n0)SFX.spell();   // themed visuals fire in the resolveSpell wrapper
};
const _springTrap=springTrap;
springTrap=function(defOwner,key,slot,attackers){
  try{ const row=$(key); ELEMFX.trapSnap(fxRect(rowCellEl(row,slot))); SFX.trap(); }catch(e){}
  _springTrap(defOwner,key,slot,attackers);
};
// moves — the biggest gap: slide sound + fly + afterimage trail (player tap/drag AND AI reposition)
const _doMove=doMove;
doMove=function(toK,toI){
  const mf=G.moveFrom; const srcEl=mf&&rowCellEl($(mf.k),mf.i); const fr=fxRect(srcEl); const html=srcEl?srcEl.innerHTML:'';
  const before=rowArr(toK)[toI];
  _doMove(toK,toI);
  const after=rowArr(toK)[toI];
  if(!before&&after&&after.kind==='creature'){ try{ const tr=fxRect(rowCellEl($(toK),toI));
    if(fr&&tr){ FX.trail(fr,tr,'rgba(180,220,255,.55)'); FX.flyRect(fr,tr,html,240); FX.ring(tr); } SFX.move(); }catch(e){} }
};
const _aiMoveCreature=aiMoveCreature;
aiMoveCreature=function(owner,fromZ,i,toZ){
  const o=rowArr(zoneKey(owner,fromZ))[i]; const srcEl=o&&rowCellEl($(zoneKey(owner,fromZ)),i); const fr=fxRect(srcEl); const html=srcEl?srcEl.innerHTML:'';
  const ok=_aiMoveCreature(owner,fromZ,i,toZ);
  if(ok&&o){ try{ const tr=fxRect(cellElFor(o)); if(fr&&tr){ FX.trail(fr,tr,'rgba(255,180,180,.5)'); FX.flyRect(fr,tr,html,240); FX.ring(tr); } SFX.move(); }catch(e){} }
  return ok;
};
// AI summon parity — the player's summon FX comes from place()/flip(); the AI enters creatures via onCreatureEnter
const _onCreatureEnter=onCreatureEnter;
onCreatureEnter=function(cr,owner){
  const r=_onCreatureEnter(cr,owner);
  if(owner==='foe'){ try{ SFX.summon(); const rr=fxRect(cellElFor(cr)); FX.ring(rr);
    const col=(cr&&ELEMENTS[cr.color]&&ELEMENTS[cr.color].color)||'#ffd98a'; FX.flash(rr,col,92); FX.burstRect(rr,col,9);
    if(cr&&cr.c>=4)FX.splash(cr,'foe'); }catch(e){} }
  return r;
};
// structure builds from the build menu (player) and the AI tech loop bypass place() — give them a construction beat
const _placeBuild=placeBuild;
placeBuild=function(which,i){
  const had=!!(cellArr('you',which)&&cellArr('you',which)[i]);
  _placeBuild(which,i);
  try{ const now=cellArr('you',which)&&cellArr('you',which)[i];
    if(now&&now.kind==='building'&&!had){ SFX.build(); const r=fxRect(cellElFor(now)); FX.ring(r); FX.flash(r,'#cfe3ff',96); FX.burstRect(r,'#cfe3ff',10); } }catch(e){}
};
const _aiBuild=aiBuild;
aiBuild=function(owner){
  const before=ownUnits(owner).filter(o=>o.kind==='building');
  const r=_aiBuild(owner);
  if(r){ SFX.build(); setTimeout(()=>{ try{ const nu=ownUnits(owner).filter(o=>o.kind==='building').find(o=>before.indexOf(o)<0);
    if(nu){ const rr=fxRect(cellElFor(nu)); FX.ring(rr); FX.flash(rr,'#cfe3ff',96); } }catch(e){} },40); }
  return r;
};
// AI spell FX — the player's castSpell wrapper covers you; resolveSpell fires the AI's (guarded by whose turn it is)
const _resolveSpell=resolveSpell;
resolveSpell=function(card,key,i){
  let tr=null, chainT=[];   // capture target rects BEFORE resolution — cleanup() re-renders the rows
  try{ tr=fxRect(rowCellEl($(key),i));
    if(card&&card.effect==='chain'){ const o=rowArr(key)[i]; if(o&&o.kind==='creature'&&!o.worker)
      chainT=liveEnemyCreatures(enemyOf(o.owner)).sort((a,b)=>(b.a-a.a)||(a.h-b.h)).slice(0,2)
        .map(t=>fxRect(cellElFor(t))).filter(Boolean); }
  }catch(e){}
  const r=_resolveSpell(card,key,i);
  if(r){ try{
    if(G.turn==='foe')SFX.spell();
    const eff=(card&&card.effect)||'', ec=card&&card.color;
    if(eff==='burn'&&tr){                       // fire comet in from the caster's side, then flame burst
      const from={left:tr.left,top:tr.top+(G.turn==='you'?170:-170),width:tr.width,height:tr.height};
      ELEMFX.elemShot(from,tr,ec||'fire',200); setTimeout(()=>ELEMFX.elemBurst(tr,ec||'fire'),200); }
    else if(eff==='raze'&&tr){ ELEMFX.elemBurst(tr,ec||'earth',true); FX.shake(); }
    else if(eff==='chain'){ (chainT.length?chainT:[tr]).forEach((t,n)=>{ if(t)setTimeout(()=>ELEMFX.elemBurst(t,'electric'),n*110); }); }
    else if(eff==='bounce'&&tr){ ELEMFX.elemBurst(tr,ec||'water'); }
    else if(tr){ FX.slash(tr); FX.burstRect(tr,'#c9a0ff'); }   // default arcane
  }catch(e){} }
  return r;
};
// player harvest lands mana — echo the AI's applyRes-driven mana beat (doHarvest + worker-tap credit P.mana directly)
const _doHarvest=doHarvest;
doHarvest=function(){ const m0=G.P.you.mana; _doHarvest(); const d=G.P.you.mana-m0; if(d>0){ SFX.mana(); FX.pop(fxRect($('youManaStr')),'+'+d,'mana'); } };
const _applyHarvest=applyHarvest;
applyHarvest=function(which,alloc,total){ const m0=G.P.you.mana; _applyHarvest(which,alloc,total); const d=G.P.you.mana-m0; if(d>0){ SFX.mana(); FX.pop(fxRect($('youManaStr')),'+'+d,'mana'); } };
// economy beats
const _applyRes=applyRes;
applyRes=function(base,owner,creature,type){
  const m0=manaTotal(owner);
  _applyRes(base,owner,creature,type);
  const d=manaTotal(owner)-m0;
  if(d>0){ SFX.mana(); FX.pop(fxRect($(owner+'ManaStr')),'+'+d,'mana'); }
};
const _trainVillager=trainVillager;
trainVillager=function(owner){
  const ok=_trainVillager(owner);
  if(ok)SFX.train();
  return ok;
};
let _fxDealing=false;
const _dealOpening=dealOpening;
dealOpening=function(o,color){ _fxDealing=true; const r=_dealOpening(o,color); _fxDealing=false; return r; };
const _drawCard=drawCard;
drawCard=function(o){ _drawCard(o); if(o==='you'&&!_fxDealing)SFX.draw(); };
// turn flow + life ticks + game over
const _startTurn=startTurn;
startTurn=function(owner){
  _startTurn(owner);
  if(owner==='you'){ FX.ribbon('YOUR TURN','var(--gold)'); SFX.turnYou(); }
  else { FX.ribbon("OPPONENT'S TURN",'var(--tide)'); SFX.turnFoe(); }
};
const _fxLife={you:null,foe:null};
const _render=render;
render=function(){
  _render(); FX.clearAim();
  ['you','foe'].forEach(o=>{ const v=G.P[o].life;
    if(_fxLife[o]!=null&&v!==_fxLife[o]){ const d=v-_fxLife[o];
      FX.pop(fxRect($(o+'Life')),(d>0?'+':'')+d, d<0?'dmg':'heal');
      if(o==='you'&&d<0){ FX.hurt(); SFX.hit(); } }
    _fxLife[o]=v; });
};
const _startGame=startGame;
startGame=function(youId,foeId,youDeck,foeDeck){
  _fxLife.you=_fxLife.foe=null;
  _startGame(youId,foeId,youDeck,foeDeck);
  SFX.shuffle(); FX.ribbon('DUEL START','var(--gold)'); SFX.turnYou();
  setTimeout(fitBoard,60);
};
const _checkWin=checkWin;
checkWin=function(){
  const was=G.over;
  _checkWin();
  if(!was&&G.over){ const win=G.P.foe.life<=0&&G.P.you.life>0;   // read the outcome from state — campResolve() may have rewritten the banner text (REGION SEIZED, etc.)
    if(win){ SFX.win(); FX.confetti(); } else SFX.lose(); }
};
// title treatment on the character-select menu
const _renderCharSel=renderCharSel;
renderCharSel=function(){
  _renderCharSel();
  const box=$('charsel').querySelector('.csbox');
  const t=document.createElement('div'); t.className='fxtitle';
  t.innerHTML='<div class="t1">SPAWN ROW DUEL</div><div class="t2">raze their base · hold the center · feed the army</div>';
  box.prepend(t);
};
// hover targeting arrow (pointer devices) — from the strike group to the hovered target
document.addEventListener('mouseover',e=>{
  if(!e.target.closest)return;
  const tgt=e.target.closest('.cell.target,.minpill.target,.crest-target');
  if(!tgt||!G.atk||!G.atk.length)return;
  const row=$(G.atk[0].k); const src=rowCellEl(row,G.atk[0].i);
  if(src)FX.aimArrow(fxRect(src),fxRect(tgt));
});
document.addEventListener('mouseout',e=>{
  if(e.target.closest&&e.target.closest('.cell.target,.minpill.target,.crest-target'))FX.clearAim();
});
// settings: gear button → overlay (volume / mute, board angle, surrender)
// Only two angles now: Top-Down and Tilted (Tilted == the former "Extreme" diorama). Any legacy
// 'tilt'/'extreme'/missing value folds into Tilted; only an explicit 'topdown' stays top-down.
let BOARD_ANGLE=(()=>{ try{return localStorage.getItem('srd.angle')==='topdown'?'topdown':'extreme';}catch(e){return 'extreme';} })();
function setBoardAngle(a){ BOARD_ANGLE=a;
  document.body.classList.remove('board-topdown','board-tilt','board-extreme'); document.body.classList.add('board-'+a);
  try{localStorage.setItem('srd.angle',a);}catch(e){}
  if(a==='extreme' && !window.SPRITES_ON){ window.SPRITES_ON=true; const sb=$('sprBtn'); if(sb)sb.classList.remove('off'); } // diorama needs the standing figures
  const grp=$('setAngles'); if(grp)grp.querySelectorAll('button').forEach(b=>b.classList.toggle('on',b.dataset.a===a));
  if(typeof render==='function')render();
  if(typeof fitBoard==='function')fitBoard(); }
function doSurrender(){ G.over=true; const ov=$('settingsOverlay'); if(ov)ov.style.display='none';
  // Surrender bypasses checkWin(), so a campaign attack's target would otherwise linger and be
  // wrongly resolved by the NEXT match's checkWin. Clear it here; route campaign quits to the map.
  if(typeof CAMPAIGN!=='undefined' && CAMPAIGN && CAMPAIGN.target!=null){ CAMPAIGN.target=null; if(typeof campSave==='function')campSave();
    if(typeof showCampaignMap==='function'){ showCampaignMap(); return; } }
  if(typeof showMainMenu==='function')showMainMenu(); }
function resetSurrenderRow(){ const row=$('setSurrenderRow'); if(!row)return;
  row.innerHTML='<button id="setSurrender" class="setsurr">🏳 Surrender / quit match</button>';
  $('setSurrender').addEventListener('click',surrenderMatch); }
function surrenderMatch(){
  // in-app confirm — a native confirm() can misbehave or close the window inside an installed PWA
  const row=$('setSurrenderRow'); if(!row){ doSurrender(); return; }
  const campaign = typeof CAMPAIGN!=='undefined' && CAMPAIGN && CAMPAIGN.target!=null;
  row.innerHTML='<div style="width:100%">'+
    '<div class="setlab" style="color:#e0a59a;margin-bottom:6px;text-align:center">Surrender this match'+(campaign?' and return to the world map?':' to the main menu?')+'</div>'+
    '<button id="setSurrYes" class="setsurr" style="margin-bottom:6px">🏳 '+(campaign?'Yes, abandon the assault':'Yes, quit to menu')+'</button>'+
    '<button id="setSurrNo" class="pclose" style="margin:0;width:100%">Cancel</button></div>';
  $('setSurrYes').addEventListener('click',doSurrender);
  $('setSurrNo').addEventListener('click',resetSurrenderRow);
}
(()=>{
  const b=document.createElement('button'); b.id='setBtn'; b.title='Settings'; b.textContent='⚙'; document.body.appendChild(b);
  const ov=document.createElement('div'); ov.id='settingsOverlay'; ov.style.display='none';
  ov.innerHTML=`<div class="setbox">
    <div class="ptitle">⚙ Settings</div>
    <div class="setrow"><span class="setlab">Volume</span><input type="range" id="setVol" min="0" max="100" value="${Math.round(SFX.getVolume()*100)}"></div>
    <div class="setrow"><span class="setlab">Sound</span><button id="setMute">${SFX.isMuted()?'🔇 Muted':'🔊 On'}</button></div>
    <div class="setrow"><span class="setlab">Board angle</span><div id="setAngles" class="setangles">
      <button data-a="topdown">Top-Down</button><button data-a="extreme">Tilted</button></div></div>
    <div class="setrow"><span class="setlab">Battle cut-ins</span><button id="setCutins">${BATTLE_CUTINS?'On':'Off'}</button></div>
    <div class="setrow" id="setSurrenderRow"><button id="setSurrender" class="setsurr">🏳 Surrender / quit match</button></div>
    <button class="pclose" id="setClose">Close</button></div>`;
  document.body.appendChild(ov);
  b.addEventListener('click',()=>{ const open=ov.style.display==='none'; ov.style.display = open ? 'flex' : 'none'; if(open)resetSurrenderRow(); });
  ov.addEventListener('click',e=>{ if(e.target===ov)ov.style.display='none'; });
  $('setClose').addEventListener('click',()=>ov.style.display='none');
  $('setVol').addEventListener('input',e=>{ SFX.setVolume(e.target.value/100); if(SFX.isMuted()&&e.target.value>0){SFX.setMuted(false);$('setMute').textContent='🔊 On';} });
  $('setMute').addEventListener('click',()=>{ const m=SFX.toggle(); $('setMute').textContent=m?'🔇 Muted':'🔊 On'; });
  $('setSurrender').addEventListener('click',surrenderMatch);
  $('setCutins').addEventListener('click',()=>{ BATTLE_CUTINS=!BATTLE_CUTINS; try{localStorage.setItem('srd.cutins',BATTLE_CUTINS?'on':'off');}catch(e){} $('setCutins').textContent=BATTLE_CUTINS?'On':'Off'; });
  $('setAngles').querySelectorAll('button').forEach(btn=>btn.addEventListener('click',()=>setBoardAngle(btn.dataset.a)));
  setBoardAngle(BOARD_ANGLE);
})();
