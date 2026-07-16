/* ═══════════ PAUSE-TO-RESPOND · DotP-style priority windows (RESP layer) ═══════════
   Must load AFTER the FX wrappers: these re-bindings intentionally wrap the FX-wrapped
   doAttack/attackBackRow/attackMinionStack, exactly like the FX layer wraps the originals. */
(function(){
  const RM=(window.matchMedia&&matchMedia('(prefers-reduced-motion: reduce)').matches)||false;
  let RESP_SETTING=(()=>{ try{return localStorage.getItem('srd.respwin')||'4';}catch(e){return '4';} })(); // 'off'|'3'|'4'|'6'
  const NETON=()=>!!(typeof MPNET!=='undefined'&&MPNET.active&&typeof MP!=='undefined'&&MP.started);

  /* all armed traps for a trigger — plural sibling of findArmedTrap (L3453), same arming rules */
  function findArmedTraps(owner,trigger){
    const out=[];
    for(const w of ['front','back']) for(let i=0;i<SLOTS;i++){ const o=G.P[owner][w][i];
      if(o&&o.kind==='trap'&&o.card.trigger===trigger&&G.turnNo>(o.setTurn??0)) out.push({o,w,i}); }
    for(let i=0;i<SLOTS;i++){ const o=G.center[i];
      if(o&&o.kind==='trap'&&o.owner===owner&&o.card.trigger===trigger&&G.turnNo>(o.setTurn??0)) out.push({o,w:'center',i}); }
    return out;
  }

  const RESP={active:false,_t:null,_tick:null,_net:null};
  window.RESP=RESP;
  /* MP is ALWAYS 4s (the anti-tell guarantee); SP honours the setting */
  RESP.dur=function(){ if(NETON())return 4000; return RESP_SETTING==='off'?0:(+RESP_SETTING)*1000; };

  function barEl(){ let el=document.getElementById('respBar');
    if(!el){ el=document.createElement('div'); el.id='respBar'; document.body.appendChild(el); } return el; }
  function countdownHTML(d){ return d>0?(RM?`<div class="respnum">${Math.ceil(d/1000)}s</div>`
      :`<div class="resptrack"><div class="respfill" style="animation:respShrink ${d}ms linear forwards"></div></div>`):''; }
  function startNum(el,d){ if(!RM||d<=0)return; const t0=performance.now();     // reduced motion: numeric tick, no bar
    RESP._tick=setInterval(()=>{ const n=el.querySelector('.respnum');
      if(n)n.textContent=Math.ceil(Math.max(0,d-(performance.now()-t0))/1000)+'s'; },250); }
  RESP._hide=function(){ const el=document.getElementById('respBar'); if(el)el.style.display='none';
    clearTimeout(RESP._t); RESP._t=null; clearInterval(RESP._tick); RESP._tick=null; RESP._net=null; };

  /* ---------- ACTING side: slim pill, countdown, no buttons — you committed ---------- */
  RESP.actingGate=function(trigger,then){
    if(G.over||RESP.active)return;               // re-entrancy: never a window inside a window
    if(G.turn!=='you'){ then(null); return; }    // gate only genuine player actions
    if(NETON()){ then(null); return; }           // MP: the MP layer owns response windows (host askGuest round-trips)
    const d=RESP.dur();
    if(d<=0){ then(null); return; }              // SP 'Off' -> classic instant flow
    RESP.active=true; G.busy=true;
    const el=barEl(); el.className=''; el.style.display='flex';
    el.innerHTML=`<div class="resplab">Opponent may respond…</div>${countdownHTML(d)}`;
    startNum(el,d); try{SFX.set();}catch(e){}
    render();
    const finish=resp=>{ if(!RESP.active)return; RESP._hide(); RESP.active=false; G.busy=false;
      if(!G.over)then(resp||null); render(); };
    RESP._t=setTimeout(()=>finish(null),d);      // SP timer only — the NETON branch and RESP.netResolve are deleted
    /* SP anti-tell: the AI's answer (auto-spring inside `then`) executes EXACTLY at window end,
       whether or not it holds a trap — findArmedTrap runs only inside then(). Constant timing. */
  };

  /* ---------- DEFENDING side: RESPOND? + one button per armed trap + ⏸ Pause + Pass ----------
     The short window is only for DECLARING intent; nobody should have to weigh a choice in 4s.
     ⏸ Pause swaps in a fresh 15s timer to actually think. MP budget: the host's 'trap'
     askGuest auto-pass is 21.5s (L6167) — 4s window + 15s pause + latency resolves in time. */
  RESP.defendWindow=function(trigger,ctx){
    return new Promise(resolve=>{
      if(G.over){resolve(null);return;}
      if(RESP._defendDone)RESP._defendDone(null);   // a new window takes over: settle the old one as a pass (its promise must not strand)
      const traps=findArmedTraps('you',trigger);
      const d=RESP.dur();
      if(d<=0&&!NETON()&&!traps.length){ resolve(null); return; }   // SP 'Off': only prompt real choices
      RESP.active=true;
      const el=barEl(); el.className='defend'; el.style.display='flex';
      const done=ref=>{ RESP._defendDone=null; RESP._hide(); RESP.active=false; resolve(ref); };   // callers own transport (mpGuestDecide sends {t:'resp',id,...})
      RESP._defendDone=done;
      const PAUSE_MS=15000;
      const show=(ms,paused)=>{
        clearTimeout(RESP._t); RESP._t=null; clearInterval(RESP._tick); RESP._tick=null;
        el.innerHTML=`<div class="resplab">${traps.length?(paused?'PAUSED — take your time':'RESPOND?'):'Opponent acts…'}</div>`+
          (ctx&&ctx.desc?`<div class="respdesc">${ctx.desc}</div>`:'')+
          `<div class="respacts">${traps.map((t,n)=>`<button class="trapbtn" data-n="${n}">⚠ ${t.o.card.nm}</button>`).join('')}`+
          (traps.length&&!paused&&ms>0?`<button class="pausebtn">⏸ Pause</button>`:'')+
          `<button class="passbtn">Pass</button></div>${countdownHTML(ms)}`;
        startNum(el,ms); try{SFX.set();}catch(e){}
        el.querySelectorAll('.trapbtn').forEach(b=>b.addEventListener('click',()=>done(traps[+b.dataset.n])));
        el.querySelector('.passbtn').addEventListener('click',()=>done(null));
        const pb=el.querySelector('.pausebtn');
        if(pb)pb.addEventListener('click',()=>show(PAUSE_MS,true));  // fresh 15s to think it over
        if(ms>0)RESP._t=setTimeout(()=>done(null),ms);               // timeout = auto-pass
      };
      show(d,false);
    });
  };

  /* validate a remote/late {w,i} intent against live state (host-side MP + safety) */
  RESP.refFrom=function(owner,wi){ if(!wi)return null; const o=cellArr(owner,wi.w)[wi.i];
    return (o&&o.kind==='trap')?{o,w:wi.w,i:wi.i}:null; };

  /* ref-taking clone of springAttackTrap (L3657) — the window already chose WHICH trap */
  RESP.springAttackTrapRef=function(defOwner,t,attackers,defender){
    if(!t||cellArr(defOwner,t.w)[t.i]!==t.o)return;   // consumed/moved since the window — hold
    const card=t.o.card; const ey=defOwner==='you'?'y':'e';
    log(`<span class="${ey}">${card.nm} springs as ${defOwner==='you'?'your':'their'} line is struck!</span>`,ey);
    if(card.effect==='thornmail'){ if(defender&&defender.kind==='creature'&&!defender.cc){ defender.a+=500; defender.maxh+=1000; defender.h+=1000; log(`&nbsp;&nbsp;${defender.nm} hardens to ⚔${defender.a}/♥${defender.h}.`); } }
    else if(card.effect==='burn'){ attackers.forEach(a=>{ if(a)a.h-=(card.val||0); }); log(`&nbsp;&nbsp;${card.val} damage to ${attackers.length} attacker(s).`); }
    G.P[defOwner].grave.push(spellRec(card)); cellArr(defOwner,t.w)[t.i]=null;
  };

  /* ---------- input lock: onCell/onHand don't check G.busy on your own turn ---------- */
  const _onCell_R=onCell;   onCell=function(k,i,o){ if(RESP.active)return; _onCell_R(k,i,o); };
  const _onHand_R=onHand;   onHand=function(i){ if(RESP.active)return; _onHand_R(i); };
  /* (startMove/openCharge/youDeckClick/endTurn/startSendMana already check G.busy — covered.) */

  /* ---------- RP-3: your attack declarations — window BEFORE the fxLunge/resolution ---------- */
  const _doAttack_R=doAttack;
  doAttack=function(tgtKey,ti){ RESP.actingGate('attack',()=>_doAttack_R(tgtKey,ti)); };
  const _attackBackRow_R=attackBackRow;
  attackBackRow=function(defOwner,col){ RESP.actingGate('attack',()=>_attackBackRow_R(defOwner,col)); };
  const _attackMinionStack_R=attackMinionStack;
  attackMinionStack=function(key,owner,which){ RESP.actingGate('attack',()=>_attackMinionStack_R(key,owner,which)); };
  /* G.atk survives the window (inputs locked), so the FX layer's fxAtkSrcs()/original doAttack
     read the same selection after the pause. AI trap auto-spring (springAttackTrap('foe',…) at
     L3808 inside the original) is UNCHANGED — it now simply fires after a constant-length pause. */

  /* ---------- RP-1: your summons — defer the foe's auto-spring to window end ---------- */
  const _foeTrapOnSummon_R=foeTrapOnSummon;
  foeTrapOnSummon=function(cr,w,i){
    RESP.actingGate('summon',()=>{ _foeTrapOnSummon_R(cr,w,i); render(); checkWin(); });
  };

  /* ---------- RP-2: AI summons — replace the contestPanel modal with the always-shown bar ---------- */
  playerTrapOnSummon=function(cr,w,i){
    const arr=cellArr('foe',w);
    return RESP.defendWindow('summon',{desc:`Opponent summons <b>${cr.nm}</b> (⚔${cr.a}/♥${cr.h}).`}).then(ref=>{
      if(!ref)return;
      if(!cr||arr[i]!==cr)return;                                   // state changed — hold (inputs are locked, shouldn't happen)
      log(`<span class="y">${ref.o.card.nm} springs — ${cr.nm} is destroyed!</span>`,'y');
      toGrave('foe',cr); arr[i]=null;
      G.P.you.grave.push(spellRec(ref.o.card)); cellArr('you',ref.w)[ref.i]=null; cleanup(); render();
    });
  };

  /* ---------- settings row (existing .setrow / .setangles pattern, reuses button.on styling) ---------- */
  (function(){
    const box=document.querySelector('#settingsOverlay .setbox'); if(!box)return;
    const row=document.createElement('div'); row.className='setrow';
    row.innerHTML=`<span class="setlab">Response window</span><div class="setangles" id="setResp">
      <button data-r="off">Off</button><button data-r="3">3s</button><button data-r="4">4s</button><button data-r="6">6s</button></div>`;
    box.insertBefore(row,document.getElementById('setSurrenderRow'));
    const paint=()=>row.querySelectorAll('button').forEach(b=>b.classList.toggle('on',b.dataset.r===RESP_SETTING));
    row.querySelectorAll('button').forEach(b=>b.addEventListener('click',()=>{
      if(NETON())return;                                            // MP: always on — the setting is SP-only
      RESP_SETTING=b.dataset.r; try{localStorage.setItem('srd.respwin',RESP_SETTING);}catch(e){} paint(); }));
    paint();
  })();
})();
