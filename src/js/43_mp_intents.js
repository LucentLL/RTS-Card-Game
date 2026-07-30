/* ---------- 4.6 wrappers: guest intent capture + host decision round-trips ---------- */
(function(){
  const K=MPMAP.k;
  const inMP=()=>MPNET.active&&MP.started;
  const guestTurn=()=>inMP()&&MP.role==='guest'&&G.turn==='you'&&!G.over;
  const hostTurn =()=>inMP()&&MP.role==='host' &&G.turn==='you'&&!G.over;

  // --- draw (deck click → doDraw L3977; success = phase advanced to action)
  const _doDraw=doDraw;
  doDraw=function(){ const s=guestTurn()&&G.phase==='draw'; _doDraw();
    if(s&&G.phase==='action')MP.intent({a:'draw'}); };

  // --- upkeep harvest (window.doHarvest, FX-wrapped L4808; success = phase advanced to draw)
  const _doHarvestMP=doHarvest;
  doHarvest=function(){ const s=guestTurn()&&G.phase==='upkeep'; _doHarvestMP();
    if(s&&G.phase==='draw')MP.intent({a:'harvest'}); };

  // --- single-row worker harvest (window.harvestRow L3694; success = mana grew)
  const _harvestRow=window.harvestRow;
  window.harvestRow=function(which){ const m0=G.P.you.mana; const s=guestTurn(); _harvestRow(which);
    if(s&&G.P.you.mana>m0)MP.intent({a:'harvestRow',w:which}); };

  // --- upkeep sacrifice (window.upkeepSac L3998; success = the cell emptied)
  const _upkeepSac=window.upkeepSac;
  window.upkeepSac=function(key,i){ const o=rowArr(key)&&rowArr(key)[+i]; const s=guestTurn()&&G.upkeep&&o&&o.owner==='you';
    _upkeepSac(key,i);
    if(s&&rowArr(key)[+i]!==o)MP.intent({a:'sac',k:K(key),i:+i}); };

  // --- upkeep pay (window.upkeepPay; success = the creature marked paid)
  const _upkeepPay=window.upkeepPay;
  window.upkeepPay=function(key,i){ const o=rowArr(key)&&rowArr(key)[+i]; const s=guestTurn()&&G.upkeep&&o&&o.owner==='you'&&!o.paid;
    _upkeepPay(key,i);
    if(s&&o.paid)MP.intent({a:'pay',k:K(key),i:+i}); };

  // --- hand plays (place L3364, FX-wrapped L4703; success = hand shrank). `w` is owner-relative: no mapping.
  const _placeMP=place;
  place=function(idx,mode,which,slot){ const n0=G.P.you.hand.length;
    _placeMP(idx,mode,which,slot);
    if(G.P.you.hand.length<n0){
      if(guestTurn())MP.intent({a:'place',idx,mode,w:which,i:slot});
      else if(hostTurn()&&(mode==='summon'||mode==='build'))MP.fx({ev:'enter',k:rowKeyFor('you',which),i:slot});
    } };

  // --- spells (castSpell L3445, FX-wrapped L4730; risk K — FX only after the cast verifiably resolved)
  const _castMP=castSpell;
  castSpell=function(idx,key,i){ const n0=G.P.you.hand.length; const card=G.P.you.hand[idx];
    _castMP(idx,key,i);
    if(G.P.you.hand.length<n0){
      if(guestTurn())MP.intent({a:'cast',idx,k:K(key),i});
      else if(hostTurn()&&card)MP.fx({ev:'spell',k:key,i,effect:card.effect,color:card.color||null});
    } };

  // --- moves (doMove L3777, FX-wrapped L4742; success = the same object landed at dest)
  const _doMoveMP=doMove;
  doMove=function(toK,toI){ const mf=G.moveFrom; const c=mf&&rowArr(mf.k)&&rowArr(mf.k)[mf.i]; const s=guestTurn()&&!!c;
    _doMoveMP(toK,toI);
    if(s&&rowArr(toK)[toI]===c)MP.intent({a:'move',fk:K(mf.k),fi:mf.i,tk:K(toK),ti:toI}); };

  // --- banked-mana transfer (doSendMana L3493; success = source bank emptied)
  const _doSendManaMP=doSendMana;
  doSendMana=function(toK,toI){ const mm=G.moveMana; const src=mm&&rowArr(mm.k)&&rowArr(mm.k)[mm.i]; const amt=src?src.bank:0;
    const s=guestTurn()&&amt>0;
    _doSendManaMP(toK,toI);
    if(s&&src.bank===0)MP.intent({a:'sendmana',fk:K(mm.k),fi:mm.i,tk:K(toK),ti:toI}); };

  // --- pour / flip via the charge panel (window.camtPour/camtFlip L3526–3527; `cs`,`chSel` are top-level in the same script)
  const _camtPour=window.camtPour;
  window.camtPour=function(){ const ch=chSel(); const inv0=ch?ch.inv:0; const at=cs&&{k:cs.k,i:cs.i};
    _camtPour();
    if(guestTurn()&&ch&&at&&ch.inv>inv0)MP.intent({a:'pour',k:K(at.k),i:at.i,amt:ch.inv-inv0}); };
  const _camtFlip=window.camtFlip;
  window.camtFlip=function(){ const at=cs&&{k:cs.k,i:cs.i}; const ch=chSel(); const ready=!!(ch&&ch.inv>=ch.card.c);
    _camtFlip();
    if(guestTurn()&&at&&ready)MP.intent({a:'flip',k:K(at.k),i:at.i}); };

  // --- flips fire an 'enter' FX for the guest when the HOST flips (any path, incl. provokeFaceDown)
  const _flipMP=flip;
  flip=function(owner,key,slot){ const r=_flipMP(owner,key,slot);
    if(inMP()&&MP.role==='host'&&owner==='you')MP.fx({ev:'enter',k:key,i:slot});
    return r; };

  // --- RTS build menu (placeBuild L2162, FX-wrapped L4768; capture G.build BEFORE — the original clears it)
  const _placeBuildMP=placeBuild;
  placeBuild=function(which,i){ const def=G.build;
    _placeBuildMP(which,i);
    const now=def&&cellArr('you',which)&&cellArr('you',which)[i];
    if(now&&now.kind==='building'&&now.bid===def.bid){
      if(guestTurn())MP.intent({a:'build',bid:def.bid,color:def.color||null,w:which,i});
      else if(hostTurn())MP.fx({ev:'enter',k:rowKeyFor('you',which),i});
    } };

  // --- structure upgrade (upgradeStruct): success = the unit's bid became the target tier
  const _upgradeStructMP=window.upgradeStruct;
  window.upgradeStruct=function(key,i,bid){ const o=rowArr(key)&&rowArr(key)[+i]; const before=o&&o.bid;
    _upgradeStructMP(key,i,bid);
    const now=rowArr(key)&&rowArr(key)[+i];
    if(guestTurn()&&now&&now===o&&now.bid===bid&&before!==bid)MP.intent({a:'upgrade',k:K(key),i:+i,bid,color:o.color||null});
  };

  // --- ATTACKS: guest = send-and-wait (not optimistic); host = two-phase (guest may interpose)
  MP.sendAttack=function(d){                                       // d in guest-LOCAL coords
    const atk=G.atk.map(s=>({k:K(s.k),i:s.i})); if(!atk.length)return;
    try{ let tEl=null;                                             // local wind-up FX (visual only)
      if(d.kind==='unit')tEl=rowCellEl($(d.tkL),d.ti);
      else if(d.kind==='back')tEl=rowCellEl($('foeBack'),d.col);
      else tEl=$(FX_STRIP[d.rkL])&&$(FX_STRIP[d.rkL]).firstElementChild||$(FX_STRIP[d.rkL]);
      if(tEl)fxLunge(fxAtkSrcs(),tEl,()=>{},fxAtkEl());
    }catch(e){}
    const msg={a:'attack',kind:d.kind,atk};
    if(d.kind==='unit'){msg.tk=K(d.tkL);msg.ti=d.ti;} else if(d.kind==='back'){msg.col=d.col;} else {msg.wWhich=d.wWhich;}
    MP.intent(msg); clearAtk(); render();                          // render: body.targeting + target glows must clear NOW, not at the snapshot
    MP.waitBanner('Attack sent — the opponent may interpose…');    // snapshot (or reject) unfreezes
  };
  function mpHostAttack(run,d){                                    // d: {kind,tkL?,ti?,col?,rowKey?,which?} host-LOCAL
    const attackers=selCres().filter(x=>!x.worker&&!x.sick&&!x.tapped);
    if(!attackers.length)return run();
    const aIdx=rowIdx(attackerRowKey());
    const tIdx=d.kind==='back'?-1:rowIdx(d.kind==='workers'?d.rowKey:d.tkL);   // the wall sits one row beyond foeBack
    const scour=groupIsScour(attackers);
    const blockable=(d.kind==='workers')?aIdx!==tIdx:(!scour&&aIdx!==tIdx);    // same row = point-blank
    const tgtU=d.kind==='unit'?(rowArr(d.tkL)&&rowArr(d.tkL)[d.ti]):null;
    const elig=blockable?eligibleInterceptors('you',aIdx,tIdx)
      .filter(r=>r.c!==tgtU&&!(d.kind==='workers'&&minPool('foe',d.which).includes(r.c))):[];   // attacker-owner='you'; the target can't screen itself
    MP.fx({ev:'attack',atk:G.atk.map(s=>({k:s.k,i:s.i})),kind:d.kind,
           tk:d.kind==='unit'?d.tkL:null,ti:d.ti,col:d.col,rk:d.kind==='workers'?d.rowKey:null,el:fxAtkEl()});
    if(!elig.length){ MP.forcedBlock=[]; run(); MP.pushNow(); return; }
    MP.askGuest('block',{atk:G.atk.map(s=>({k:s.k,i:s.i})),kind:d.kind,
        tk:d.kind==='unit'?d.tkL:null,ti:d.ti,col:d.col,rk:d.kind==='workers'?d.rowKey:null},
      resp=>{ MP.forcedBlock=resp.refs||[]; MP.clearWait(); run(); MP.pushNow(); });
    MP.waitBanner('Opponent may interpose…');
  }
  const _doAttackMP=doAttack;                                      // wraps the RESP-wrapped (which wraps the FX-wrapped L4679)
  doAttack=function(tgtKey,ti){
    if(!inMP())return _doAttackMP(tgtKey,ti);
    if(MP.role==='guest'){ if(guestTurn()&&G.phase==='action')MP.sendAttack({kind:'unit',tkL:tgtKey,ti}); return; }
    mpHostAttack(()=>_doAttackMP(tgtKey,ti),{kind:'unit',tkL:tgtKey,ti}); };
  const _attackBackRowMP=attackBackRow;                            // wraps the RESP-wrapped (FX L4685)
  attackBackRow=function(defOwner,col){
    if(!inMP())return _attackBackRowMP(defOwner,col);
    if(MP.role==='guest'){ if(guestTurn()&&G.phase==='action'&&defOwner==='foe')MP.sendAttack({kind:'back',col}); return; }
    mpHostAttack(()=>_attackBackRowMP(defOwner,col),{kind:'back',col}); };
  const _attackMinionStackMP=attackMinionStack;                    // wraps the RESP-wrapped (FX L4696)
  attackMinionStack=function(key,owner,which){
    if(!inMP())return _attackMinionStackMP(key,owner,which);
    const rk=WELL2ROW[key]||key;
    if(MP.role==='guest'){ if(guestTurn()&&G.phase==='action'&&owner==='foe')MP.sendAttack({kind:'workers',rkL:rk,wWhich:which}); return; }
    mpHostAttack(()=>_attackMinionStackMP(key,owner,which),{kind:'workers',rowKey:rk,which}); };

  // --- interception override: consume the guest's pre-fetched choice instead of the AI heuristic (L3616)
  const _aiChoose=aiChooseInterceptors;
  aiChooseInterceptors=function(attackers,info){
    if(inMP()){ const refs=MP.forcedBlock||[]; MP.forcedBlock=null;
      const elig=info.elig||[];
      return refs.map(r=>{ let c;
          if(r.pw!=null)c=minPool(r.po,r.pw)[r.pi]; else c=rowArr(r.k)&&rowArr(r.k)[r.i];
          return elig.find(e=>e.c===c)||null; })                   // identity match ⇒ eligibility re-validated
        .filter(Boolean);
    }
    return _aiChoose(attackers,info); };

  // --- summon-trap deferral: host summons, GUEST holds the trap (replaces auto-spring foeTrapOnSummon L3461)
  const _foeTrapMP=foeTrapOnSummon;
  foeTrapOnSummon=function(cr,w,i){
    if(!inMP())return _foeTrapMP(cr,w,i);
    if(MP.role==='guest')return;                                   // optimistic local summon: the HOST resolves this
    const t=findArmedTrap('foe','summon');
    if(!t){ MP.waitBanner('Opponent may respond…'); setTimeout(()=>{MP.clearWait();},RESP.dur()); return; }   // Step 10b: constant-time window even with no trap — the host can't read "no trap" from timing
    const arr=cellArr('you',w); if(!cr||arr[i]!==cr)return;
    MP.askGuest('trap',{w,i,cr:{nm:cr.nm,a:cr.a,h:cr.h}},resp=>{
      if(resp&&resp.spring){ const t2=findArmedTrap('foe','summon');
        if(t2&&arr[i]===cr){ log(`<span class="e">${t2.o.card.nm} springs! ${cr.nm} is dragged down as it forms.</span>`,'e');
          toGrave('you',cr); arr[i]=null;
          G.P.foe.grave.push(spellRec(t2.o.card)); cellArr('foe',t2.w)[t2.i]=null; cleanup(); checkWin(); } }
      else log('<span class="e">The opponent holds their trap.</span>','e');
      MP.clearWait(); MP.pushNow(); });
    MP.waitBanner('Opponent may spring a trap…');
  };

  // --- surrender → resign message (doSurrender L4892 bypasses checkWin; keep that, add the wire + banner)
  const _doSurrenderMP=doSurrender;
  doSurrender=function(){ if(inMP()&&!G.over){ try{MPNET.send({t:'resign'});}catch(e){}
      const ov=$('settingsOverlay'); if(ov)ov.style.display='none';
      MP.gameOverLocal(false); return; }
    _doSurrenderMP(); };

  // --- host: debounced snapshot after EVERY render (every mutation in this codebase ends in render())
  const _renderMP=render;                                          // wraps the FX-wrapped render (L4839)
  render=function(){ _renderMP();
    if(MPNET.active&&MP.role==='host'&&MP.started){ clearTimeout(MP._rt); MP._rt=setTimeout(()=>MP.pushNow(),45); } };

  // --- input freeze while awaiting the remote side (waitBanner sets MP.frozen; snapshot/reject/clearWait clear it)
  const _onCellMP=onCell;     onCell=function(k,i,o){ if(MP.frozen)return; _onCellMP(k,i,o); };
  const _onHandMP=onHand;     onHand=function(i){ if(MP.frozen)return; _onHandMP(i); };
  const _endTurnMP=endTurn;   endTurn=function(){ if(MP.frozen)return; _endTurnMP(); };
  const _doMoveFrz=doMove;    doMove=function(toK,toI){ if(MP.frozen)return; _doMoveFrz(toK,toI); };
  // #endBtn captured the ORIGINAL endTurn reference at load (L4067) — swap it for a late-bound call so the freeze gate applies
  try{ const eb=$('endBtn'); if(eb){ eb.removeEventListener('click',_endTurnMP); eb.addEventListener('click',()=>endTurn()); } }catch(e){}
})();

