/* ===== main menu / deck builder / solo screens ===== */
function hideAllScreens(){ if(typeof campGlobeStop==='function') campGlobeStop();   // single choke point for every leave-the-map path; a display:none canvas is still isConnected, so the loop won't stop itself
  ['mainMenu','charsel','soloSelect','deckBuilder','campaign','mpLobby'].forEach(id=>{const e=$(id); if(e)e.style.display='none';}); }
function showScreen(id){ hideAllScreens(); const e=$(id); if(!e)return; e.style.display='flex'; e.classList.remove('screen-in'); void e.offsetWidth; e.classList.add('screen-in'); }
function showMainMenu(){ showScreen('mainMenu'); }
function menuPlaySolo(){ showSoloDeckPick(); }
function menuDeckBuilder(){ openDeckBuilder(); }

/* ============================ CAMPAIGN MODE ============================
   A living WORLD GLOBE of territories (Dawn-of-War style, on a planet). The
   world is a hexsphere (see 10_campaign_globe.js) carved into ~22 contiguous
   territories, grouped into 8 contiguous element empires (one capital each).
   Pick a faction; your color spreads as you conquer bordering territories.
   Taking an element's CAPITAL absorbs its lands and unlocks its dual deck.
   Between your turns the rival elements expand and clash (End Turn). A
   challenge opens a Fire Emblem-style dialogue (10_campaign_dialogue.js),
   then the battle reuses startGame(); progress persists in localStorage.
   Territory ids are numeric (0-based) — never test them for truthiness
   (guard with != null). Sphere geometry is deterministic from map.f, so
   saves carry only tile→territory assignments. */
const CAMP_KEY='srd.campaign.v3';
let CAMPAIGN=(function(){ try{ localStorage.removeItem('srd.campaign.v2'); }catch(e){}
  try{ const raw=localStorage.getItem(CAMP_KEY); if(raw){ const c=JSON.parse(raw);
  if(c&&c.faction&&CCS[c.faction]&&c.map&&c.map.tileTerr&&c.map.f&&c.map.terr&&c.map.ids&&c.map.capitals
     // tileTerr must match the sphere its own f rebuilds, else every render indexes past the tile list
     && c.map.tileTerr.length===(10*c.map.f*c.map.f+2)){ c.allies=c.allies||{}; c.target=null; c.battleAs=null; if(!c.turn)c.turn=1; return c; } } }catch(e){} return null; })();
function campSave(){ try{ localStorage.setItem(CAMP_KEY,JSON.stringify(CAMPAIGN)); }catch(e){} }
function campEl(){ return document.getElementById('campaign'); }
function dualId(a,b){ return COLORS.indexOf(a)<COLORS.indexOf(b)? a+'_'+b : b+'_'+a; }
function terrById(id){ return CAMPAIGN.map && CAMPAIGN.map.terr[id]; }
function campPlayerTerr(){ return CAMPAIGN.map.ids.filter(id=>CAMPAIGN.map.terr[id].owner===CAMPAIGN.faction); }
function campIsCapital(tid){ const caps=CAMPAIGN.map.capitals; for(const el in caps){ if(caps[el]===tid) return el; } return null; }
function campAttackableTerr(id){ const t=terrById(id); if(!t||t.owner===CAMPAIGN.faction)return false; return t.adj.some(u=>terrById(u).owner===CAMPAIGN.faction); }

