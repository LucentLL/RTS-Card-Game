/* ===== main menu / deck builder / solo screens ===== */
function hideAllScreens(){ ['mainMenu','charsel','soloSelect','deckBuilder','campaign','mpLobby'].forEach(id=>{const e=$(id); if(e)e.style.display='none';}); }
function showScreen(id){ hideAllScreens(); const e=$(id); if(!e)return; e.style.display='flex'; e.classList.remove('screen-in'); void e.offsetWidth; e.classList.add('screen-in'); }
function showMainMenu(){ showScreen('mainMenu'); }
function menuPlaySolo(){ showSoloDeckPick(); }
function menuDeckBuilder(){ openDeckBuilder(); }

/* ============================ CAMPAIGN MODE ============================
   A living world map of contiguous hex TERRITORIES (Dawn-of-War style). The map
   is randomly generated each campaign: a blobby hex continent is carved into ~22
   territories, grouped into 8 contiguous element empires (one capital each). Pick
   a faction; your color spreads as you conquer bordering territories. Taking an
   element's CAPITAL absorbs its lands and unlocks its dual (2-element) deck — the
   game's existing dual commanders — to field in later battles. Between your turns
   the rival elements expand and clash (End Turn). Battles reuse startGame();
   progress persists in localStorage. Territory ids are numeric (0-based) — never
   test them for truthiness (guard with != null). */
const CAMP_KEY='srd.campaign.v2';
let CAMPAIGN=(function(){ try{ const raw=localStorage.getItem(CAMP_KEY); if(raw){ const c=JSON.parse(raw);
  if(c&&c.faction&&CCS[c.faction]&&c.map&&c.map.terr&&c.map.ids&&c.map.capitals){ c.allies=c.allies||{}; c.target=null; c.battleAs=null; if(!c.turn)c.turn=1; return c; } } }catch(e){} return null; })();
function campSave(){ try{ localStorage.setItem(CAMP_KEY,JSON.stringify(CAMPAIGN)); }catch(e){} }
function campEl(){ return document.getElementById('campaign'); }
const CAMP_DIRS=[[1,0],[0,1],[-1,1],[-1,0],[0,-1],[1,-1]];   // axial neighbours, ordered to match flat-top hex edges 0..5
function hexCorners(q,r,S){ const cx=S*1.5*q, cy=S*Math.sqrt(3)*(r+q/2); const pts=[]; for(let k=0;k<6;k++){ const a=Math.PI/3*k; pts.push([cx+S*Math.cos(a), cy+S*Math.sin(a)]); } return {cx,cy,pts}; }
function dualId(a,b){ return COLORS.indexOf(a)<COLORS.indexOf(b)? a+'_'+b : b+'_'+a; }
function terrById(id){ return CAMPAIGN.map && CAMPAIGN.map.terr[id]; }
function campPlayerTerr(){ return CAMPAIGN.map.ids.filter(id=>CAMPAIGN.map.terr[id].owner===CAMPAIGN.faction); }
function campIsCapital(tid){ const caps=CAMPAIGN.map.capitals; for(const el in caps){ if(caps[el]===tid) return el; } return null; }
function campAttackableTerr(id){ const t=terrById(id); if(!t||t.owner===CAMPAIGN.faction)return false; return t.adj.some(u=>terrById(u).owner===CAMPAIGN.faction); }

