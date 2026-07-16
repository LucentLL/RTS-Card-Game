/* ---------- 4.2 MPMAP: guest↔host perspective mirror — row + well keys flip, slot indices map identity ---------- */
const MPMAP=(function(){
  const M={youBack:'foeBack',foeBack:'youBack',youFront:'foeFront',foeFront:'youFront',center:'center',
           wellYouBack:'wellFoeBack',wellFoeBack:'wellYouBack',wellYouFront:'wellFoeFront',wellFoeFront:'wellYouFront',wellCenter:'wellCenter'};
  return {k:key=>M[key]||key};
})();

/* ---------- 4.3 MPSER: canonical snapshots (host coords, art-stripped) + wholesale adoption (guest, sides swapped) ---------- */
const MPSER=(function(){
  function strip(o){                                   // null out .art everywhere (rebuilt locally); tokens keep theirs (no registry entry)
    if(Array.isArray(o)){ o.forEach(strip); return; }
    if(!o||typeof o!=='object')return;
    if('art' in o&&o.art&&!o.token)o.art=null;
    for(const k in o){ const v=o[k]; if(v&&typeof v==='object')strip(v); }
  }
  function artFor(rec){ try{
      if(rec.worker)return ART.villager;
      if((rec.kind||rec.type)==='building'&&rec.bid){ const d=resolveStruct(rec.bid,rec.color||null); return (d&&d.art)||null; }
      const e=CARD_BY_KEY[(rec.color||'neutral')+'|'+rec.nm]||CARD_BY_KEY['neutral|'+rec.nm];
      return (e&&e.tpl&&e.tpl.art)||null;
    }catch(e){ return null; } }
  function rehydrate(o){
    if(Array.isArray(o)){ o.forEach(rehydrate); return; }
    if(!o||typeof o!=='object')return;
    if('art' in o&&!o.art&&rec2(o))o.art=artFor(o);
    for(const k in o){ const v=o[k]; if(v&&typeof v==='object')rehydrate(v); }
  }
  function rec2(o){ return !!o.nm; }                   // only card-shaped records get a lookup
  function pSnap(o){ const P=G.P[o];
    return {color:P.color,cc:P.cc,life:P.life,mana:P.mana,cmana:P.cmana,hand:P.hand,deck:P.deck,grave:P.grave,
      front:P.front,back:P.back,min:P.min,firstExtract:P.firstExtract,villagerUsed:P.villagerUsed,upaid:P.upaid}; }
  function snapshot(){                                  // host-canonical duel state, deep-copied + art-stripped
    const s=JSON.parse(JSON.stringify({turn:G.turn,over:G.over,turnNo:G.turnNo,phase:G.phase,uid,
      center:G.center,P:{you:pSnap('you'),foe:pSnap('foe')}}));
    strip(s); return s;
  }
  function adopt(st){                                   // GUEST: wholesale replace — host 'you' becomes local 'foe' and vice versa
    if(!st)return;
    const wasTurn=G.turn;
    const S=JSON.parse(JSON.stringify(st));             // never alias the wire/pending object
    rehydrate(S);
    if(S.uid&&S.uid>uid)uid=S.uid;                      // keep local uid++ ids clear of adopted ones
    Object.assign(G.P.you,S.P.foe); Object.assign(G.P.foe,S.P.you);
    G.center=S.center;
    G.center.forEach(o=>{ if(o&&o.owner)o.owner=(o.owner==='you'?'foe':'you'); });
    ['you','foe'].forEach(o=>{ const P=G.P[o];
      ['front','back'].forEach(w=>P[w].forEach(u=>{ if(u&&u.owner)u.owner=o; }));
      ['back','front','center'].forEach(w=>P.min[w].forEach(u=>{ if(u)u.owner=o; })); });
    G.turn=(S.turn==='you')?'foe':'you';
    G.over=!!S.over; G.turnNo=S.turnNo; setPhase(S.phase);
    G.sel=null;G.atk=[];G.moveFrom=null;G.moveMana=null;G.cardMenu=null;G.build=null;G.minSel=null;G.busy=false;
    MP.clearWait();                                     // any send-and-wait freeze ends when the authoritative board lands
    try{ const _cp=$('contestPanel'); if(_cp&&_cp.style.display!=='none')_cp.style.display='none'; }catch(e){}  // close a stale block modal if the host resolved before we answered
    if(G.turn!==wasTurn&&!G.over){ try{                 // risk T: the flip arrives inside a snapshot — fire the ribbon here
      if(G.turn==='you'){ FX.ribbon('YOUR TURN','var(--gold)'); SFX.turnYou(); if(G.phase==='upkeep')upkeepNext(); }
      else { FX.ribbon("OPPONENT'S TURN",'var(--tide)'); SFX.turnFoe(); } }catch(e){} }
    else if(G.turn==='you'&&G.phase==='upkeep'&&!G.over){ try{ upkeepNext(); }catch(e){} }   // same-turn snapshot (host echoing our pay/sac/move) — re-pop the settle menu it just wiped
    render(); checkWin();
  }
  return {snapshot,adopt};
})();