function campGenMap(faction){
  /* Carve the whole hexsphere into K contiguous territories (multi-source BFS
     flood — same guarantee as the old flat map: every territory is one blob),
     then 8 contiguous element empires via farthest-point capital seeds.
     Validated by an 800-map Monte-Carlo (0 fragmented territories/empires). */
  const sphere=getSphere(CAMP_FREQ); const T=sphere.tiles, n=T.length;
  const K=Math.min(22,n);
  // territory seeds: Mitchell best-candidate (farthest of 8 random picks) —
  // organic like pure random, but no clustered seeds → no giant-blob-next-to-sliver
  const seeds=[Math.floor(Math.random()*n)];
  const chord=(a,b)=>{ const A=T[a].c,B=T[b].c; return Math.hypot(A[0]-B[0],A[1]-B[1],A[2]-B[2]); };
  while(seeds.length<K){ let best=-1,bd=-1;
    for(let c=0;c<8;c++){ const cand=Math.floor(Math.random()*n); if(seeds.indexOf(cand)>=0)continue;
      let d=1e9; seeds.forEach(s=>{ d=Math.min(d,chord(cand,s)); }); if(d>bd){bd=d;best=cand;} }
    if(best<0)continue; seeds.push(best); }
  const tileTerr=new Array(n).fill(-1);
  { const fringe=[]; seeds.forEach((s,i)=>{ tileTerr[s]=i; fringe.push(s); });
    let fi=0; while(fi<fringe.length){ const t=fringe[fi++]; const ti=tileTerr[t];
      for(const u of T[t].adj){ if(tileTerr[u]<0){ tileTerr[u]=ti; fringe.push(u); } } } }
  const terr={}, ids=[];
  for(let i=0;i<K;i++){ terr[i]={id:i,tiles:[],adj:[],owner:null,garrison:0,anchor:-1}; ids.push(i); }
  for(let t=0;t<n;t++) terr[tileTerr[t]].tiles.push(t);
  const adj=ids.map(()=>new Set());
  for(let t=0;t<n;t++) for(const u of T[t].adj){ const a=tileTerr[t],b=tileTerr[u]; if(a!==b){ adj[a].add(b); adj[b].add(a); } }
  ids.forEach(i=>{ terr[i].adj=[...adj[i]];
    // anchor = own tile nearest the territory's centroid direction (marker spot)
    let sx=0,sy=0,sz=0; terr[i].tiles.forEach(t=>{ const c=T[t].c; sx+=c[0];sy+=c[1];sz+=c[2]; });
    const l=Math.hypot(sx,sy,sz)||1; const cn=[sx/l,sy/l,sz/l];
    let bt=terr[i].tiles[0], bd=-2;
    terr[i].tiles.forEach(t=>{ const c=T[t].c; const d=c[0]*cn[0]+c[1]*cn[1]+c[2]*cn[2]; if(d>bd){bd=d;bt=t;} });
    terr[i].anchor=bt; });
  // 8 element seeds by farthest-point sampling on anchor positions
  const pos=i=>T[terr[i].anchor].c;
  const cd=(a,b)=>{ const A=pos(a),B=pos(b); return Math.hypot(A[0]-B[0],A[1]-B[1],A[2]-B[2]); };
  const eseeds=[ids[Math.floor(Math.random()*ids.length)]];
  while(eseeds.length<8 && eseeds.length<ids.length){ let best=null,bd=-1; for(const t of ids){ if(eseeds.indexOf(t)>=0)continue; let d=1e9; eseeds.forEach(s=>{ d=Math.min(d,cd(t,s)); }); if(d>bd){bd=d;best=t;} } eseeds.push(best); }
  const others=COLORS.filter(e=>e!==faction); for(let i=others.length-1;i>0;i--){ const j=Math.floor(Math.random()*(i+1)); const t=others[i];others[i]=others[j];others[j]=t; }
  const elemsForSeeds=[faction].concat(others).slice(0,eseeds.length);
  const capitals={}, owner={}, q2=[];
  eseeds.forEach((tid,i)=>{ const el=elemsForSeeds[i]; owner[tid]=el; capitals[el]=tid; q2.push(tid); });
  let qi=0; while(qi<q2.length){ const t=q2[qi++]; for(const u of terr[t].adj){ if(owner[u]==null){ owner[u]=owner[t]; q2.push(u); } } }
  ids.forEach(t=>{ if(owner[t]==null){ let best=eseeds[0],bd=1e9; eseeds.forEach(s=>{ const d=cd(t,s); if(d<bd){bd=d;best=s;} }); owner[t]=owner[best]; } });
  ids.forEach(t=>{ terr[t].owner=owner[t]; const isCap=Object.keys(capitals).some(el=>capitals[el]===t); terr[t].garrison = 5+Math.floor(Math.random()*7)+(isCap?7:0); });
  return { f:CAMP_FREQ, tileTerr, terr, ids, capitals };
}

