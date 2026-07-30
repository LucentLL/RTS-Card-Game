/* ---------- 4.7 guest FX replay + decisions + protocol pump + lobby ---------- */
function mpReplayFx(ev){ try{
  const LK=MPMAP.k;
  if(ev.ev==='attack'){
    const srcs=(ev.atk||[]).map(s=>rowCellEl($(LK(s.k)),s.i)).filter(Boolean);
    let tEl=null;
    if(ev.kind==='back')tEl=rowCellEl($('youBack'),ev.col);
    else if(ev.kind==='workers'){const w=$(FX_STRIP[LK(ev.rk)]);tEl=(w&&w.firstElementChild)||w;}
    else tEl=rowCellEl($(LK(ev.tk)),ev.ti);
    MP.holdFx(650);
    if(srcs.length&&tEl)fxLunge(srcs,tEl,()=>{},ev.el||null);
    if(tEl)setTimeout(()=>{try{ELEMFX.elemBurst(fxRect(tEl),ev.el||null);FX.shake();}catch(e){}},300);
  } else if(ev.ev==='impact'){
    let el=ev.well?(($(FX_STRIP[LK(ev.k)])||{}).firstElementChild||$(FX_STRIP[LK(ev.k)])):rowCellEl($(LK(ev.k)),ev.i);
    MP.holdFx(350); if(el){ELEMFX.elemBurst(fxRect(el),ev.el||null);FX.shake();}
  } else if(ev.ev==='spell'){
    const el=rowCellEl($(LK(ev.k)),ev.i); MP.holdFx(430);
    if(el){ const r=fxRect(el); SFX.spell();
      if(ev.effect==='burn'){ELEMFX.elemShot({left:r.left,top:r.top-170,width:r.width,height:r.height},r,ev.color||'fire',200);
        setTimeout(()=>ELEMFX.elemBurst(r,ev.color||'fire'),200);}
      else if(ev.effect==='raze'){ELEMFX.elemBurst(r,ev.color||'earth',true);FX.shake();}
      else if(ev.effect==='chain'){ELEMFX.elemBurst(r,'electric');}
      else if(ev.effect==='bounce'){ELEMFX.elemBurst(r,ev.color||'water');}
      else {FX.slash(r);FX.burstRect(r,'#c9a0ff');} }
  } else if(ev.ev==='enter'){ MP.postFx.push(ev); }                // ring AFTER the snapshot lands (unit must exist)
}catch(e){} }

async function mpGuestDecide(m){                                   // respWindow handler (guest)
  if(m.what==='trap'){                                             // Step 10a: the timed RESP bar replaces the old contestPanel modal
    const ref=await RESP.defendWindow('summon',{desc:`Opponent summons <b>${m.data.cr?m.data.cr.nm:'a creature'}</b>`+
      `${m.data.cr?` (⚔${m.data.cr.a}/♥${m.data.cr.h})`:''}. Spring a trap?`});
    MPNET.send({t:'resp',id:m.id,spring:!!ref}); return; }
  if(m.what==='block'){
    const atkL=(m.data.atk||[]).map(s=>({k:MPMAP.k(s.k),i:s.i}));
    const aKey=atkL[0].k, aIdx=rowIdx(aKey);
    const tIdx=m.data.kind==='back'?ROWS.length                    // the castle wall sits one row beyond YOUR back row
      :m.data.kind==='workers'?rowIdx(MPMAP.k(m.data.rk)):rowIdx(MPMAP.k(m.data.tk));
    const attacker=rowArr(aKey)[atkL[0].i]||{nm:'Enemy',a:0,h:0};
    const tgtU=(m.data.kind!=='back'&&m.data.kind!=='workers')?(rowArr(MPMAP.k(m.data.tk))||[])[m.data.ti]:null;
    const elig=eligibleInterceptors('foe',aIdx,tIdx)               // same geometry the host validates against
      .filter(r=>r.c!==tgtU&&!(m.data.kind==='workers'&&m.data.which&&minPool('you',m.data.which).includes(r.c)));
    if(!elig.length){MPNET.send({t:'resp',id:m.id,refs:[]});return;}
    const blk=await askBlock({attacker,elig,title:'Incoming Attack',ms:20000,   // deadline < host's 25s askGuest auto-pass so a real block is never silently dropped
      desc:`${attacker.nm} (⚔${attacker.a}/♥${attacker.h}) strikes from ${rowName(aKey)}.`});
    const refs=blk.map(r=>{ if(r.i!=null)return {k:MPMAP.k(r.key),i:r.i};      // cell blocker
        const which=whichForKey('you',r.key); const pi=minPool('you',which).indexOf(r.c);
        return pi>=0?{po:'foe',pw:which,pi}:null; })                            // worker blocker → canonical pool ref
      .filter(Boolean);
    MPNET.send({t:'resp',id:m.id,refs}); }
}

