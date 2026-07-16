/* -- deck builder -- */
let dbState=null;
function openDeckBuilder(editIndex,ret){
  const decks=loadDecks();
  // creating a new deck while at the cap would open an unsaveable builder — send to the deck list instead
  if(editIndex==null&&decks.length>=MAX_DECKS){ showSoloDeckPick(); return; }
  hideAllScreens();
  const back=ret||'menu';
  if(editIndex!=null&&decks[editIndex]) dbState={cc:decks[editIndex].cc,name:decks[editIndex].name,cards:{...decks[editIndex].cards},editIndex,back,sel:null,filter:{q:'',type:'',elem:'',cost:null,kw:'',tag:''},sort:'type'};
  else dbState={cc:'fire',name:'',cards:{},editIndex:null,back,sel:null,filter:{q:'',type:'',elem:'',cost:null,kw:'',tag:''},sort:'type'};
  renderDeckBuilder(); showScreen('deckBuilder');
}
function dbReturn(){ const back=dbState&&dbState.back; dbState=null; if(back==='solo')showSoloDeckPick(); else showMainMenu(); }
function dbNameInput(v){ if(dbState){ dbState.name=v; refreshDbCounter(); } }
function renderDeckBuilder(){ renderDbLeaders(); $('dbName').value=dbState.name; renderDbFilters(); renderDbPool(); renderDbDeck(); renderDbDetail(); renderDbStats(); refreshDbCounter(); }
// --- pool search / type filter / sort ---
const DB_SORT={
  type:(a,b)=>(DB_TYPE_ORDER[a.type]-DB_TYPE_ORDER[b.type])||((a.tpl.c||0)-(b.tpl.c||0))||a.nm.localeCompare(b.nm),
  cost:(a,b)=>((a.tpl.c||0)-(b.tpl.c||0))||a.nm.localeCompare(b.nm),
  costdesc:(a,b)=>((b.tpl.c||0)-(a.tpl.c||0))||a.nm.localeCompare(b.nm),
  name:(a,b)=>a.nm.localeCompare(b.nm),
  atk:(a,b)=>((b.tpl.a||0)-(a.tpl.a||0))||a.nm.localeCompare(b.nm),
};
const DB_KW_LABEL={fs:'First Strike',detonate:'Detonate',undertow:'Undertow',entrench:'Entrench',scour:'Scour',chrysalis:'Chrysalis',overcharge:'Overcharge',ward:'Ward',reap:'Reap'};
const DB_KW_ORDER=['fs','detonate','undertow','entrench','scour','chrysalis','overcharge','ward','reap'];
function dbAvailKw(reg){ const s=new Set(); reg.forEach(e=>{ if(e.type!=='creature'||!e.tpl)return; if(e.tpl.fs)s.add('fs'); if(e.tpl.kw)s.add(e.tpl.kw); }); return DB_KW_ORDER.filter(k=>s.has(k)); }
function dbAvailTags(reg){ const s=new Set(); reg.forEach(e=>{ if(!e.tpl)return; if(e.tpl.tribe)s.add(e.tpl.tribe); if(e.tpl.subtype)s.add(e.tpl.subtype); }); return [...TRIBES,...SUBTYPES].filter(t=>s.has(t)); }
function dbFilterActive(f){ return !!(f.type||f.elem||f.cost||f.kw||f.tag||(f.q&&f.q.length)); }
function renderDbFilters(){ const el=$('dbTypeChips'); if(!el)return; const f=dbState.filter||{}; const reg=regForCC(dbState.cc);
  const types=[['','All'],['creature','⚔ Creatures'],['spell','✦ Spells']];
  const trow=types.map(([t,lab])=>`<button class="dbchip${(f.type||'')===t?' on':''}" onclick="dbSetType('${t}')">${lab}</button>`).join('');
  const cols=((CCS[dbState.cc]&&CCS[dbState.cc].colors)||MAJORS).concat(['neutral']);
  const erow=cols.map(c=>c==='neutral'
    ?`<button class="dbechip${f.elem==='neutral'?' on':''}" style="--ec:#6a6a76" onclick="dbSetElem('neutral')" title="Neutral"><span style="color:#9a9aa6;font-size:13px">◇</span></button>`
    :`<button class="dbechip${f.elem===c?' on':''}" style="--ec:var(--${c})" onclick="dbSetElem('${c}')" title="${cap(c)}">${elemBadge(c,15)}</button>`).join('');
  const crow=[1,2,3,4,5,6].map(c=>`<button class="dbchip${f.cost===c?' on':''}" onclick="dbSetCost(${c})">◆${c===6?'6+':c}</button>`).join('');
  const kws=dbAvailKw(reg);
  const kwrow=kws.length?`<div class="dbfrow"><span class="dbflab">Ability</span>${kws.map(k=>`<button class="dbchip sm${f.kw===k?' on':''}" onclick="dbSetKw('${k}')">${DB_KW_LABEL[k]}</button>`).join('')}</div>`:'';
  const tags=dbAvailTags(reg);
  const tagrow=tags.length?`<div class="dbfrow"><span class="dbflab">Tribe</span>${tags.map(t=>`<button class="dbchip sm${f.tag===t?' on':''}" onclick="dbSetTag('${jsq(t)}')">${escHtml(t)}</button>`).join('')}</div>`:'';
  const clear=dbFilterActive(f)?`<span class="dbfspace"></span><button class="dbclear" onclick="dbClearFilters()">Clear ✕</button>`:'';
  el.innerHTML=`<div class="dbfrow">${trow}${clear}</div><div class="dbfrow">${erow}<span class="dbfsep"></span>${crow}</div>${kwrow}${tagrow}`;
}
window.dbSetSearch=(v)=>{ if(!dbState)return; dbState.filter.q=(v||'').toLowerCase(); renderDbPool(); };
window.dbSetType=(t)=>{ if(!dbState)return; dbState.filter.type=t; renderDbFilters(); renderDbPool(); };
window.dbSetElem=(c)=>{ if(!dbState)return; dbState.filter.elem=dbState.filter.elem===c?'':c; renderDbFilters(); renderDbPool(); };
window.dbSetCost=(c)=>{ if(!dbState)return; dbState.filter.cost=dbState.filter.cost===c?null:c; renderDbFilters(); renderDbPool(); };
window.dbSetKw=(k)=>{ if(!dbState)return; dbState.filter.kw=dbState.filter.kw===k?'':k; renderDbFilters(); renderDbPool(); };
window.dbSetTag=(t)=>{ if(!dbState)return; dbState.filter.tag=dbState.filter.tag===t?'':t; renderDbFilters(); renderDbPool(); };
window.dbClearFilters=()=>{ if(!dbState)return; dbState.filter={q:'',type:'',elem:'',cost:null,kw:'',tag:''}; const s=$('dbSearch'); if(s)s.value=''; renderDbFilters(); renderDbPool(); };
window.dbSetSort=(v)=>{ if(!dbState)return; dbState.sort=v; renderDbPool(); };
function renderDbStats(){ const el=$('dbStats'); if(!el)return;
  let cre=0,spl=0; const curve={};
  for(const k in dbState.cards){ const n=dbState.cards[k]; if(!n)continue; const e=CARD_BY_KEY[k]; if(!e)continue;
    if(e.type==='creature')cre+=n; else if(e.type==='spell')spl+=n;
    const c=Math.min(6,e.tpl.c||0); curve[c]=(curve[c]||0)+n; }
  const maxb=Math.max(1,...Object.keys(curve).map(k=>curve[k]));
  let bars='';
  for(let c=0;c<=6;c++){ const v=curve[c]||0; const h=v?Math.max(8,Math.round((v/maxb)*100)):0;
    bars+=`<div class="dbcbar" title="◆${c===6?'6+':c}: ${v} card${v===1?'':'s'}"><div class="dbbarfill${v?'':' empty'}" style="height:${h}%">${v?`<span>${v}</span>`:''}</div><div class="dbbarlab">${c===6?'6+':c}</div></div>`; }
  el.innerHTML=`<div class="dbstatline"><span>⚔ Creatures <b>${cre}</b></span><span>✦ Spells <b>${spl}</b></span></div><div class="dbcurve">${bars}</div>`; }