function campGenMap(faction){
  const S=32, N=5;
  function hdist(q,r){ return (Math.abs(q)+Math.abs(r)+Math.abs(q+r))/2; }
  let land=new Set();
  for(let attempt=0; attempt<40; attempt++){
    const raw=new Set();
    for(let q=-N;q<=N;q++) for(let r=-N;r<=N;r++){ if(hdist(q,r)>N) continue; const d=hdist(q,r);
      let keep=true; if(d>=N) keep=Math.random()>0.6; else if(d>=N-1) keep=Math.random()>0.22; if(keep) raw.add(q+','+r); }
    // largest connected component
    const seen=new Set(); let best=[];
    for(const k of raw){ if(seen.has(k))continue; const comp=[],stk=[k]; seen.add(k);
      while(stk.length){ const c=stk.pop(); comp.push(c); const [q,r]=c.split(',').map(Number);
        for(const [dq,dr] of CAMP_DIRS){ const nk=(q+dq)+','+(r+dr); if(raw.has(nk)&&!seen.has(nk)){ seen.add(nk); stk.push(nk); } } }
      if(comp.length>best.length) best=comp; }
    land=new Set(best); if(land.size>=42) break;
  }
  const landArr=[...land]; const axial=k=>{ const p=k.split(',').map(Number); return [p[0],p[1]]; };
  const hkd=(a,b)=>{ const A=axial(a),B=axial(b); return (Math.abs(A[0]-B[0])+Math.abs(A[1]-B[1])+Math.abs(A[0]+A[1]-B[0]-B[1]))/2; };
  const K=Math.min(landArr.length, 22);
  const sh=landArr.slice(); for(let i=sh.length-1;i>0;i--){ const j=Math.floor(Math.random()*(i+1)); const t=sh[i];sh[i]=sh[j];sh[j]=t; }
  const seeds=sh.slice(0,K);
  // claim hexes by multi-source BFS FLOOD from the seeds over the land adjacency graph — a nearest-seed
  // distance-Voronoi (argmin hkd) does NOT guarantee a territory is a single connected blob on a concave
  // continent (fragments strand, floating labels, attack-a-sliver); a flood makes every territory contiguous.
  const hexTerr={}; { const fringe=[]; seeds.forEach((s,i)=>{ hexTerr[s]=i; fringe.push(s); });
    let fi=0; while(fi<fringe.length){ const k=fringe[fi++]; const a=axial(k); const ti=hexTerr[k];
      for(const [dq,dr] of CAMP_DIRS){ const nk=(a[0]+dq)+','+(a[1]+dr); if(land.has(nk)&&hexTerr[nk]==null){ hexTerr[nk]=ti; fringe.push(nk); } } } }
  const terr={}, ids=[]; for(let i=0;i<K;i++){ terr[i]={id:i,hexes:[],adj:[],owner:null,garrison:0,cx:0,cy:0}; ids.push(i); }
  for(const k of landArr){ terr[hexTerr[k]].hexes.push(k); }
  const adj={}; ids.forEach(i=>adj[i]=new Set());
  for(const k of landArr){ const [q,r]=axial(k); const ti=hexTerr[k];
    for(const [dq,dr] of CAMP_DIRS){ const nk=(q+dq)+','+(r+dr); if(land.has(nk)){ const tj=hexTerr[nk]; if(tj!==ti){ adj[ti].add(tj); adj[tj].add(ti); } } } }
  ids.forEach(i=>{ terr[i].adj=[...adj[i]]; const hs=terr[i].hexes; let sx=0,sy=0;
    hs.forEach(k=>{ const [q,r]=axial(k); sx+=S*1.5*q; sy+=S*Math.sqrt(3)*(r+q/2); }); const gx=sx/hs.length, gy=sy/hs.length;
    // snap the label anchor to the OWN hex nearest the centroid, so it never floats over ocean / a rival hex (concave shapes)
    let bk=hs[0], bd=1e18; hs.forEach(k=>{ const a=axial(k); const hx=S*1.5*a[0], hy=S*Math.sqrt(3)*(a[1]+a[0]/2); const dd=(hx-gx)*(hx-gx)+(hy-gy)*(hy-gy); if(dd<bd){bd=dd;bk=k;} });
    const ab=axial(bk); terr[i].cx=S*1.5*ab[0]; terr[i].cy=S*Math.sqrt(3)*(ab[1]+ab[0]/2); });
  // 8 element seeds by farthest-point sampling on centroids
  const cd=(a,b)=>Math.hypot(terr[a].cx-terr[b].cx, terr[a].cy-terr[b].cy);
  const eseeds=[ids[Math.floor(Math.random()*ids.length)]];
  while(eseeds.length<8 && eseeds.length<ids.length){ let best=null,bd=-1; for(const t of ids){ if(eseeds.indexOf(t)>=0)continue; let d=1e9; eseeds.forEach(s=>{ d=Math.min(d,cd(t,s)); }); if(d>bd){bd=d;best=t;} } eseeds.push(best); }
  const others=COLORS.filter(e=>e!==faction); for(let i=others.length-1;i>0;i--){ const j=Math.floor(Math.random()*(i+1)); const t=others[i];others[i]=others[j];others[j]=t; }
  const elemsForSeeds=[faction].concat(others).slice(0,eseeds.length);
  const capitals={}, owner={}, q2=[];
  eseeds.forEach((tid,i)=>{ const el=elemsForSeeds[i]; owner[tid]=el; capitals[el]=tid; q2.push(tid); });
  let qi=0; while(qi<q2.length){ const t=q2[qi++]; for(const u of terr[t].adj){ if(owner[u]==null){ owner[u]=owner[t]; q2.push(u); } } }
  ids.forEach(t=>{ if(owner[t]==null){ let best=eseeds[0],bd=1e9; eseeds.forEach(s=>{ const d=cd(t,s); if(d<bd){bd=d;best=s;} }); owner[t]=owner[best]; } });
  ids.forEach(t=>{ terr[t].owner=owner[t]; const isCap=Object.keys(capitals).some(el=>capitals[el]===t); terr[t].garrison = 5+Math.floor(Math.random()*7)+(isCap?7:0); });
  let minx=1e9,miny=1e9,maxx=-1e9,maxy=-1e9;
  for(const k of landArr){ const {pts}=hexCorners(axial(k)[0],axial(k)[1],S); pts.forEach(p=>{ minx=Math.min(minx,p[0]);miny=Math.min(miny,p[1]);maxx=Math.max(maxx,p[0]);maxy=Math.max(maxy,p[1]); }); }
  const pad=S*0.9; const vb={x:minx-pad,y:miny-pad,w:(maxx-minx)+pad*2,h:(maxy-miny)+pad*2};
  return { S, hex:hexTerr, terr, ids, capitals, vb };
}