/* --- protocol pump --- */
MPNET.onMsg=m=>{ try{ switch(m.t){
  case 'hello': if(m.proto!==MPNET.PROTO){MP.abort('Version mismatch — both players need the same build.');return;}
    MP.status('Connected.'); break;
  case 'deck': if(MP.role!=='host')return;
    MP.peerPick={cc:m.cc,deck:m.deck||null};
    if(MP.myPick)mpStartMatch(); else MP.status('Opponent is ready — pick your deck.'); break;
  case 'start': if(MP.role!=='guest')return; mpGuestStart(m); break;
  case 'intent': if(MP.role!=='host'||!MP.started)return;
    if(m.q<=MP.lastQ)return; MP.lastQ=m.q; MPAPPLY.dispatch(m); break;
  case 'snapshot': { if(MP.role!=='guest'||!MP.started)return;
    if(m.sv<=MP.svIn)return; MP.svIn=m.sv; MP._pend=m.state;
    const wait=MP.fxUntil-Date.now(); clearTimeout(MP._st);
    if(wait>0)MP._st=setTimeout(()=>{MPSER.adopt(MP._pend);MP.drainPostFx();},wait);
    else {MPSER.adopt(MP._pend);MP.drainPostFx();} break; }
  case 'respWindow': if(MP.role==='guest')mpGuestDecide(m); break;
  case 'resp': { const a=MP._asks[m.id]; delete MP._asks[m.id]; if(a){ clearTimeout(a.timer); a.cb(m); } break; }
  case 'wait': setHint('<b style="color:var(--tide)">'+(m.what==='trap'?'Opponent may spring a trap…':'Opponent is choosing blockers…')+'</b>'); break;
  case 'fx': if(MP.role==='guest')mpReplayFx(m.ev); break;
  case 'reject': MP.clearWait(); setHint('<b style="color:#ff8a7a">Action rejected — board resynced.</b>'); break;
  case 'emote': mpToastEmote(m.e); break;
  case 'resign': if(MP.started)MP.gameOverLocal(true); break;
  case 'ping': MPNET.send({t:'pong',ts:m.ts}); break;
  case 'pong': MP.rtt=Date.now()-m.ts; MP._lastPong=Date.now();
    { const c=$('mpLink'); if(c){c.style.display='block';c.textContent='⛓ '+MP.rtt+'ms';c.style.color=MP.rtt>400?'#e35b4f':'';} } break;
  case 'bye': MPNET.close(); mpShowDrop('Opponent left.'); break;
} }catch(e){ console.warn('MP msg',e); } };