function renderDbLeaders(){
  const ids=Object.keys(CCS);
  const chip=id=>{const c=CCS[id]; const on=id===dbState.cc?' on':'';
    return `<button class="dbleader${on}" style="--lc:var(--${c.colors[0]})" onclick="dbPickCC('${id}')">${ccPips(c)}<span>${c.name}</span></button>`;};
  const solo=ids.filter(id=>CCS[id].colors.length===1).map(chip).join('');
  const dual=ids.filter(id=>CCS[id].colors.length===2).map(chip).join('');
  $('dbLeaders').innerHTML=`<div class="dbleadlab">Elements</div><div class="dbleadrow">${solo}</div>`+
    `<div class="dbleadlab">Dual Compacts</div><div class="dbleadrow">${dual}</div>`;
}
const DB_TYPE_ORDER={creature:0,building:1,spell:2};
function accentOf(color){ return color?clsOf[color]:'neutral'; } // never clsOf[null]
function costGlyph(e){ return '◆'+(e.tpl.c||0); }   // cost is generic mana only
function cardStat(e){ return e.type==='creature'?`⚔${e.tpl.a} / ♥${e.tpl.h}`
  :e.type==='building'?((e.tpl.eff==='mana'?('◆+'+e.tpl.val+' / turn'):'⚒ trains')+` · ♥${e.tpl.h}`)
  :(e.tpl.trap?'⚠ Trap':'✦ Spell'); }