function menuCampaign(){ if(CAMPAIGN&&CAMPAIGN.faction&&CAMPAIGN.map&&!CAMPAIGN.lost) showCampaignMap(); else showFactionSelect(); }
function showFactionSelect(){ hideAllScreens(); renderFactionSelect(); campEl().style.display='flex'; }
function renderFactionSelect(){
  const cards=COLORS.map(e=>{ const E=ELEMENTS[e];
    return `<button class="cschar" onclick="campStart('${e}')"><div class="cn" style="color:${E.color}">${elemBadge(e,16)} ${E.name}</div>`+
      `<div class="cp">${E.lore}</div><div class="cpow">♥ <b>${E.hp}</b> · ⚒ <b>${E.wk}</b> workers · ${E.name} banner</div></button>`;
  }).join('');
  campEl().innerHTML=`<div class="campscroll"><div class="csbox"><h1>Choose Your Banner</h1>`+
    `<div class="cssub">Campaign — hold one home realm on a freshly-drawn world, then conquer it territory by territory. Take an element's capital to absorb its lands and unlock its dual deck.</div>`+
    `<div class="csrow">${cards}</div><button class="csback" onclick="showMainMenu()">← menu</button></div></div>`;
}
function campStart(e){ if(!CCS[e]||COLORS.indexOf(e)<0)return; CAMPAIGN={faction:e, turn:1, target:null, battleAs:null, allies:{}, map:campGenMap(e)}; campSave(); campGlobeResetView(); showCampaignMap(); }
/* display BEFORE render: campGlobeMount measures its parent to size the canvas,
   and a still-hidden #campaign measures 0×0 (the globe would mount at the 80px
   floor and only pop to full size when the renderer's self-heal fires). */
function showCampaignMap(){ if(!CAMPAIGN||!CAMPAIGN.map){ showFactionSelect(); return; } CAMPAIGN.target=null; CAMPAIGN.battleAs=null; campSave(); hideAllScreens(); campEl().style.display='flex'; renderCampaignMap(); }

function renderCampaignMap(){
  const el=campEl(); const M=CAMPAIGN.map; const fac=CAMPAIGN.faction; const FC=ELEMENTS[fac].color;
  const held=campPlayerTerr().length, total=M.ids.length;
  const capsAll=Object.keys(M.capitals); const heldCaps=capsAll.filter(e=>M.terr[M.capitals[e]].owner===fac).length;
  const allyList=Object.keys(CAMPAIGN.allies||{}).filter(e=>CAMPAIGN.allies[e]);
  const allyBadges=allyList.length?allyList.map(e=>elemBadge(e,16)).join(''):'<span style="color:var(--ink-dim);font-style:italic">none yet</span>';
  const hud=`<div class="camphud"><div class="camphudl">`+
    `<span class="campfac" style="color:${FC}">${elemBadge(fac,18)} ${ELEMENTS[fac].name}</span>`+
    `<span class="campstat">Turn <b>${CAMPAIGN.turn}</b></span>`+
    `<span class="campstat">Lands <b>${held}/${total}</b></span>`+
    `<span class="campstat">Capitals <b>${heldCaps}/${capsAll.length}</b></span>`+
    `<span class="campstat">Allies ${allyBadges}</span></div>`+
    `<div class="camphudr"><button class="campbtn go" onclick="campEndTurn()">End Turn ▶</button>`+
    `<button class="campbtn ghost" onclick="campReset()">New</button><button class="campbtn ghost" onclick="showMainMenu()">Menu</button></div></div>`;
  const legend=`<div class="camplegend"><span class="campnote">drag the globe · tap a territory</span>`+COLORS.map(e=>`<span class="clg" style="color:${ELEMENTS[e].color}">${elemBadge(e,13)} ${ELEMENTS[e].name}</span>`).join('')+`</div>`;
  el.innerHTML=hud+`<div class="campwrap"><canvas class="campglobe"></canvas></div>`+legend+
    `<div id="campConfirm" class="campoverlay" onclick="if(event.target===this)campCloseConfirm()"></div>`+
    `<div id="campTurnLog" class="campoverlay" onclick="if(event.target===this)campTurnLogClose()"></div>`+
    `<div id="campToast"></div>`;
  campGlobeMount(el.querySelector('.campglobe'), M, fac, campTerrClick);
}
function campTerrClick(tid){ const t=terrById(tid); if(!t)return; const fac=CAMPAIGN.faction;
  if(t.owner===fac){ campToast(`Your territory — garrison <b>${t.garrison}</b>.${campIsCapital(tid)===fac?' <span style="color:var(--gold)">Your capital.</span>':''}`); return; }
  if(!campAttackableTerr(tid)){ campToast(`${cap(ELEMENTS[t.owner].name)} land — not on your front. Advance to a bordering territory first.`); return; }
  campOpenAttack(tid);
}
/* Taking this tile absorbs an element? Keyed off the FIXED designation, not the
   current holder, so a throne a rival already seized still pays out. One helper
   for the confirm box, the dialogue and the resolution so the three can't drift. */