MPNET.onOpen=()=>{
  clearTimeout(MP._linkT); try{MPSIG.stop();}catch(e){} mpAutoBusy(false);   // rendezvous done — the link is live
  MPNET.send({t:'hello',proto:MPNET.PROTO,role:MP.role});
  clearInterval(MP._ping); MP._lastPong=Date.now();
  MP._ping=setInterval(()=>{ if(!MPNET.active)return;
    MPNET.send({t:'ping',ts:Date.now()});
    if(Date.now()-MP._lastPong>13000){ MPNET.close(); mpShowDrop('Connection timed out.'); } },4000);
  if(MP.role==='host'&&MP.resume&&MP.started){                     // rehost mid-game: snapshot IS the resume
    MP.resume=false; hideAllScreens(); render();
    MPNET.send({t:'start',resume:true,first:G.turn==='you'?'host':'guest',state:MPSER.snapshot()}); return; }
  if(MP.role==='guest'&&MP.started){ MP.status('Reconnected — waiting for the host state…'); return; }
  MP.status('Connected — choose your deck.');
  MP.pickingDeck=true; showSoloDeckPick();                         // reuse of the solo deck picker in 'mp' mode
};
MPNET.onDrop=why=>{ clearInterval(MP._ping); const c=$('mpLink'); if(c)c.style.display='none';
  if(MP.started&&!G.over)mpShowDrop('Link dropped ('+why+').');
  else if(MP.role){ MP.status('Connection lost ('+why+').'); } };

/* --- deck picker in 'mp' mode: patch the pick + retitle/rewire the rendered screen --- */
const _soloPickDeckMP=window.soloPickDeck;
window.soloPickDeck=function(sel){ if(MP.pickingDeck){ mpDeckPicked(sel); return; } _soloPickDeckMP(sel); };
const _renderSoloDeckPickMP=renderSoloDeckPick;
renderSoloDeckPick=function(){ _renderSoloDeckPickMP();
  if(!MP.pickingDeck)return;
  const box=$('soloSelect').querySelector('.csbox');
  const h=box.querySelector('h1'); if(h)h.textContent='Choose Your Deck · Multiplayer';
  const back=box.querySelector('.csback');
  if(back){ back.textContent='← cancel match'; back.onclick=e=>{e&&e.preventDefault();MP.reset();showScreen('mpLobby');MP.status('Match cancelled.');}; }
};
function mpDeckPicked(sel){
  let cc,deck=null;
  if(sel.kind==='custom'){ const d=loadDecks()[sel.index]; if(!d||!deckValid(d))return;
    cc=d.cc; deck={name:d.name,cc:d.cc,cards:d.cards}; }
  else cc=sel.cc;
  MP.myPick={cc,deck}; MP.pickingDeck=false;
  MPNET.send({t:'deck',cc,deck});
  showScreen('mpLobby'); $('mpModeRow').style.display='none'; mpAutoBusy(true);
  MP.status('Deck locked — waiting for opponent…');
  if(MP.role==='host'&&MP.peerPick)mpStartMatch();
}
function mpStartMatch(){                                           // HOST only: build both decks, coin flip, ship state
  const you=MP.myPick,foe=MP.peerPick;
  if(!CCS[foe.cc]){MP.abort('Opponent sent an unknown commander.');return;}
  let foeDeck;
  if(foe.deck){ if(!isWellFormedDeck(foe.deck)||foe.deck.cc!==foe.cc||!deckValid(foe.deck)){MP.abort('Opponent deck failed validation.');return;}
    foeDeck=expandDeck(foe.deck); }                                // HOST RNG shuffles BOTH decks (only in-duel randomness)
  else foeDeck=deckOf(CCS[foe.cc].colors);
  const youDeck=you.deck?expandDeck(you.deck):deckOf(CCS[you.cc].colors);
  if(typeof CAMPAIGN!=='undefined'&&CAMPAIGN)CAMPAIGN.target=null; // an MP result must never touch srd.campaign.v2 (checkWin L4208)
  MP.started=true; MP.softReset(); MP.started=true;
  startGame(you.cc,foe.cc,youDeck,foeDeck);
  const guestFirst=Math.random()<0.5;
  if(guestFirst){ G.turn='foe'; log('<span class="e">Coin flip — the opponent goes first.</span>','e'); }
  else log('<span class="y">Coin flip — you go first.</span>','y');
  render();
  const snap=MPSER.snapshot(); MP._lastSnap=JSON.stringify(snap); MP.sv=1;
  MPNET.send({t:'start',first:guestFirst?'guest':'host',state:snap});
}
function mpGuestStart(m){                                          // GUEST: local table setup, then adopt canonical state
  if(typeof CAMPAIGN!=='undefined'&&CAMPAIGN)CAMPAIGN.target=null;
  MP.started=true; MP.svIn=0; MP.fxUntil=0; MP.postFx=[];
  const st=m.state;
  startGame(st.P.foe.cc,st.P.you.cc,[],[]);                        // guest cc = canonical foe; [] decks — adopt overwrites
  MPSER.adopt(st);
  log(`<span class="y">Connected duel — ${m.first==='guest'?'you go first.':'the host goes first.'}</span>`);
}

