/* ---------- v18: fullscreen fit, hand fan, rotate prompt ---------- */
// size cells so the entire field + fan always fits the viewport — never scroll
function fitBoard(){
  const root=document.querySelector('.wrap'); if(!root)return;
  const topChrome=6;                                            // no header/status anymore
  const handReserve=Math.min(150,Math.max(64,innerHeight*.16));  // hand peeks at the bottom; small reserve
  const boardChrome=28;                                          // mat padding + center gap
  let ch=Math.min(280,
    (innerHeight-topChrome-handReserve-boardChrome)/4.7,         // 5 rows + a little center gap — fill more vertical space
    ((innerWidth-60)/7.4)/.74);                                  // 7 columns edge to edge (worker rails are gone — they live on the walls)
  ch=Math.max(30,ch);
  root.style.setProperty('--ch',ch.toFixed(1)+'px');
  const ext=document.body.classList.contains('board-extreme');   // extreme fits itself below (projection ≠ flat layout) — the flat guards would fight it
  for(let i=0;i<12&&ch>30;i++){
    const overY=!ext && document.documentElement.scrollHeight>innerHeight+1;
    const m=document.querySelector('.main');
    const overX=!ext && m&&m.scrollWidth>m.clientWidth+1;
    if(!overY&&!overX)break;
    ch-=3; root.style.setProperty('--ch',ch.toFixed(1)+'px');
  }
  // Cell WIDTH: portrait by default, but widen cells to fill the row on wide/short screens so the board
  // stops stranding big empty side-margins (the complaint on landscape phones). Art is object-fit:cover,
  // so a wider slot just crops wider — no stretch. Floor = portrait (.74·ch); cap = 1.5·ch (mild landscape).
  const gpW=Math.min(9,Math.max(3,innerWidth*0.007));
  const rowAvail=innerWidth - 36 - 6*gpW;                        // mat width minus padding and the 6 column gaps
  const setCW=v=>{ const c=Math.max(v*0.74, Math.min(rowAvail/7, v*1.5)); root.style.setProperty('--cw', c.toFixed(1)+'px'); };
  setCW(ch);
  // Tilted (extreme): rotateX compresses the projection, which stranded big gaps above and below the
  // field. DEEPEN the cells until the projected board fills the mat's height, then scale down only if
  // the magnified near row overruns the viewport width. Measured settled each fit — adapts to any screen.
  if(ext){
    const back=document.getElementById('youBack'), mm=document.querySelector('.matmain'), mat=document.querySelector('.mat');
    if(back && mm && mat){
      const savedT=mm.style.transition; mm.style.transition='none';     // measure the SETTLED tilt, not a mid-animation frame
      root.style.setProperty('--extscale','1');
      const availW=innerWidth-10;
      // The 45° projection compresses depth ~1.4:1, so the FLAT field must be ~142% of the mat for the
      // tilted image to fill it top to bottom (board-tilt does the same trick with 106%). The rows are
      // flex children of the field, so cells size to the row boxes (offsetHeight = flat layout, not the
      // projected rect). The near row's perspective magnification then forces a uniform shrink; a
      // couple of passes grow the flat field to cancel that shrink until it converges.
      let hf=1.36, s=1;
      for(let p=0;p<3;p++){
        mm.style.height=(hf*100).toFixed(1)+'%'; void mm.offsetWidth;
        const che=Math.max(40, back.offsetHeight-2);
        root.style.setProperty('--ch',che.toFixed(1)+'px');
        root.style.setProperty('--cw', Math.min(rowAvail/7, che*1.5).toFixed(1)+'px');
        void mm.offsetWidth;
        s=Math.max(0.6, Math.min(1, availW/back.getBoundingClientRect().width));
        const want=Math.min(1.7, 1.36/s);
        if(Math.abs(want-hf)<0.02) break;
        hf=want;
      }
      root.style.setProperty('--extscale', s.toFixed(3));
      void mm.offsetWidth; mm.style.transition=savedT;
    } else root.style.setProperty('--extscale','1');
  } else { const mm=document.querySelector('.matmain'); if(mm) mm.style.height='';   // clear the extreme-mode flat-height override so Top-Down keeps its own sizing
    root.style.setProperty('--extscale','1'); }
  // the handwrap now spans the full viewport (left:0;right:0) and its .hand child self-centers,
  // so no manual horizontal offset is needed (writing inline left here would fight the CSS right:0).
}
addEventListener('resize',fitBoard);
document.addEventListener('fullscreenchange',()=>setTimeout(fitBoard,120));
// Keep the castle wall raised while the cursor is anywhere over the wall band (not just the cards),
// so you can move off the hand onto the commander / Build button without it retracting. The wall
// already rises on hand-hover and stays for a selected card; this PINS it open via body.wall-open
// for the whole bottom band and releases when the cursor moves back up onto the board. Hover devices
// only — touch keeps the existing tap-to-select behaviour (the wall holds while a card is selected).
(function(){
  if(!(window.matchMedia && matchMedia('(hover: hover)').matches)) return;
  const hand=document.getElementById('hand'), hud=document.getElementById('hudbar');
  if(!hand||!hud) return;
  let pinned=false;
  const setPinned=v=>{ if(v!==pinned){ pinned=v; document.body.classList.toggle('wall-open',v); } };
  const bandTop=()=>innerHeight-(parseFloat(getComputedStyle(hud).height)||180)-6;
  // the commander tower (Build button + worker column) stands ABOVE the wall band, so moving off the
  // hand up to it would otherwise cross bandTop and retract the wall before you reach Build. Treat the
  // whole commander cluster as part of the keep-open zone so the cursor can travel there.
  const cmd=document.querySelector('.cmdzone.you');
  const overCmd=(x,y)=>{ if(!cmd)return false; const r=cmd.getBoundingClientRect(); return r.width&&x>=r.left-8&&x<=r.right+8&&y>=r.top-8&&y<=r.bottom+8; };
  const keepOpen=e=> e.clientY>=bandTop() || overCmd(e.clientX,e.clientY) || (e.target&&e.target.closest&&!!e.target.closest('#hand,.cmdzone.you,.wallzone,.wallvit,#cardActions'));
  hand.addEventListener('pointerenter',()=>setPinned(true));   // entering the hand opens the wall and pins it
  document.addEventListener('pointermove',e=>{
    if(pinned){ if(!keepOpen(e)) setPinned(false); }                 // moved fully up onto the board (not over the tower) → release
    else if(e.clientY >= innerHeight-64) setPinned(true);            // hover anywhere over the resting wall band (rail + peek strip, full width) → reveal the wall
  });
  document.addEventListener('pointerleave',()=>setPinned(false));
  // foe wall mirrors at the top: reaching the top edge raises it (revealing deck/grave + fanning their hand), moving back down releases it
  const hudF=document.getElementById('hudbarFoe');
  if(hudF){ let fpin=false;
    const setF=v=>{ if(v!==fpin){ fpin=v; document.body.classList.toggle('foewall-open',v); } };
    const bandBot=()=>(parseFloat(getComputedStyle(hudF).height)||170)+6;
    document.addEventListener('pointermove',e=>{
      if(fpin){ if(e.clientY > bandBot()) setF(false); }
      else if(e.clientY <= 28) setF(true);
    });
  }
})();
// ---- touch + off-click castle-wall control (the hover IIFE above drives mouse). On touch there is no
//      hover, so tapping a wall band raises it; only one wall opens at a time; a tap on the empty board
//      retracts the wall AND deselects a held card (closing its summon/set menu). ----
(function(){
  const EDGE=36;
  function openWall(side){ document.body.classList.toggle('wall-open',side==='you'); document.body.classList.toggle('foewall-open',side==='foe'); }
  // TOUCH: tap the bottom edge to raise YOUR wall, the top edge to raise the FOE wall (mutually exclusive)
  document.addEventListener('pointerdown', e=>{
    if(e.pointerType==='mouse') return;
    if(e.target&&e.target.closest&&e.target.closest('button,.inspect,.wallzone,.wallvit,#cardActions')) return;
    const y=e.clientY;
    if(y >= innerHeight - EDGE) openWall('you');
    else if(y <= EDGE) openWall('foe');
  }, true);
  // OFF-CLICK (all devices): a click on the empty board retracts both walls and deselects a held card + its menu
  document.addEventListener('click', e=>{
    if(!e.target||!e.target.closest||e.target.closest('.hc,.cell,.wkslot,#cardActions,.wallzone,.wallvit,button,.inspect,#hudbar,#hudbarFoe,#settingsOverlay,#mainMenu,#charsel,#soloSelect,#deckBuilder,#buildPanel,#cpanel,#viewerPanel,#banner,.cmdzone,#mpLobby,#mpDrop,#respBar')) return;
    let re=false;
    // deselect ONLY a held hand card (closes its summon/set menu); never touch attack (G.atk) or
    // move (G.moveFrom) selections — a stray cell-miss must not cancel an attack/move mid-action.
    if(typeof G!=='undefined' && G.sel && G.sel.kind==='hand'){ G.sel=null; G.cardMenu=null; if(typeof defaultHint==='function')defaultHint(); re=true; }
    document.body.classList.remove('wall-open','foewall-open');
    if(re && typeof render==='function') render();
  });
})();
// ---- pointer drag-and-drop: drag a hand card onto a legal slot to SUMMON, or drag a placed
//      creature to a legal cell to MOVE. Reuses the click pipeline (onCell/place + startMove/doMove)
//      so every rule, mana cost, trap, win-check, animation and SFX stays identical. Pointer events
//      give mouse + touch parity (the game is played on phones). Tap still works: a drag only begins
//      once the pointer crosses a small threshold, otherwise the normal click fires.
(function(){
  if(typeof onCell!=='function'||typeof cellArr!=='function') return;
  let drag=null, justDragged=false; const TH=7;
  // On touch/pen the pointerdown target gets IMPLICIT pointer capture; startDrag() then re-renders
  // (render()/startMove) and removes that node, which fires pointercancel and self-aborts the drag.
  // Move the capture onto <html> (never removed) so the gesture survives the re-render. (Harmless on mouse.)
  const CAP=document.documentElement;
  function grabCapture(){ try{ if(drag&&drag.pid!=null&&CAP.setPointerCapture) CAP.setPointerCapture(drag.pid); }catch(_){} }
  function dropCapture(pid){ try{ if(pid!=null&&CAP.releasePointerCapture) CAP.releasePointerCapture(pid); }catch(_){} }
  function clearHover(){ document.querySelectorAll('.cell.draghover').forEach(c=>c.classList.remove('draghover')); }
  function cellUnder(x,y){
    const el=document.elementFromPoint(x,y); const hit=el&&el.closest?el.closest('.cell'):null;
    if(hit&&(hit.classList.contains('tappable')||hit.classList.contains('target'))) return hit;
    // the tilted board's 3D transform can defeat elementFromPoint — resolve against the cells'
    // projected rects instead, snapping to the nearest LEGAL cell within a small radius
    // (this also makes touch drops forgiving)
    let best=null,bd=Infinity;
    document.querySelectorAll('.cell.tappable,.cell.target').forEach(cl=>{
      const r=cl.getBoundingClientRect();
      const dx=Math.max(r.left-x,x-r.right,0), dy=Math.max(r.top-y,y-r.bottom,0);
      const d=dx*dx+dy*dy;
      if(d<bd){bd=d;best=cl;}
    });
    if(best&&bd<=44*44) return best;
    return hit;
  }
  function makeGhost(){
    const vis = drag.kind==='hand' ? drag.src : (drag.src.querySelector('.card')||drag.src);
    const r=vis.getBoundingClientRect(); const node=vis.cloneNode(true);
    if(node.classList) node.classList.remove('selected');
    const g=document.createElement('div'); g.className='dragghost';
    g.style.width=r.width+'px'; g.style.height=r.height+'px'; g.appendChild(node);
    document.body.appendChild(g); return g;
  }
  function begin(e){
    justDragged=false;
    if(!e.isPrimary || (e.button!=null&&e.button>0)) return;
    if(G.turn!=='you'||G.busy||G.over) return;
    if(G.phase==='draw'||G.phase==='end') return;   // drags only in upkeep (reposition) / action
    if(e.target.closest('.inspect')||e.target.closest('button')) return;
    const hand=e.target.closest('.hc');
    if(hand && hand.dataset.hand!=null){ if(G.phase!=='action')return; drag={kind:'hand', idx:+hand.dataset.hand, src:hand, x0:e.clientX, y0:e.clientY, on:false, pid:e.pointerId}; return; }
    const cell=e.target.closest('.cell');
    if(cell && cell.dataset.key){
      const k=cell.dataset.key, i=+cell.dataset.slot;
      const arr=rowArr(k), o=arr?arr[i]:null;
      if(o&&o.kind==='creature'&&o.owner==='you'&&typeof canMoveCard==='function'&&canMoveCard(k,i)){
        drag={kind:'board', k, i, src:cell, x0:e.clientX, y0:e.clientY, on:false, pid:e.pointerId};
      }
    }
    // RTS marquee (mouse/pen only): begin a selection box from empty board ground during the
    // action phase — sweep it over your ready creatures to pick the attack group. Touch keeps tap-select.
    if(!drag && e.pointerType!=='touch' && G.phase==='action' && !G.moveFrom && !(G.sel&&G.sel.kind==='hand')
       && e.target.closest('.mat') && !e.target.closest('.hc') && !e.target.closest('.wkslot') && !e.target.closest('#cardActions')){
      drag={kind:'marquee', x0:e.clientX, y0:e.clientY, cx:e.clientX, cy:e.clientY, on:false, pid:e.pointerId};
    }
  }
  // ---- marquee helpers ----
  function ownReadyCells(){ const out=[];
    document.querySelectorAll('.cell[data-key]').forEach(cl=>{ const k=cl.dataset.key,i=+cl.dataset.slot;
      const arr=rowArr(k), o=arr?arr[i]:null;
      if(o&&o.kind==='creature'&&o.owner==='you'&&!o.worker&&!o.sick&&!o.tapped) out.push({k,i,el:cl,r:cl.getBoundingClientRect()});
    }); return out; }
  function marqRect(){ const x=Math.min(drag.x0,drag.cx),y=Math.min(drag.y0,drag.cy);
    return {left:x,top:y,right:Math.max(drag.x0,drag.cx),bottom:Math.max(drag.y0,drag.cy)}; }
  function rectsHit(a,b){ return a.left<b.right&&a.right>b.left&&a.top<b.bottom&&a.bottom>b.top; }
  function clearMarqHi(){ document.querySelectorAll('.cell.marqhi').forEach(c=>c.classList.remove('marqhi')); }
  function updateMarquee(x,y){ drag.cx=x; drag.cy=y; const R=marqRect(); const b=drag.box;
    b.style.left=R.left+'px'; b.style.top=R.top+'px'; b.style.width=(R.right-R.left)+'px'; b.style.height=(R.bottom-R.top)+'px';
    clearMarqHi(); ownReadyCells().forEach(c=>{ if(rectsHit(c.r,R)) c.el.classList.add('marqhi'); }); }
  function finishMarquee(){ const R=marqRect();
    const hits=ownReadyCells().filter(c=>rectsHit(c.r,R));
    if(!hits.length){ if(G.atk.length){ G.atk=[]; G.cardMenu=null; if(typeof defaultHint==='function')defaultHint(); render(); } return; }
    const byRow={}; hits.forEach(c=>{ (byRow[c.k]=byRow[c.k]||[]).push(c); });   // attackers must share a row → keep the fullest
    let bestK=null,bestN=-1; for(const k in byRow){ if(byRow[k].length>bestN){bestN=byRow[k].length;bestK=k;} }
    G.atk=byRow[bestK].map(c=>({k:c.k,i:c.i})); G.cardMenu=null; G.sel=null; G.moveFrom=null; G.minSel=null;
    if(typeof setHint==='function') setHint(G.atk.length===1
      ? `<b>1</b> attacker · ⚔${sumA(selCres())} — strike any foe or their ♥ life, or use an action above the card.`
      : `<b>${G.atk.length}</b> attackers · ⚔${sumA(selCres())} combined — tap a target to strike, or tap a glowing creature to drop it.`);
    render();
  }
  function startDrag(){
    if(drag.kind==='hand'){
      const card=G.P.you.hand[drag.idx]; if(!card){ drag=null; return; }
      const mode=card.type==='building'?'build':card.type==='spell'?(card.trap?'settrap':'cast'):'summon';
      // mirror the action menu's disabled-button gate: don't begin a drag we couldn't legally drop
      if((mode==='summon'||mode==='build') && typeof canPay==='function' && !canPay('you',card)){ if(typeof setHint==='function')setHint(`<b style="color:var(--ink)">${card.nm}</b> — not enough mana.`); drag=null; return; }
      if(mode==='settrap' && manaTotal('you')<1){ if(typeof setHint==='function')setHint(`<b style="color:var(--ink)">${card.nm}</b> — setting costs ◆1 placed on the card.`); drag=null; return; }
      if(mode==='cast' && typeof spellHasTarget==='function' && !spellHasTarget(card)){ if(typeof setHint==='function')setHint(`<b style="color:var(--ink)">${card.nm}</b> — no legal target.`); drag=null; return; }
      drag.on=true; drag.ghost=makeGhost(); document.body.classList.add('dragging');
      grabCapture();                                            // keep the gesture alive through the re-render below (touch)
      G.sel={kind:'hand',idx:drag.idx,mode}; G.cardMenu=null; G.atk=[]; G.moveFrom=null; G.minSel=null;
      render();                                                 // lights the legal target slots
    } else if(drag.kind==='marquee'){
      drag.on=true; document.body.classList.add('marqueeing');
      drag.box=document.createElement('div'); drag.box.className='marquee'; document.body.appendChild(drag.box);
      grabCapture(); updateMarquee(drag.cx,drag.cy);
    } else {
      drag.on=true; drag.ghost=makeGhost(); document.body.classList.add('dragging');
      grabCapture();                                            // keep the gesture alive through startMove's re-render (touch)
      window.startMove(drag.k, drag.i);                         // sets G.moveFrom + lights legal destinations
    }
  }
  function dropOn(e){
    const c=cellUnder(e.clientX,e.clientY);
    if(c && c.dataset.key){
      const k=c.dataset.key, i=+c.dataset.slot;
      if(drag.kind==='hand'){ const arr=rowArr(k), occ=arr?arr[i]:null; onCell(k,i,occ); return true; }
      if(c.classList.contains('tappable')){ doMove(k,i); return true; }   // board move: only onto a lit legal destination
      return false;                                                        // otherwise fall through and cancel the move
    }
    return false;
  }
  function cancelSel(){
    if(drag.kind==='hand'){ G.sel=null; G.cardMenu=null; if(typeof defaultHint==='function')defaultHint(); render(); }
    else if(window.cancelMove){ window.cancelMove(); }
  }
  function end(e,aborted){
    const pid=drag&&drag.pid;
    if(drag && drag.on){
      if(drag.kind==='marquee'){
        drag.box&&drag.box.remove(); document.body.classList.remove('marqueeing'); clearMarqHi();
        if(!aborted) finishMarquee();
      } else {
        drag.ghost&&drag.ghost.remove(); document.body.classList.remove('dragging'); clearHover();
        let ok=false; try{ if(!aborted&&e) ok=dropOn(e); }catch(_){ ok=false; }
        if(!ok) cancelSel();
      }
      justDragged=true;                                         // suppress the click this pointer sequence emits
    }
    drag=null; dropCapture(pid);
  }
  document.addEventListener('pointerdown', begin, true);
  const mine=e=>drag && (drag.pid==null || e.pointerId===drag.pid);  // only the pointer that began (and holds capture) drives the drag — a 2nd finger can't hijack or end it
  document.addEventListener('pointermove', e=>{
    if(!mine(e)) return;
    if(!drag.on){ const th=(e.pointerType==='touch')?15:TH; if(Math.abs(e.clientX-drag.x0)+Math.abs(e.clientY-drag.y0)>th) startDrag(); else return; }  // higher threshold on touch so a tap (to select/attack) isn't read as a drag-to-move
    if(!drag||!drag.on) return;                                 // startDrag may have aborted (unaffordable) → drag is null
    e.preventDefault();
    if(drag.kind==='marquee'){ updateMarquee(e.clientX,e.clientY); return; }
    drag.ghost.style.left=e.clientX+'px'; drag.ghost.style.top=e.clientY+'px';
    clearHover(); const c=cellUnder(e.clientX,e.clientY);
    if(c&&(c.classList.contains('tappable')||c.classList.contains('target'))) c.classList.add('draghover');
  }, {passive:false});
  document.addEventListener('pointerup', e=>{ if(mine(e)) end(e,false); });
  document.addEventListener('pointercancel', e=>{ if(mine(e)) end(e,true); });
  document.addEventListener('click', e=>{ if(justDragged){ justDragged=false; e.stopPropagation(); e.preventDefault(); } }, true);
})();
// ---- hover-to-inspect (fine pointers only): sweep the mouse over a hand card or an occupied
//      cell and its full inspectRef/inspectHand write-up appears in the left panel, non-blocking.
//      ONE delegated mouseover listener (cells are rebuilt every render — per-node listeners die;
//      the host._inspect closure set by addInspect is re-set each render, so delegation survives).
(function(){
  if(!FINE_POINTER) return;                       // touch keeps the ⓘ-tap → modal path untouched
  const SHOW_MS=180, HIDE_MS=120;
  let showT=null, hideT=null, curKey=null;
  const vp=()=>$('viewerPanel');
  const SCREENS=['mainMenu','charsel','soloSelect','deckBuilder','campaign','banner','buildPanel',
                 'settingsOverlay','cpanel','rulesPanel','logPanel','contestPanel','harvestPanel','mpLobby','mpDrop'];
  function modalOpen(){                           // a REAL modal (deck/grave viewer, tapped inspect) — never fight it
    const p=vp(); return p && p.style.display==='flex' && !p.classList.contains('hover');
  }
  function suppressed(){
    if(document.body.classList.contains('dragging')) return true;
    if(typeof G!=='undefined'&&G.over) return true;
    if(modalOpen()) return true;
    const m=$('cardActions'); if(m&&m.style.display==='block') return true;
    return SCREENS.some(id=>{const e=$(id); return e&&getComputedStyle(e).display!=='none';});
  }
  function hideNow(){
    clearTimeout(showT); showT=null; clearTimeout(hideT); hideT=null; curKey=null;
    const p=vp(); if(p&&p.classList.contains('hover')){ p.style.display='none'; p.classList.remove('left','hover'); }
  }
  function hoverTargetOf(node){
    if(!node||node.nodeType!==1||!node.closest) return null;
    const t=node.closest('.hc,.cell');
    return (t&&t._inspect)?t:null;                // _inspect exists only on inspectable (occupied) hosts
  }
  const keyOf=el=> el.dataset.hand!=null ? 'h'+el.dataset.hand : el.dataset.key+'|'+el.dataset.slot;
  document.addEventListener('mouseover',e=>{
    const t=hoverTargetOf(e.target);
    if(!t){                                       // over nothing inspectable → hide after a short grace
      if(curKey!=null||showT){ clearTimeout(showT); showT=null; curKey=null;
        clearTimeout(hideT); hideT=setTimeout(hideNow,HIDE_MS); }
      return;
    }
    if(suppressed()){ hideNow(); return; }
    clearTimeout(hideT); hideT=null;
    const k=keyOf(t);
    if(k===curKey) return;                        // already shown / pending for this card
    clearTimeout(showT); curKey=k;
    const fn=t._inspect;
    showT=setTimeout(()=>{                        // show delay kills flicker on a board sweep
      showT=null;
      if(suppressed()){ curKey=null; return; }
      window.__hoverInspecting=true;
      try{ fn(); }finally{ window.__hoverInspecting=false; }
    },SHOW_MS);
  });
  document.addEventListener('mouseout',e=>{       // pointer left the window entirely
    if(!e.relatedTarget){ clearTimeout(showT); showT=null; curKey=null;
      clearTimeout(hideT); hideT=setTimeout(hideNow,HIDE_MS); }
  });
  // any click that lands in a suppressed state (card menu opened, deck viewer opened, drag ended
  // on a modal…) sweeps a lingering hover panel away; hideNow never touches a real modal.
  document.addEventListener('click',()=>{ if(suppressed()) hideNow(); });
})();
// stack the resting hand left-to-right so the peek headers overlap cleanly (the fan arc is
// dropped — it fought the flat name+cost peek strip; per-card hover/select still pops on top)
const _renderHand_v18=renderHand;
renderHand=function(){
  _renderHand_v18();
  const el=$('hand'); const kids=[...el.children];
  kids.forEach((c,i)=>{ c.style.zIndex=10+i; });
};
// first press on the start menu → fullscreen + landscape lock, like a real client
(function(){
  let armed=true;
  function goFS(){
    if(!armed)return; armed=false;
    try{ const el=document.documentElement;
      const req=el.requestFullscreen||el.webkitRequestFullscreen;
      if(req){ const pr=req.call(el); if(pr&&pr.catch)pr.catch(()=>{}); }
    }catch(e){}
    try{ if(screen.orientation&&screen.orientation.lock){ const pr=screen.orientation.lock('landscape'); if(pr&&pr.catch)pr.catch(()=>{}); } }catch(e){}
    setTimeout(fitBoard,360);
  }
  ['mainMenu','soloSelect','deckBuilder','charsel'].forEach(id=>{const el=$(id); if(el)el.addEventListener('pointerup',goFS,{capture:true});});
})();
// portrait touch devices get a rotate prompt instead of a broken squeeze
(function(){
  const d=document.createElement('div'); d.id='rotateNote';
  d.innerHTML='<div><div style="font-size:34px;margin-bottom:10px">⟳</div>ROTATE YOUR DEVICE<div style="font-family:\'EB Garamond\',serif;font-style:italic;font-size:13px;color:var(--ink-dim);letter-spacing:0;text-transform:none;margin-top:8px">Spawn Row Duel plays in landscape.</div></div>';
  document.body.appendChild(d);
  const css=document.createElement('style');
  css.textContent='#rotateNote{position:fixed;inset:0;z-index:80;display:none;align-items:center;justify-content:center;background:rgba(4,3,8,.96);text-align:center;font-family:\'Cinzel\',serif;color:var(--gold);font-size:17px;letter-spacing:.14em;padding:30px;}@media (orientation:portrait) and (pointer:coarse){#rotateNote{display:flex;}}';
  document.head.appendChild(css);
})();
fitBoard();
/* ============================ end v16 layer ============================ */