function cardBlurb(e){ const t=e.tpl;
  if(e.type==='creature'){ const tl=typeLine(t); const p=[]; if(t.fs)p.push('First Strike.'); if(t.up)p.push(`Upkeep ⚒-${t.up}.`); return (tl?`<b>${tl}.</b> `:'')+(p.join(' ')||'A line creature.')+` ⚔${t.a} / ♥${t.h}.`; }
  if(e.type==='building'){ return (t.eff==='mana'?`Structure — yields ◆${t.val} each turn.`:'Structure — trains a worker each turn.')+` Adds ⚒+${t.sup||0} workers to its row.`; }
  if(t.trap) return 'Trap — set face-down (◆1); springs when your opponent summons a creature, dragging it down.';
  if(t.effect==='burn') return `Spell — deal ${t.val} damage to an enemy creature, structure, or face-down.`;
  if(t.effect==='raze') return 'Spell — destroy a target enemy structure.';
  return 'A spell.';
}
function dbCardLabel(e,nameCount){ return nameCount&&nameCount[e.nm]>1?`${e.nm} (${cap(e.color||'neutral')})`:e.nm; }
function dbPoolNameCount(){ const nc={}; regForCC(dbState.cc).forEach(e=>{nc[e.nm]=(nc[e.nm]||0)+1;}); return nc; } // names shared across colors (e.g. Longhouse on a dual leader)
function dbTileInner(e,label){ // full mini-card body: art, name plate, DM bottom bar (spells/traps get a ribbon)
  const t=e.tpl;
  const bar=e.type==='spell'
    ?`<div class="dbcard-rib">${t.trap?'⚠ TRAP':'✦ SPELL'}</div>`
    :e.type==='building'
      ?`<div class="stats"><span class="eff">${t.eff==='mana'?('◆+'+t.val):'⚒'}</span>${t.sup?`<span class="cap plus">⚒+${t.sup}</span>`:''}<span class="hp">♥${t.h}</span></div>`
      :`<div class="stats"><span class="atk">${t.a}</span>${t.up?`<span class="cap neg">⚒-${t.up}</span>`:''}<span class="hp">♥${t.h}</span></div>`;
  return `<div class="dbcard-art">${cardArtImg(t)}</div><div class="dbcard-nm">${escHtml(label)}</div>${bar}`;
}
function renderDbPool(){
  const f=dbState.filter||{}; const q=(f.q||'').trim(); const ty=f.type||'';
  const list=regForCC(dbState.cc).filter(e=>{
    if(ty&&e.type!==ty)return false;
    if(f.elem){ if(f.elem==='neutral'?e.color:e.color!==f.elem)return false; }
    if(f.cost){ const cc=e.tpl.c||0; if(f.cost===6?cc<6:cc!==f.cost)return false; }
    if(f.kw){ if(f.kw==='fs'){ if(!e.tpl.fs)return false; } else if(e.tpl.kw!==f.kw)return false; }
    if(f.tag&&e.tpl.tribe!==f.tag&&e.tpl.subtype!==f.tag)return false;
    if(q&&!(`${e.nm} ${e.type} ${e.color||''} ${e.tpl.kw||''} ${e.tpl.tribe||''} ${e.tpl.subtype||''} ${e.tpl.fs?'first strike':''}`.toLowerCase().includes(q)))return false;
    return true;
  }).sort(DB_SORT[dbState.sort]||DB_SORT.type);
  const cnt=$('dbPoolCount'); if(cnt)cnt.textContent=' · '+list.length;
  const nameCount=dbPoolNameCount();
  $('dbPool').innerHTML=(list.map(e=>{
    const n=dbState.cards[e.key]||0; const sel=e.key===dbState.sel?' sel':''; const label=dbCardLabel(e,nameCount);
    return `<div class="dbcard ${accentOf(e.color)}${sel}" data-key="${escHtml(e.key)}" onclick="dbPoolClick('${jsq(e.key)}')" title="${escHtml(label)}">`+
      `<div class="dbcard-cost">${e.tpl.c||0}</div>`+(n>0?`<div class="dbcount-badge">${n}</div>`:'')+
      dbTileInner(e,label)+`</div>`;
  }).join('')) || '<div class="dbempty">No cards match.</div>';
}
function renderDbDeck(){
  const keys=Object.keys(dbState.cards).filter(k=>dbState.cards[k]>0&&CARD_BY_KEY[k])
    .sort((a,b)=>{const ea=CARD_BY_KEY[a],eb=CARD_BY_KEY[b];return (DB_TYPE_ORDER[ea.type]-DB_TYPE_ORDER[eb.type])||((ea.tpl.c||0)-(eb.tpl.c||0));});
  if(!keys.length){ $('dbDeckList').innerHTML='<div class="dbempty">Click cards on the right to add them here.</div>'; return; }
  const nameCount=dbPoolNameCount();
  $('dbDeckList').innerHTML=keys.map(k=>{const e=CARD_BY_KEY[k]; const n=dbState.cards[k]; const sel=k===dbState.sel?' sel':''; const label=dbCardLabel(e,nameCount);
    return `<div class="dbcard ${accentOf(e.color)}${sel}" onclick="dbDeckClick('${jsq(k)}')" title="${escHtml(label)}">`+
      `<div class="dbcard-cost">${e.tpl.c||0}</div><div class="dbcount-badge">${n}</div>`+
      `<div class="dbcard-x" onclick="event.stopPropagation();dbStep('${jsq(k)}',-1)">✕</div>`+
      dbTileInner(e,label)+`</div>`;
  }).join('');
}
function renderDbDetail(){
  const el=$('dbDetail'); const e=dbState.sel&&CARD_BY_KEY[dbState.sel];
  if(!e){ el.innerHTML='<div class="dbd-empty">Select a card to see its details.</div>'; return; }
  const n=dbState.cards[e.key]||0; const total=deckTotal(dbState.cards);
  const label=dbCardLabel(e,dbPoolNameCount());
  const elem=e.color?`${elemBadge(e.color,18)}${cap(e.color)}`:'◇ Neutral';
  el.innerHTML=`<div class="dbd-art">${cardArtImg(e.tpl,'big')}</div>`+
    `<div class="dbd-name">${escHtml(label)}</div>`+
    `<div class="dbd-line"><span style="color:var(--spawn)">${costGlyph(e)}</span><span>${cardStat(e)}</span></div>`+
    `<div class="dbd-elem">${elem}</div>`+
    `<div class="dbd-desc">${cardBlurb(e)}</div>`+
    `<div class="dbd-step"><button onclick="dbStep('${jsq(e.key)}',-1)" ${n<=0?'disabled':''}>−</button><span class="dbcount">${n}</span>`+
    `<button onclick="dbStep('${jsq(e.key)}',1)" ${(n>=MAX_COPIES||total>=DECK_SIZE)?'disabled':''}>+</button></div>`;
}
window.dbPoolClick=(key)=>{ if(!dbState)return; const added=dbAdd(key); dbState.sel=key; renderDeckBuilder();
  if(!added){ const nm=CARD_BY_KEY[key]?CARD_BY_KEY[key].nm:'that card'; $('dbHint').textContent=deckTotal(dbState.cards)>=DECK_SIZE?`Deck is full — ${DECK_SIZE} cards.`:`Already at ${MAX_COPIES} copies of ${nm}.`; } };