/* --- lobby buttons --- */
window.menuMultiplayer=function(){ MP.reset(); showScreen('mpLobby');
  $('mpModeRow').style.display='flex'; mpAutoBusy(false);
  MP.status('Enter your shared password, then Host or Join.'); };
function mpAutoBusy(b){ ['mpHostBtn','mpJoinBtn'].forEach(id=>{const e=$(id); if(e)e.disabled=!!b;}); }
function mpWatchLink(){ clearTimeout(MP._linkT);                    // codes traded — now the P2P link itself must open
  MP._linkT=setTimeout(()=>{ if(MPNET.active)return;
    // no direct link in 12s (client-isolated wifi / carrier NAT) — move the whole duel onto the
    // encrypted broker relay. Slower path, but it connects from anywhere. Both sides switch on
    // their own identical timeout, keyed by the same password.
    MP.status('No direct link — switching to relayed play…');
    MPNET.relayConnect(MP._pass,MP.role);
    MP._linkT=setTimeout(()=>{ if(!MPNET.active){
      MP.status('Could not connect — even the relay failed. Check that both sides are online and retry.');
      mpAutoBusy(false); } },20000);
  },12000); }
/* password-only HOST: seal an offer, post it on the password channel, link up on the first valid answer */
window.mpAutoHost=async function(){
  const pass=($('mpPass').value||'').trim();
  if(!pass){ MP.status('Pick a password first — any word you both agree on.'); return; }
  MP.role='host'; MP._pass=pass; mpAutoBusy(true); MPSIG.stop();
  try{
    MP.status('Opening your table…');
    const code=await MPNET.hostOffer(pass);
    const tH=await MPSIG.topic(pass,'h'), tG=await MPSIG.topic(pass,'g');
    await MPSIG.publish(tH,code);
    MP.status('Table open — waiting for your friend to tap Join… (keep this screen up)');
    let linked=false;
    MPSIG.listen(tG,async ans=>{ if(linked)return;
      try{ await MPNET.hostAccept(ans,pass); linked=true; MPSIG.stop(); MP.status('Friend found — linking…'); mpWatchLink(); }
      catch(_){ /* stale or wrong-password answer — keep waiting */ }
    });
    MPSIG.deadline(()=>{ if(!MPNET.active&&!linked){ MPSIG.stop(); MP.status('No one joined — tap Host to open the table again.'); mpAutoBusy(false); } },300000);
  }catch(e){ MPSIG.stop(); mpAutoBusy(false);
    MP.status('Could not reach the relay ('+e.message+') — use Manual connect below.'); }
};
/* password-only JOIN: fetch the newest offer on the password channel, answer it automatically */
window.mpAutoJoin=async function(){
  const pass=($('mpPass').value||'').trim();
  if(!pass){ MP.status('Enter the shared password first.'); return; }
  MP.role='guest'; MP._pass=pass; mpAutoBusy(true); MPSIG.stop();
  const answerTo=async off=>{
    try{
      const ans=await MPNET.joinWithOffer(off,pass);
      MP.status('Table found — answering…');
      const tG=await MPSIG.topic(pass,'g');
      await MPSIG.publish(tG,ans);
      MP.status('Answer sent — linking…'); mpWatchLink();
      return true;
    }catch(e){
      if(e&&e.message==='Wrong password'){ MP.status('Found a table, but the password does not match — check it with your friend.'); mpAutoBusy(false); MPSIG.stop(); return true; }
      MP.status('Join failed: '+(e&&e.message||e)); mpAutoBusy(false); MPSIG.stop(); return true;
    }
  };
  try{
    MP.status('Looking for your friend’s table…');
    const tH=await MPSIG.topic(pass,'h');
    const off=await MPSIG.recent(tH,'15m');
    if(off){ await answerTo(off); return; }
    MP.status('No table yet — waiting for your friend to tap Host… (keep this screen up)');
    let took=false;
    MPSIG.listen(tH,async o=>{ if(took)return; took=true; MPSIG.stop(); await answerTo(o); });
    MPSIG.deadline(()=>{ if(!MPNET.active&&!took){ MPSIG.stop(); MP.status('No table appeared — ask your friend to tap Host, then Join again.'); mpAutoBusy(false); } },300000);
  }catch(e){ MPSIG.stop(); mpAutoBusy(false);
    MP.status('Could not reach the relay ('+e.message+') — use Manual connect below.'); }
};
window.mpHostCreate=async function(){ const pass=$('mpHostPass').value;
  if(!pass){MP.status('Pick a password first.');return;}
  MP.role='host'; MPSIG.stop(); mpAutoBusy(false);
  try{ MP.status('Gathering network candidates…'); $('mpMakeBtn').disabled=true;
    const code=await MPNET.hostOffer(pass);
    $('mpHostCode').value=code; MP.status('Code ready ('+code.length+' chars) — send it, then paste their answer.');
  }catch(e){ MP.status('Failed: '+e.message); } finally{ $('mpMakeBtn').disabled=false; } };