function campCapitalPrize(tid){ const c=campIsCapital(tid); return (c && c!==CAMPAIGN.faction) ? c : null; }
function campOpenAttack(tid){ const box=campEl().querySelector('#campConfirm'); if(!box)return; const t=terrById(tid); const fac=CAMPAIGN.faction; const defEl=t.owner; const capEl=campIsCapital(tid); const prize=campCapitalPrize(tid);
  const combos=[[fac]].concat(Object.keys(CAMPAIGN.allies||{}).filter(e=>CAMPAIGN.allies[e]).map(e=>[fac,e]));
  const opts=combos.map(cols=>{ const cid=cols.length===1?cols[0]:dualId(cols[0],cols[1]); const c=CCS[cid]; if(!c)return '';
    return `<button class="campdeck" onclick="campAttack(${tid},'${cid}')"><span class="campdeckn" style="color:${ELEMENTS[cols[0]].color}">${cols.map(e=>elemBadge(e,14)).join('')} ${c.name}</span><span class="campdeckd">♥${c.hp} · ⚒${c.wk} · ${cols.map(cap).join(' + ')}</span></button>`; }).join('');
  const capTag = prize ? ` — <span style="color:var(--gold)">${cap(ELEMENTS[prize].name).toUpperCase()} CAPITAL</span>`
    : (capEl===fac ? ` — <span style="color:var(--gold)">YOUR CAPITAL</span>` : '');
  const capNote = prize ? `. Take it to <b>absorb ${cap(ELEMENTS[prize].name)}</b> — its remaining lands and its dual deck become yours.`
    : (capEl===fac ? '. <b>Your throne</b>, held by another — retake it.' : '.');
  box.innerHTML=`<div class="campconfbox"><div class="campconftitle" style="color:${ELEMENTS[defEl].color}">${elemBadge(defEl,18)} ${cap(ELEMENTS[defEl].name)} territory${capTag}</div>`+
    `<div class="campconfsub">Garrison <b>${t.garrison}</b>${capNote}</div>`+
    `<div class="campconfsub">March under which banner?</div><div class="campdecks">${opts}</div>`+
    `<div class="campconfacts"><button class="campcancel" onclick="campCloseConfirm()" style="width:100%">Cancel</button></div></div>`;
  box.style.display='flex';
}
function campAttack(tid,cid){ if(!CAMPAIGN||!CCS[cid])return; const t=terrById(tid); if(!t)return; campCloseConfirm();
  CAMPAIGN.target=tid; CAMPAIGN.battleAs=cid; campSave();
  // owner-relative: the defender's "capital" lines are written in first person
  // about their OWN throne, so a rival-seized capital must not trigger them
  campDialogue({ atkEl:CAMPAIGN.faction, defEl:t.owner, capital:campIsCapital(tid)===t.owner,
    onDone:()=>startGame(cid, t.owner, deckOf(CCS[cid].colors.slice()), undefined) });
}
function campResolve(win){ if(!CAMPAIGN||CAMPAIGN.target==null)return; const tid=CAMPAIGN.target; const t=terrById(tid); const fac=CAMPAIGN.faction; let sub='';
  if(!t){ CAMPAIGN.target=null; CAMPAIGN.battleAs=null; campSave(); return; }
  const defEl=t.owner; const capEl=campIsCapital(tid);
  if(win){
    const prize=campCapitalPrize(tid);
    t.owner=fac; t.garrison=Math.max(3, Math.floor(t.garrison/2)+2);
    let extra='';
    if(prize){ CAMPAIGN.allies=CAMPAIGN.allies||{}; let absorbed=0; const gained=[];
      const swallow=el=>{ CAMPAIGN.allies[el]=true; gained.push(el);
        CAMPAIGN.map.ids.forEach(id=>{ const u=CAMPAIGN.map.terr[id]; if(u.owner===el){ u.owner=fac; absorbed++; } }); };
      swallow(prize);
      // absorbing one element's lands can hand you ANOTHER element's throne; cascade,
      // else that element lingers as a landless holdout that no attack can ever reach
      for(let again=true; again;){ again=false;
        for(const el in CAMPAIGN.map.capitals){ if(el===fac||CAMPAIGN.allies[el])continue;
          if(CAMPAIGN.map.terr[CAMPAIGN.map.capitals[el]].owner===fac){ swallow(el); again=true; } } }
      const decks=gained.map(e=>{ const dc=CCS[dualId(fac,e)]; return `<b>${dc?dc.name:cap(ELEMENTS[e].name)}</b>`; }).join(' and ');
      extra=`<br>The ${cap(ELEMENTS[prize].name)} capital falls — ${absorbed?`its ${absorbed} remaining land${absorbed===1?'':'s'} bow to you, and `:''}the ${decks} deck${gained.length>1?'s are':' is'} yours to field.`; }
    // victory = the whole map, not just the thrones; latched so it can't re-fire on every later win
    const done = !CAMPAIGN.completed && campPlayerTerr().length===CAMPAIGN.map.ids.length;
    if(done) CAMPAIGN.completed=true;
    $('bannerMsg').textContent=done?'THE REALM IS UNITED':(prize?'CAPITAL TAKEN':'TERRITORY WON'); $('bannerMsg').style.color='var(--gold)';
    sub=`Your banner rises over ${cap(ELEMENTS[defEl].name)} ground.${extra}`;
    if(done) sub+='<br><b style="color:var(--gold)">Every land is yours — the eight elements united under one throne.</b>';
  } else { t.garrison=Math.max(1,t.garrison-1);
    $('bannerMsg').textContent='ASSAULT REPELLED'; $('bannerMsg').style.color='#e35b4f';
    sub=`${cap(ELEMENTS[defEl].name)} holds the line. Regroup and strike again.`; }
  CAMPAIGN.target=null; CAMPAIGN.battleAs=null; campSave();
  const bm=$('bannerMsg'); let d=bm.nextElementSibling; if(d&&d.className==='bsub')d.remove();
  d=document.createElement('div'); d.className='bsub'; d.style.cssText='font-size:14px;color:var(--ink);margin-top:6px;'; d.innerHTML=sub; bm.after(d);
  const acts=$('bannerActs'); if(acts)acts.innerHTML=CAMPAIGN.completed
    ? '<button onclick="campDoReset()">New Campaign</button><button onclick="campReturn()">↩ World Map</button>'
    : '<button onclick="campReturn()">↩ World Map</button>';
}
function campReturn(){ $('banner').style.display='none'; const a=$('bannerActs'); if(a)a.innerHTML='<button onclick="location.reload()">Duel Again</button>'; showCampaignMap(); }
function campEndTurn(){ if(!CAMPAIGN||!CAMPAIGN.map)return; const M=CAMPAIGN.map, fac=CAMPAIGN.faction; const logs=[]; CAMPAIGN.turn++;
  M.ids.forEach(tid=>{ const t=M.terr[tid]; t.garrison=Math.min(24, t.garrison+(campIsCapital(tid)?2:1)); });
  const ai=COLORS.filter(e=>e!==fac && M.ids.some(id=>M.terr[id].owner===e));
  for(let i=ai.length-1;i>0;i--){ const j=Math.floor(Math.random()*(i+1)); const t=ai[i];ai[i]=ai[j];ai[j]=t; }
  ai.forEach(el=>{ let best=null;
    M.ids.forEach(tid=>{ const t=M.terr[tid]; if(t.owner!==el)return; t.adj.forEach(u=>{ const d=M.terr[u]; if(d.owner===el)return; const sc=t.garrison-d.garrison; if(!best||sc>best.sc) best={from:tid,to:u,sc,def:d.owner}; }); });
    if(best && best.sc>-2 && Math.random()<0.7){ const a=M.terr[best.from], d=M.terr[best.to];
      const aw=a.garrison*(0.7+0.6*Math.random()), dw=d.garrison*(0.7+0.6*Math.random());
      if(aw>dw){ const wasYou=d.owner===fac; const from=d.owner; d.owner=el; const mv=Math.max(2,Math.floor(a.garrison/2)); a.garrison=Math.max(1,a.garrison-mv); d.garrison=mv;
        logs.push(`<span style="color:${ELEMENTS[el].color}">${cap(ELEMENTS[el].name)}</span> overran ${wasYou?'<b style="color:#e0a59a">your</b>':`<span style="color:${ELEMENTS[from].color}">${cap(ELEMENTS[from].name)}</span>`+"'s"} territory.`);
      } else { a.garrison=Math.max(1,Math.floor(a.garrison*0.8)); } }
  });
  if(!M.ids.some(id=>M.terr[id].owner===fac)){ campDefeat(); return; }
  campSave(); renderCampaignMap(); campTurnLog(logs);
}
function campTurnLog(logs){ const box=campEl().querySelector('#campTurnLog'); if(!box)return;
  const body=logs.length?logs.map(l=>`<div class="tlrow">${l}</div>`).join(''):'<div class="tlrow" style="color:var(--ink-dim);font-style:italic">The map lies quiet this turn.</div>';
  box.innerHTML=`<div class="campconfbox"><div class="campconftitle" style="color:var(--gold)">Turn ${CAMPAIGN.turn} — the world stirs</div><div class="tlscroll">${body}</div><div class="campconfacts"><button class="campgo" onclick="campTurnLogClose()" style="width:100%">Continue</button></div></div>`;
  box.style.display='flex'; }