window.dbDeckClick=(key)=>{ if(!dbState)return; dbState.sel=key; renderDeckBuilder(); };
function dbAdd(key){ if(!dbState)return false; const n=dbState.cards[key]||0; if(n>=MAX_COPIES||deckTotal(dbState.cards)>=DECK_SIZE)return false; dbState.cards[key]=n+1; return true; }
function currentDbDeck(){ const cards={}; for(const [k,v] of Object.entries(dbState.cards)) if(v>0) cards[k]=v; return {name:(dbState.name||'').trim(),cc:dbState.cc,cards}; }
function refreshDbCounter(){
  const total=deckTotal(dbState.cards); const deck=currentDbDeck(); const valid=deckValid(deck);
  const el=$('dbCounter'); el.innerHTML=`<b>${total}</b><i>/${DECK_SIZE}</i>`; el.className='dbcounter '+(valid?'ok':'bad');
  el.style.setProperty('--pct',Math.min(100,Math.round(total/DECK_SIZE*100)));
  const decks=loadDecks(); const room=dbState.editIndex!=null||decks.length<MAX_DECKS; const named=deck.name.length>0;
  $('dbSave').disabled=!(valid&&room&&named);
  $('dbHint').textContent=!named?'Name your deck.':(!room?('You have '+MAX_DECKS+' decks — edit or delete one.'):(valid?'Ready to save.':deckErrors(deck)[0]));
}
window.dbPickCC=(id)=>{ if(!dbState)return; dbState.cc=id; dbState.filter.elem='';dbState.filter.kw='';dbState.filter.tag=''; for(const k of Object.keys(dbState.cards)) if(!cardColorOK(k,id)) delete dbState.cards[k]; if(dbState.sel&&!cardColorOK(dbState.sel,id))dbState.sel=null; renderDeckBuilder(); };
window.dbStep=(key,dir)=>{ if(!dbState)return; if(dir>0&&deckTotal(dbState.cards)>=DECK_SIZE)return; const n=(dbState.cards[key]||0)+(+dir); if(n<=0)delete dbState.cards[key]; else dbState.cards[key]=Math.min(MAX_COPIES,n); renderDeckBuilder(); };
window.dbSave=()=>{ if(!dbState)return; const deck=currentDbDeck(); if(!deckValid(deck)||!deck.name)return; const decks=loadDecks();
  if(dbState.editIndex!=null&&decks[dbState.editIndex]) decks[dbState.editIndex]=deck; else decks.push(deck);
  if(saveDecks(decks)){ dbReturn(); } else $('dbHint').textContent='Could not save — browser storage is blocked.'; };