function menuCampaign(){ if(CAMPAIGN&&CAMPAIGN.faction&&CAMPAIGN.map) showCampaignMap(); else showFactionSelect(); }
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
function campStart(e){ if(!CCS[e]||COLORS.indexOf(e)<0)return; CAMPAIGN={faction:e, turn:1, target:null, battleAs:null, allies:{}, map:campGenMap(e)}; campSave(); showCampaignMap(); }
function showCampaignMap(){ if(!CAMPAIGN||!CAMPAIGN.map){ showFactionSelect(); return; } CAMPAIGN.target=null; CAMPAIGN.battleAs=null; campSave(); hideAllScreens(); renderCampaignMap(); campEl().style.display='flex'; }

function renderCampaignMap(){
  const el=campEl(); const M=CAMPAIGN.map; const fac=CAMPAIGN.faction; const FC=ELEMENTS[fac].color; const S=M.S;
  let fills='', borders='', labels='';
  M.ids.forEach(tid=>{ const t=M.terr[tid]; const oc=ELEMENTS[t.owner].color; const mine=t.owner===fac;
    t.hexes.forEach(k=>{ const p=k.split(',').map(Number); const {pts}=hexCorners(p[0],p[1],S);
      const d='M'+pts.map(c=>c[0].toFixed(1)+','+c[1].toFixed(1)).join('L')+'Z';
      fills+=`<path class="chex${mine?' mine':''}" data-terr="${tid}" d="${d}" style="fill:${oc}"/>`; }); });
  const land=M.hex;
  Object.keys(land).forEach(k=>{ const p=k.split(',').map(Number); const {pts}=hexCorners(p[0],p[1],S); const ti=land[k]; const aO=M.terr[ti].owner;
    for(let e=0;e<6;e++){ const dq=CAMP_DIRS[e][0], dr=CAMP_DIRS[e][1]; const nk=(p[0]+dq)+','+(p[1]+dr); const A=pts[e], B=pts[(e+1)%6];
      if(land[nk]==null){ borders+=`<line class="cbord coast" x1="${A[0].toFixed(1)}" y1="${A[1].toFixed(1)}" x2="${B[0].toFixed(1)}" y2="${B[1].toFixed(1)}"/>`; continue; }
      if(k<nk){ const tj=land[nk]; if(tj===ti) continue; const bO=M.terr[tj].owner; const emp=aO!==bO; const you=emp&&(aO===fac||bO===fac);
        const cls=emp?(you?'cbord youedge':'cbord empire'):'cbord internal';
        borders+=`<line class="${cls}" x1="${A[0].toFixed(1)}" y1="${A[1].toFixed(1)}" x2="${B[0].toFixed(1)}" y2="${B[1].toFixed(1)}"/>`; } }
  });
  M.ids.forEach(tid=>{ const t=M.terr[tid]; const capEl=campIsCapital(tid); const mine=t.owner===fac; const att=campAttackableTerr(tid);
    const R=capEl?17:13; const cls='cmark'+(att?' att':(mine?' mine':''));
    let inner=`<circle r="${R}" class="${cls}"/>`;
    if(capEl) inner+=`<text class="cgly" y="-3">${ELEMENTS[capEl].glyph}</text><text class="cgar cap" y="13">${t.garrison}</text>`;
    else inner+=`<text class="cgar" y="5">${t.garrison}</text>`;
    labels+=`<g class="cterr${att?' att':''}" transform="translate(${t.cx.toFixed(1)} ${t.cy.toFixed(1)})">${inner}</g>`; });
  const vb=M.vb;
  const svg=`<svg viewBox="${vb.x.toFixed(1)} ${vb.y.toFixed(1)} ${vb.w.toFixed(1)} ${vb.h.toFixed(1)}" class="campsvg" xmlns="http://www.w3.org/2000/svg">`+
    `<rect class="ocean" x="${vb.x.toFixed(1)}" y="${vb.y.toFixed(1)}" width="${vb.w.toFixed(1)}" height="${vb.h.toFixed(1)}"/>`+
    `<g class="cfills">${fills}</g><g class="cborders">${borders}</g><g class="clabels">${labels}</g></svg>`;
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
  const legend=`<div class="camplegend">`+COLORS.map(e=>`<span class="clg" style="color:${ELEMENTS[e].color}">${elemBadge(e,13)} ${ELEMENTS[e].name}</span>`).join('')+`</div>`;
  el.innerHTML=hud+`<div class="campwrap">${svg}</div>`+legend+
    `<div id="campConfirm" class="campoverlay" onclick="if(event.target===this)campCloseConfirm()"></div>`+
    `<div id="campTurnLog" class="campoverlay" onclick="if(event.target===this)campTurnLogClose()"></div>`+
    `<div id="campToast"></div>`;
  const svgEl=el.querySelector('.campsvg');
  if(svgEl) svgEl.addEventListener('click',ev=>{ const g=ev.target.closest&&ev.target.closest('[data-terr]'); if(g) campTerrClick(+g.getAttribute('data-terr')); });
}
function campTerrClick(tid){ const t=terrById(tid); if(!t)return; const fac=CAMPAIGN.faction;
  if(t.owner===fac){ campToast(`Your territory — garrison <b>${t.garrison}</b>.${campIsCapital(tid)===fac?' <span style="color:var(--gold)">Your capital.</span>':''}`); return; }
  if(!campAttackableTerr(tid)){ campToast(`${cap(ELEMENTS[t.owner].name)} land — not on your front. Advance to a bordering territory first.`); return; }
  campOpenAttack(tid);
}
function campOpenAttack(tid){ const box=campEl().querySelector('#campConfirm'); if(!box)return; const t=terrById(tid); const fac=CAMPAIGN.faction; const defEl=t.owner; const capEl=campIsCapital(tid);
  const combos=[[fac]].concat(Object.keys(CAMPAIGN.allies||{}).filter(e=>CAMPAIGN.allies[e]).map(e=>[fac,e]));
  const opts=combos.map(cols=>{ const cid=cols.length===1?cols[0]:dualId(cols[0],cols[1]); const c=CCS[cid]; if(!c)return '';
    return `<button class="campdeck" onclick="campAttack(${tid},'${cid}')"><span class="campdeckn" style="color:${ELEMENTS[cols[0]].color}">${cols.map(e=>elemBadge(e,14)).join('')} ${c.name}</span><span class="campdeckd">♥${c.hp} · ⚒${c.wk} · ${cols.map(cap).join(' + ')}</span></button>`; }).join('');
  box.innerHTML=`<div class="campconfbox"><div class="campconftitle" style="color:${ELEMENTS[defEl].color}">${elemBadge(defEl,18)} ${cap(ELEMENTS[defEl].name)} territory${capEl===defEl?` — <span style="color:var(--gold)">CAPITAL</span>`:''}</div>`+
    `<div class="campconfsub">Garrison <b>${t.garrison}</b>${capEl===defEl?`. Take it to <b>absorb ${cap(ELEMENTS[defEl].name)}</b> — its remaining lands and its dual deck become yours.`:'.'}</div>`+
    `<div class="campconfsub">March under which banner?</div><div class="campdecks">${opts}</div>`+
    `<div class="campconfacts"><button class="campcancel" onclick="campCloseConfirm()" style="width:100%">Cancel</button></div></div>`;
  box.style.display='flex';
}
function campAttack(tid,cid){ if(!CAMPAIGN||!CCS[cid])return; const t=terrById(tid); if(!t)return; campCloseConfirm();
  CAMPAIGN.target=tid; CAMPAIGN.battleAs=cid; campSave();
  startGame(cid, t.owner, deckOf(CCS[cid].colors.slice()), undefined);
}
function campResolve(win){ if(!CAMPAIGN||CAMPAIGN.target==null)return; const tid=CAMPAIGN.target; const t=terrById(tid); const fac=CAMPAIGN.faction; let sub='';
  if(!t){ CAMPAIGN.target=null; CAMPAIGN.battleAs=null; campSave(); return; }
  const defEl=t.owner; const capEl=campIsCapital(tid);
  if(win){
    // Taking an element's THRONE unlocks/absorbs THAT element — key off capEl (fixed designation), not defEl
    // (the current holder), so a capital a rival already seized still grants its dual deck when you take it.
    const capitalConquest = capEl && capEl!==fac;
    t.owner=fac; t.garrison=Math.max(3, Math.floor(t.garrison/2)+2);
    let extra='';
    if(capitalConquest){ let absorbed=0; CAMPAIGN.map.ids.forEach(id=>{ const u=CAMPAIGN.map.terr[id]; if(u.owner===capEl){ u.owner=fac; absorbed++; } });
      CAMPAIGN.allies=CAMPAIGN.allies||{}; CAMPAIGN.allies[capEl]=true;
      const dc=CCS[dualId(fac,capEl)];
      extra=`<br>The ${cap(ELEMENTS[capEl].name)} capital falls — ${absorbed?`its ${absorbed} remaining land${absorbed===1?'':'s'} bow to you, and `:''}the <b>${dc?dc.name:cap(ELEMENTS[capEl].name)}</b> deck is yours to field.`; }
    const caps=CAMPAIGN.map.capitals; const total=Object.keys(caps).length; const held=Object.keys(caps).filter(e=>CAMPAIGN.map.terr[caps[e]].owner===fac).length;
    const done=held>=total;
    $('bannerMsg').textContent=done?'THE REALM IS UNITED':(capitalConquest?'CAPITAL TAKEN':'TERRITORY WON'); $('bannerMsg').style.color='var(--gold)';
    sub=`Your banner rises over ${cap(ELEMENTS[defEl].name)} ground.${extra}`;
    if(done) sub+='<br><b style="color:var(--gold)">Every capital is yours — the eight elements united under one throne.</b>';
  } else { t.garrison=Math.max(1,t.garrison-1);
    $('bannerMsg').textContent='ASSAULT REPELLED'; $('bannerMsg').style.color='#e35b4f';
    sub=`${cap(ELEMENTS[defEl].name)} holds the line. Regroup and strike again.`; }
  CAMPAIGN.target=null; CAMPAIGN.battleAs=null; campSave();
  const bm=$('bannerMsg'); let d=bm.nextElementSibling; if(d&&d.className==='bsub')d.remove();
  d=document.createElement('div'); d.className='bsub'; d.style.cssText='font-size:14px;color:var(--ink);margin-top:6px;'; d.innerHTML=sub; bm.after(d);
  const acts=$('bannerActs'); if(acts)acts.innerHTML='<button onclick="campReturn()">↩ World Map</button>';
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
function campDefeat(){ campSave(); hideAllScreens();
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
.campwrap{flex:1;min-height:0;width:100%;display:flex;align-items:center;justify-content:center;}
.campsvg{width:auto;height:100%;max-width:100%;max-height:100%;display:block;touch-action:manipulation;}
.ocean{fill:#0a1626;}
.chex{cursor:pointer;stroke:rgba(0,0,0,.28);stroke-width:.6;transition:filter .12s;}
.chex.mine{filter:brightness(1.14) saturate(1.12);}
.chex:hover{filter:brightness(1.2);}
.cbord{pointer-events:none;fill:none;stroke-linecap:round;}
.cbord.coast{stroke:#05101c;stroke-width:3;}
.cbord.internal{stroke:rgba(0,0,0,.22);stroke-width:1;}
.cbord.empire{stroke:rgba(240,236,255,.8);stroke-width:2.4;}
.cbord.youedge{stroke:var(--gold);stroke-width:3.2;}
.cterr{pointer-events:none;}
.cmark{fill:rgba(8,6,14,.74);stroke:rgba(255,255,255,.28);stroke-width:1.5;}
.cmark.mine{stroke:#fff;stroke-width:2;}
.cmark.att{fill:rgba(8,6,14,.85);stroke:var(--gold);stroke-width:2.6;}
.cterr.att{animation:camppulse 1.4s ease-in-out infinite;}
.cgar{font-family:serif;font-weight:700;font-size:15px;text-anchor:middle;fill:#fff;paint-order:stroke;stroke:rgba(0,0,0,.6);stroke-width:2px;}
.cgar.cap{font-size:12px;}
.cgly{font-family:serif;font-weight:700;font-size:16px;text-anchor:middle;fill:#fff;paint-order:stroke;stroke:rgba(0,0,0,.55);stroke-width:1.5px;}
@keyframes camppulse{0%,100%{opacity:.55;}50%{opacity:1;}}
.camplegend{display:flex;flex-wrap:wrap;justify-content:center;gap:4px 14px;padding:5px 8px 2px;}
.clg{font-family:'EB Garamond',serif;font-size:12px;display:inline-flex;align-items:center;gap:4px;opacity:.9;}
.campoverlay{position:fixed;inset:0;z-index:41;display:none;align-items:center;justify-content:center;background:rgba(4,3,8,.7);backdrop-filter:blur(2px);padding:18px;}
.campconfbox{background:linear-gradient(180deg,#1c1630,#120c1e);border:1px solid rgba(180,160,220,.4);border-radius:14px;padding:18px 20px;max-width:460px;width:100%;box-shadow:0 18px 50px rgba(0,0,0,.6);}
.campconftitle{font-family:'Cinzel',serif;font-size:18px;letter-spacing:.03em;display:flex;align-items:center;gap:6px;margin-bottom:8px;flex-wrap:wrap;}
.campconfsub{font-family:'EB Garamond',serif;color:var(--ink);font-size:14px;margin-bottom:8px;line-height:1.4;}
.campconfacts{display:flex;gap:10px;margin-top:12px;}
.campgo{flex:1;font-family:'Cinzel',serif;font-size:14px;letter-spacing:.05em;color:#fff;background:linear-gradient(180deg,#8a6b1e,#6a4f12);border:1px solid var(--gold);border-radius:9px;padding:11px;cursor:pointer;}
.campgo:hover{filter:brightness(1.15);}
.campcancel{font-family:'Cinzel',serif;font-size:13px;color:var(--ink-dim);background:transparent;border:1px solid rgba(180,160,220,.3);border-radius:9px;padding:11px 16px;cursor:pointer;}
.campdecks{display:flex;flex-direction:column;gap:7px;margin-top:4px;}
.campdeck{display:flex;flex-direction:column;align-items:flex-start;gap:2px;text-align:left;background:rgba(40,32,58,.7);border:1px solid rgba(180,160,220,.32);border-radius:9px;padding:8px 12px;cursor:pointer;}
.campdeck:hover{border-color:var(--gold);background:rgba(52,42,74,.8);}
.campdeckn{font-family:'Cinzel',serif;font-size:14px;display:inline-flex;align-items:center;gap:5px;}
.campdeckd{font-family:'EB Garamond',serif;font-size:12px;color:var(--ink-dim);}
.tlscroll{max-height:56vh;overflow-y:auto;-webkit-overflow-scrolling:touch;}
.tlrow{font-family:'EB Garamond',serif;font-size:14px;color:var(--ink);padding:5px 2px;border-bottom:1px solid rgba(180,160,220,.12);}
#campToast{position:fixed;left:50%;bottom:54px;transform:translateX(-50%);z-index:42;display:none;font-family:'EB Garamond',serif;font-size:14px;color:var(--ink);background:rgba(12,9,20,.95);border:1px solid rgba(180,160,220,.4);border-radius:10px;padding:9px 16px;max-width:80vw;text-align:center;box-shadow:0 10px 30px rgba(0,0,0,.5);}
`; document.head.appendChild(s); })();
/* ========================== end campaign mode ========================== */