function campTurnLogClose(){ const b=campEl().querySelector('#campTurnLog'); if(b)b.style.display='none'; }
function campDefeat(){ CAMPAIGN.lost=true; campSave(); hideAllScreens();   // flagged so a reload lands on faction select, not a dead map
  $('bannerMsg').textContent='YOUR BANNER HAS FALLEN'; $('bannerMsg').style.color='#e35b4f';
  const bm=$('bannerMsg'); let d=bm.nextElementSibling; if(d&&d.className==='bsub')d.remove();
  d=document.createElement('div'); d.className='bsub'; d.style.cssText='font-size:14px;color:var(--ink);margin-top:6px;'; d.innerHTML='The last of your holdings is lost. The campaign is over.'; bm.after(d);
  const acts=$('bannerActs'); if(acts)acts.innerHTML='<button onclick="campDoReset()">New Campaign</button>';
  $('banner').style.display='flex'; }
function campReset(){ const box=campEl().querySelector('#campConfirm'); if(!box)return;
  box.innerHTML=`<div class="campconfbox"><div class="campconftitle" style="color:#e0a59a">Abandon this campaign?</div>`+
    `<div class="campconfsub">Your conquered lands and alliances are lost, and a new world is drawn.</div>`+
    `<div class="campconfacts"><button class="campgo" style="background:linear-gradient(180deg,#7a2420,#5a1814);border-color:#c0463c" onclick="campDoReset()">Start over</button>`+
    `<button class="campcancel" onclick="campCloseConfirm()">Keep playing</button></div></div>`;
  box.style.display='flex'; }