window.dbCancel=()=>{ dbReturn(); };
/* -- main-menu drifting embers (procedural; skipped under reduced motion) -- */
(function(){ if(window.matchMedia&&matchMedia('(prefers-reduced-motion: reduce)').matches)return;
  const host=document.querySelector('#mainMenu .mmembers'); if(!host)return;
  for(let i=0;i<16;i++){ const s=document.createElement('span'); s.className='mmember'; const sz=2+Math.random()*3.5;
    s.style.cssText=`left:${(Math.random()*100).toFixed(1)}%;width:${sz.toFixed(1)}px;height:${sz.toFixed(1)}px;animation-duration:${(9+Math.random()*14).toFixed(1)}s;animation-delay:${(-Math.random()*22).toFixed(1)}s;`;
    if(Math.random()<.35){ s.style.background='var(--gold)'; s.style.boxShadow='0 0 6px 1px rgba(217,176,74,.45)'; }
    host.appendChild(s); }
})();
/* -- deck-builder pool hover zoom (pointer:fine only; #dbZoom is pointer-events:none) -- */
(function(){
  if(!window.matchMedia||!matchMedia('(pointer:fine)').matches)return;
  const z=document.createElement('div'); z.id='dbZoom'; document.body.appendChild(z);
  let key=null;
  const hide=()=>{ key=null; z.classList.remove('on'); };
  const move=ev=>{ const w=z.offsetWidth||250,h=z.offsetHeight||320;
    let x=ev.clientX-w-22, y=Math.max(8,Math.min(innerHeight-h-8,ev.clientY-h/2));
    if(x<8)x=ev.clientX+22;                                    // pool is the right column → prefer left of cursor
    z.style.transform=`translate(${Math.round(x)}px,${Math.round(y)}px)`; };
  document.addEventListener('mouseover',ev=>{
    const t=ev.target.closest&&ev.target.closest('#dbPool .dbcard');
    if(!t){ hide(); return; }
    const k=t.dataset.key; const e=k&&CARD_BY_KEY[k]; if(!e){ hide(); return; }
    if(k!==key){ key=k;
      z.innerHTML=`<div class="dbz-art">${cardArtImg(e.tpl,'big')}</div><div class="dbz-nm">${escHtml(e.nm)}</div>`+
        `<div class="dbz-line"><span style="color:var(--spawn)">${costGlyph(e)}</span><span>${cardStat(e)}</span></div>`+
        `<div class="dbz-desc">${cardBlurb(e)}</div>`;
      z.style.setProperty('--zc',e.color?`var(--${e.color})`:'#6a6a76');
      z.classList.add('on'); }
    move(ev);
  });
  document.addEventListener('mousemove',ev=>{ if(key)move(ev); });
})();