/* ---------- 4.4 MP core: roles, counters, asks, freeze, status ---------- */
const MP={role:null,started:false,resume:false,pickingDeck:false,myPick:null,peerPick:null,
  lastQ:0,qOut:0,sv:0,svIn:0,_lastSnap:null,_pend:null,_st:null,_rt:null,rtt:null,_ping:null,_lastPong:0,
  _asks:{},_askId:0,forcedBlock:null,frozen:false,fxUntil:0,postFx:[],_dropSuppressed:false};
MP.intent=function(m){ if(MP.role!=='guest'||!MPNET.active)return; MPNET.send({t:'intent',q:++MP.qOut,...m}); };
MP.pushNow=function(){ if(!MPNET.active||MP.role!=='host'||!MP.started)return;   // ALWAYS sends (risk F: rejects must heal the guest too)
  const snap=MPSER.snapshot(); MP._lastSnap=JSON.stringify(snap);
  MPNET.send({t:'snapshot',sv:++MP.sv,state:snap}); };
MP.fx=function(ev){ if(MPNET.active&&MP.role==='host'&&MP.started)MPNET.send({t:'fx',ev}); };   // host→guest FX relay: the passive side replays attack/impact/spell/enter animations (mpReplayFx) instead of teleporting state
MP.holdFx=function(ms){ MP.fxUntil=Math.max(MP.fxUntil,Date.now()+Math.min(ms,1200)); };
MP.drainPostFx=function(){ const q=MP.postFx; MP.postFx=[];
  q.forEach(ev=>{ try{ const k=MPMAP.k(ev.k); const el=rowCellEl($(k),ev.i); const r=fxRect(el);
    const u=rowArr(k)[ev.i]; const col=(u&&u.color&&ELEMENTS[u.color]&&ELEMENTS[u.color].color)||'#ffe6a8';
    FX.ring(r); FX.flash(r,col,92); SFX.summon(); }catch(e){} }); };
MP.waitBanner=function(txt){ MP.frozen=true; try{setHint('<b style="color:var(--tide)">'+txt+'</b>');}catch(e){} };
MP.clearWait=function(){ MP.frozen=false; try{if(typeof G!=='undefined'&&!G.over)defaultHint();}catch(e){} };
MP.status=function(txt){ const el=$('mpStatus'); if(el)el.textContent=txt; };
MP.reset=function(){ try{MPNET.close();}catch(e){} clearInterval(MP._ping); MP._ping=null;
  clearTimeout(MP._rt); clearTimeout(MP._st); clearTimeout(MP._linkT); try{MPSIG.stop();}catch(e){}
  Object.keys(MP._asks).forEach(id=>{ const a=MP._asks[id]; if(a&&a.timer)clearTimeout(a.timer); });
  Object.assign(MP,{role:null,started:false,resume:false,pickingDeck:false,myPick:null,peerPick:null,
    lastQ:0,qOut:0,sv:0,svIn:0,_lastSnap:null,_pend:null,rtt:null,_lastPong:0,_asks:{},_askId:0,
    forcedBlock:null,frozen:false,fxUntil:0,postFx:[],_dropSuppressed:false});
  const c=$('mpLink'); if(c)c.style.display='none'; };
MP.softReset=function(){ clearTimeout(MP._rt); clearTimeout(MP._st);   // game bookkeeping only — role/peer/net survive (risk N)
  Object.keys(MP._asks).forEach(id=>{ const a=MP._asks[id]; if(a&&a.timer)clearTimeout(a.timer); });
  Object.assign(MP,{started:false,lastQ:0,qOut:0,sv:0,svIn:0,_lastSnap:null,_pend:null,
    _asks:{},_askId:0,forcedBlock:null,frozen:false,fxUntil:0,postFx:[]}); };
MP.abort=function(msg){ try{MPNET.send({t:'bye'});}catch(e){} MP.reset();
  showScreen('mpLobby'); const mr=$('mpModeRow'); if(mr)mr.style.display='flex';
  if(typeof mpAutoBusy==='function')mpAutoBusy(false);
  MP.status(msg); };
MP.gameOverLocal=function(win){ if(!G.over){ G.P[win?'foe':'you'].life=0; checkWin(); } };
MP.askGuest=function(what,data,cb){
  const id=++MP._askId;
  const t=setTimeout(()=>{ const a=MP._asks[id]; delete MP._asks[id];
    if(a){ MP.clearWait(); a.cb(what==='block'?{refs:[]}:{spring:false}); } },     // host-authoritative auto-pass (risk D)
    what==='trap'?21500:25000);   // trap: 4s window + 15s ⏸ pause + 2.5s latency — the guest's answer must beat this
  MP._asks[id]={cb,timer:t};
  MPNET.send({t:'respWindow',what,id,data});
};