function campDoReset(){ CAMPAIGN=null; try{localStorage.removeItem(CAMP_KEY);}catch(e){} const b=$('banner'); if(b)b.style.display='none'; const a=$('bannerActs'); if(a)a.innerHTML='<button onclick="location.reload()">Duel Again</button>'; campCloseConfirm(); showFactionSelect(); }
function campCloseConfirm(){ const b=campEl().querySelector('#campConfirm'); if(b){ b.style.display='none'; b.innerHTML=''; } }
let campToastT=null;
function campToast(msg){ const t=campEl().querySelector('#campToast'); if(!t)return; t.innerHTML=msg; t.style.display='block'; clearTimeout(campToastT); campToastT=setTimeout(()=>{ t.style.display='none'; },2600); }
(function injectCampaignCSS(){ const s=document.createElement('style'); s.textContent=`
#campaign{position:fixed;inset:0;z-index:40;display:none;flex-direction:column;align-items:center;
  background:radial-gradient(1200px 820px at 50% -12%,#241a36 0%,transparent 60%),radial-gradient(900px 600px at 50% 118%,#101a2e 0%,transparent 55%),rgba(4,3,8,.985);
  backdrop-filter:blur(3px);padding:8px 10px 6px;overflow:hidden;}
.campscroll{width:100%;height:100%;overflow-y:auto;-webkit-overflow-scrolling:touch;display:flex;align-items:flex-start;justify-content:center;padding:6px;}
.camphud{width:100%;max-width:1180px;display:flex;align-items:center;justify-content:space-between;gap:10px;flex-wrap:wrap;padding:2px 4px 6px;}
.camphudl{display:flex;align-items:center;gap:13px;flex-wrap:wrap;}
.campfac{font-family:'Cinzel',serif;font-size:16px;letter-spacing:.05em;display:inline-flex;align-items:center;gap:6px;}
.campstat{font-family:'EB Garamond',serif;color:var(--ink);font-size:14px;display:inline-flex;align-items:center;gap:4px;}
.campstat b{color:var(--gold);}
.camphudr{display:flex;gap:8px;flex-wrap:wrap;}
.campbtn{font-family:'Cinzel',serif;font-size:12px;letter-spacing:.04em;color:var(--ink);background:rgba(30,24,44,.85);border:1px solid rgba(180,160,220,.35);border-radius:8px;padding:7px 11px;cursor:pointer;}
.campbtn:hover{border-color:var(--gold);color:#fff;}
.campbtn.go{background:linear-gradient(180deg,#2f6a3a,#1e4a28);border-color:#4fae5e;color:#eafff0;}
.campbtn.ghost{background:transparent;color:var(--ink-dim);}
.campwrap{flex:1;min-height:0;width:100%;display:flex;align-items:center;justify-content:center;position:relative;}
.camplegend{display:flex;flex-wrap:wrap;justify-content:center;align-items:center;gap:4px 14px;padding:5px 8px 2px;}
.campnote{font-family:'EB Garamond',serif;font-size:12px;color:var(--ink-dim);font-style:italic;margin-right:6px;}
.clg{font-family:'EB Garamond',serif;font-size:12px;display:inline-flex;align-items:center;gap:4px;opacity:.9;}
.campoverlay{position:fixed;inset:0;z-index:41;display:none;align-items:center;justify-content:center;background:rgba(4,3,8,.7);backdrop-filter:blur(2px);padding:18px;}
.campconfbox{background:linear-gradient(180deg,#1c1630,#120c1e);border:1px solid rgba(180,160,220,.4);border-radius:14px;padding:18px 20px;max-width:460px;width:100%;box-shadow:0 18px 50px rgba(0,0,0,.6);}
.campconftitle{font-family:'Cinzel',serif;font-size:18px;letter-spacing:.03em;display:flex;align-items:center;gap:6px;margin-bottom:8px;flex-wrap:wrap;}
.campconfsub{font-family:'EB Garamond',serif;color:var(--ink);font-size:14px;margin-bottom:8px;line-height:1.4;}
.campconfacts{display:flex;gap:10px;margin-top:12px;}
.campgo{flex:1;font-family:'Cinzel',serif;font-size:14px;letter-spacing:.05em;color:#fff;background:linear-gradient(180deg,#8a6b1e,#6a4f12);border:1px solid var(--gold);border-radius:9px;padding:11px;cursor:pointer;}
.campgo:hover{filter:brightness(1.15);}
.campcancel{font-family:'Cinzel',serif;font-size:13px;color:var(--ink-dim);background:transparent;border:1px solid rgba(180,160,220,.3);border-radius:9px;padding:11px 16px;cursor:pointer;}
.campdecks{display:flex;flex-direction:column;gap:7px;margin-top:4px;max-height:46vh;overflow-y:auto;-webkit-overflow-scrolling:touch;}  /* grows to 8 banners late in a campaign — scroll the list, keep Cancel pinned */
.campdeck{display:flex;flex-direction:column;align-items:flex-start;gap:2px;text-align:left;background:rgba(40,32,58,.7);border:1px solid rgba(180,160,220,.32);border-radius:9px;padding:8px 12px;cursor:pointer;}
.campdeck:hover{border-color:var(--gold);background:rgba(52,42,74,.8);}
.campdeckn{font-family:'Cinzel',serif;font-size:14px;display:inline-flex;align-items:center;gap:5px;}
.campdeckd{font-family:'EB Garamond',serif;font-size:12px;color:var(--ink-dim);}
.tlscroll{max-height:56vh;overflow-y:auto;-webkit-overflow-scrolling:touch;}
.tlrow{font-family:'EB Garamond',serif;font-size:14px;color:var(--ink);padding:5px 2px;border-bottom:1px solid rgba(180,160,220,.12);}
#campToast{position:fixed;left:50%;bottom:54px;transform:translateX(-50%);z-index:42;display:none;font-family:'EB Garamond',serif;font-size:14px;color:var(--ink);background:rgba(12,9,20,.95);border:1px solid rgba(180,160,220,.4);border-radius:10px;padding:9px 16px;max-width:80vw;text-align:center;box-shadow:0 10px 30px rgba(0,0,0,.5);}
`; document.head.appendChild(s); })();
/* ========================== end campaign mode ========================== */