window.mpHostConnect=async function(){ try{ MP.status('Connecting…');
    await MPNET.hostAccept($('mpAnswerIn').value,$('mpHostPass').value);
  }catch(e){ MP.status(e.message); } };
window.mpJoin=async function(){ try{ MP.role='guest'; MPSIG.stop(); mpAutoBusy(false); MP.status('Building answer…');
    const code=await MPNET.joinWithOffer($('mpOfferIn').value,$('mpJoinPass').value);
    $('mpAnswerOut').value=code; MP.status('Answer ready ('+code.length+' chars) — send it back to the host and wait.');
  }catch(e){ MP.status(e.message); } };
window.mpCopy=function(id){ const el=$(id); el.select(); el.setSelectionRange(0,999999);
  try{ navigator.clipboard.writeText(el.value); }catch(e){ try{document.execCommand('copy');}catch(_){}} };
window.mpCancel=function(){ MP.reset(); showMainMenu(); };
function mpShowDrop(why){ const d=$('mpDrop'); $('mpDropWhy').textContent=why+' You can mint fresh codes (same password) and resume — the host state carries the whole game.';
  d.style.display='flex';
  $('mpDropRetry').onclick=()=>{ d.style.display='none'; MP.resume=(MP.role==='host'); const wasStarted=MP.started; MP.softReset(); MP.started=wasStarted; showScreen('mpLobby');
    $('mpModeRow').style.display='flex'; mpAutoBusy(false);
    MP.status(MP.role==='host'?'Same password — tap Host again; the duel resumes when your friend re-joins.':'Same password — wait for the host to re-host, then tap Join.'); };
  $('mpDropQuit').onclick=()=>{ d.style.display='none'; MP.reset(); if(!G.over&&MP.started)MP.gameOverLocal(false); showMainMenu(); };
}
/* --- emotes (optional, tiny) --- */
window.mpEmote=function(e){ if(MPNET.active&&MP.started)MPNET.send({t:'emote',e}); };
function mpToastEmote(e){ try{ const el=document.createElement('div');
  el.textContent=e; el.style.cssText='position:fixed;top:44px;left:50%;transform:translateX(-50%);font-size:34px;z-index:65;animation:none;pointer-events:none;transition:opacity .4s;';
  document.body.appendChild(el); setTimeout(()=>{el.style.opacity='0';},1200); setTimeout(()=>el.remove(),1700); }catch(_){} }
(()=>{ const c=document.createElement('div'); c.id='mpLink'; document.body.appendChild(c); })();
/* ============================ end multiplayer layer ============================ */