/* -- solo: pick your deck, then your opponent -- */
let soloYou=null;
function showSoloDeckPick(){ hideAllScreens(); soloYou=null; renderSoloDeckPick(); showScreen('soloSelect'); }
function deckBoxBg(cc){ const a=ELEMENTS[cc.colors[0]], b=ELEMENTS[cc.colors[1]||cc.colors[0]];
  return `background:radial-gradient(130% 170% at 84% -12%,${b.bg[0]} 0%,transparent 55%),radial-gradient(150% 190% at 12% 8%,${a.bg[0]} 0%,${a.bg[1]} 55%,${a.bg[2]} 100%)`; }
function renderSoloDeckPick(){
  const box=$('soloSelect').querySelector('.csbox'); const decks=loadDecks();
  const premades=Object.keys(CCS).map(id=>{const c=CCS[id]; const E=ELEMENTS[c.colors[0]];
    return `<button class="deckbox" style="${deckBoxBg(c)}" onclick='soloPickDeck({"kind":"premade","cc":"${id}"})'>`+
      `<span class="dbxmark">${ELEMENTS[c.colors[c.colors.length-1]].glyph}</span>`+
      `<span class="dbxtop"><span class="dbxpips">${ccPips(c)}</span><span class="dbxbadge">♥ ${c.hp} · ⚒ ${c.wk}</span></span>`+
      `<span class="dbxname" style="color:${E.accent}">${c.name}</span>`+
      `<span class="dbxsub">Premade · auto-built ${c.colors.map(cap).join(' + ')}</span>`+
      `<span class="dbxplay">Play ▶</span></button>`;}).join('');
  const customs=decks.map((d,i)=>{const c=CCS[d.cc]; const E=ELEMENTS[c.colors[0]]; const valid=deckValid(d); const total=deckTotal(d.cards);
    return `<div class="deckbox custom${valid?'':' invalid'}" role="button" style="${deckBoxBg(c)}" ${valid?`onclick='soloPickDeck({"kind":"custom","index":${i}})'`:''}>`+
      `<span class="dbxmark">${ELEMENTS[c.colors[c.colors.length-1]].glyph}</span>`+
      `<span class="dbxtop"><span class="dbxpips">${ccPips(c)}</span><span class="dbxbadge${valid?'':' bad'}">${total}/${DECK_SIZE}${valid?'':' · invalid'}</span></span>`+
      `<span class="dbxname" style="color:${E.accent}">${escHtml(d.name||'(unnamed)')}</span>`+
      `<span class="dbxsub">${c.name} · custom deck</span>`+
      `<span class="dbxacts"><button onclick="event.stopPropagation();openDeckBuilder(${i},'solo')">✎ Edit</button><button onclick="event.stopPropagation();soloDelete(${i})">✕</button>${valid?`<button class="go" onclick='event.stopPropagation();soloPickDeck({"kind":"custom","index":${i}})'>Play ▶</button>`:''}</span>`+
    `</div>`;}).join('');
  const newSlot=decks.length<MAX_DECKS
    ? `<button class="deckbox csnew2" onclick="openDeckBuilder(null,'solo')">＋<small>New Deck</small></button>`
    : `<div class="csempty">Deck limit reached (${MAX_DECKS}/${MAX_DECKS}) — edit or delete one to free a slot.</div>`;
  box.innerHTML=`<h1>Choose Your Deck</h1>`+
    `<div class="cssub">Premades mirror the classic auto-decks. Build your own with the Deck Builder.</div>`+
    `<div class="dbxsect">Premade</div><div class="dbxgrid">${premades}</div>`+
    `<div class="dbxsect">Your decks (${decks.length}/${MAX_DECKS})</div><div class="dbxgrid">${customs}${newSlot}</div>`+
    `<button class="csback" onclick="showMainMenu()">← menu</button>`;
}
window.soloDelete=(i)=>{ deleteDeck(i); renderSoloDeckPick(); };
window.soloPickDeck=(sel)=>{ soloYou=sel; renderSoloFoePick(); };
function renderSoloFoePick(){
  const box=$('soloSelect').querySelector('.csbox'); const ids=Object.keys(CCS);
  const cards=ids.map(id=>{const c=CCS[id]; const E=ELEMENTS[c.colors[0]];
    return `<button class="deckbox" style="${deckBoxBg(c)}" onclick="soloStart('${id}')">`+
      `<span class="dbxmark">${ELEMENTS[c.colors[c.colors.length-1]].glyph}</span>`+
      `<span class="dbxtop"><span class="dbxpips">${ccPips(c)}</span><span class="dbxbadge">♥ ${c.hp} · ⚒ ${c.wk}</span></span>`+
      `<span class="dbxname" style="color:${E.accent}">${c.name}</span>`+
      `<span class="dbxsub">${c.desc}</span>`+
      `<span class="dbxplay">Fight ▶</span></button>`;}).join('')+
    `<button class="deckbox" style="background:radial-gradient(150% 190% at 12% 8%,#241f30 0%,#15121c 55%,#08070b 100%)" onclick="soloStart('__rand')">`+
      `<span class="dbxmark">？</span><span class="dbxtop"><span class="dbxbadge">🎲 random</span></span>`+
      `<span class="dbxname" style="color:var(--gold)">Random</span><span class="dbxsub">A surprise opponent, rolled when the duel begins.</span>`+
      `<span class="dbxplay">Fight ▶</span></button>`;
  box.innerHTML=`<h1>Choose Your Opponent</h1><div class="cssub">Your deck is set. Pick who you face.</div><div class="dbxgrid">${cards}</div><button class="csback" onclick="showSoloDeckPick()">← back</button>`;
}
window.soloStart=(foeId)=>{
  const ids=Object.keys(CCS); if(foeId==='__rand')foeId=ids[Math.floor(Math.random()*ids.length)];
  let youId,youDeck;
  if(soloYou&&soloYou.kind==='custom'){ const d=loadDecks()[soloYou.index]; if(!d||!deckValid(d)){ showSoloDeckPick(); return; } youId=d.cc; youDeck=expandDeck(d); }
  else { youId=(soloYou&&soloYou.cc)||'fire'; youDeck=undefined; }
  $('soloSelect').style.display='none';
  startGame(youId,foeId,youDeck,undefined);
};
function dealOpening(o){ // your command center is pre-placed, so the opening hand is simply 4 cards
  G.P[o].hand=[]; for(let i=0;i<4;i++)drawCard(o);
}
function drawCard(o){if(G.P[o].deck.length){const t=G.P[o].deck.pop();G.P[o].hand.push({kind:'handcard',id:uid++,type:t.type,color:t.type==='spell'?null:(t.color||G.P[o].color),nm:t.nm,a:t.a,h:t.h,c:t.c,fs:t.fs,up:t.up,sup:t.sup,eff:t.eff,val:t.val,ic:t.ic,art:t.art,trap:t.trap,effect:t.effect,target:t.target,trigger:t.trigger,
  kw:t.kw,det:t.det,ward:t.ward,wardhp:t.wardhp,reap:t.reap,grow:t.grow,hatch:t.hatch,into:t.into,entrench:t.entrench,tribe:t.tribe,subtype:t.subtype});}}
function log(html,cls=''){const p=document.createElement('p');if(cls)p.className=cls;p.innerHTML=html;$('log').prepend(p);}
window.toggleLog=function(on){ $('logPanel').style.display=on?'flex':'none'; };
window.toggleRules=function(on){ $('rulesPanel').style.display=on?'flex':'none'; };

