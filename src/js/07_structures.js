/* ----- STRUCTURE UPGRADES: level a built structure up IN PLACE (keeps its tile + stored ◆), following
   its `up2` chain. Row-gated tiers (Keep/Citadel back, Barracks front) enforce the RTS "line lives in
   that row" feel. Branching (Outpost → Cannon Tower | Bastion) just lists more than one target. ----- */
function upgradeTargets(o){
  if(!o||o.kind!=='building'||o.cc||!o.bid)return [];
  const src=resolveStruct(o.bid,o.color); const ids=(src&&src.up2)||[];
  return ids.map(bid=>resolveStruct(bid,o.color)).filter(Boolean);
}
function upgradeWhy(owner,o,key,def){
  if(def.row&&whichOf(key)!==def.row) return def.row==='back'?'only in your back row':'only in your front row';
  if(manaTotal(owner)<def.c) return 'need ◆'+def.c;
  if((def.sup||0)<0 && (rowWorkers(owner,whichOf(key))-(o.sup||0)+(def.sup||0))<0) return 'row has no ⚒ to spare';
  return '';
}
function canUpgradeTo(owner,o,key,def){ return !!def && upgradeWhy(owner,o,key,def)===''; }
function applyUpgrade(o,def){                     // swap the tier's stats onto the SAME unit (id/owner/bank/tile kept)
  o.bid=def.bid; o.nm=def.nm; o.eff=def.eff; o.val=def.val||0; o.sup=def.sup||0; o.ic=def.ic;
  const dmg=Math.max(0,(o.maxh??def.h)-o.h);      // upgrading repairs NOTHING: damage carries through the rebuild —
  o.maxh=def.h; o.h=Math.max(1,def.h-dmg);        // the structure gains only the new tier's extra max HP
  o.c=def.c; o.art=def.art;
  if(def.color) o.color=def.color;
}
window.upgradeStruct=function(key,i,bid){
  if(!acting())return;
  const o=rowArr(key)[i]; if(!o||o.kind!=='building'||o.owner!=='you'||o.cc)return;
  const def=upgradeTargets(o).find(d=>d.bid===bid); if(!def)return;
  if(!canUpgradeTo('you',o,key,def)){ setHint(`Can’t upgrade to <b>${escHtml(def.nm)}</b> — ${escHtml(upgradeWhy('you',o,key,def))}.`); render(); return; }
  const oldNm=o.nm; payAny('you',def.c); applyUpgrade(o,def);
  log(`<span class="y">You upgrade ${escHtml(oldNm)} into a ${escHtml(def.nm)}.</span>`,'y');
  G.cardMenu=null; syncWorkers('you'); afterDeploy('you'); defaultHint(); render(); checkWin();
};
// find a placed unit's board coords (key + slot) for its owner
function buildingLoc(owner,unit){
  for(const w of ['back','front','center']){ const a=cellArr(owner,w); if(!a)continue; const i=a.indexOf(unit); if(i>=0) return {key:(w==='center'?'center':rowKeyFor(owner,w)), i}; }
  return null;
}
// AI: upgrade one eligible structure in place (chain order; branches take the first affordable target)
function aiUpgrade(owner){
  for(const b of ownBuildings(owner)){
    const loc=buildingLoc(owner,b); if(!loc)continue;
    const def=upgradeTargets(b).find(d=>canUpgradeTo(owner,b,loc.key,d));
    if(!def)continue;
    const old=b.nm; payAny(owner,def.c); applyUpgrade(b,def); syncWorkers(owner);
    log(`<span class="e">Opponent upgrades ${escHtml(old)} into a ${escHtml(def.nm)}.</span>`,'e');
    return true;
  }
  return false;
}
// ----- AI building: foundry -> a forge for each color -> longhouse / bulwark -----
function aiBuild(owner){
  const ccId=G.P[owner].cc, list=buildList(ccId);
  // priority order = buildList order; cap how many of each the AI wants
  const CAP={foundry:1,encampment:1,longhouse:1,vault:1,outpost:1,bulwark:1,tower:2,reliquary:1};
  for(const def of list){
    if(CAP[def.bid] && ownBuildings(owner).filter(b=>bidLineage(b).indexOf(def.bid)>=0).length>=CAP[def.bid]) continue;  // an upgraded tier still counts toward its base's cap
    if(def.bid==='forge' && ownBuildings(owner).some(b=>bidLineage(b).indexOf('forge')>=0&&b.color===def.color)) continue;   // one forge (or its Grand upgrade) per color
    if(def.bid==='grandforge' && ownBuildings(owner).some(b=>b.bid==='grandforge'&&b.color===def.color)) continue;
    if(!canBuild(owner,def)) continue;
    const which=['back','front'].find(w=>freeDeploySlot(owner,w)>=0&&placeRowOK(owner,w,def)); if(!which)continue;
    const slot=aiPickDeploySlot(owner,which);
    payAny(owner,def.c); cellArr(owner,which)[slot]=mkBld(def,owner); syncWorkers(owner);
    log(`<span class="e">Opponent raises a ${escHtml(def.nm)}.</span>`,'e');
    return true; // build one per call
  }
  return false;
}
function toGrave(owner,obj){
  if(!obj)return; let rec;
  if(obj.kind==='creature') rec={type:obj.worker?'villager':'creature',nm:obj.nm,a:obj.a,h:obj.maxh??obj.h,c:obj.c,up:obj.up,fs:obj.fs,art:obj.art,color:obj.color,token:!!obj.token,
    kw:obj.token?null:obj.kw,det:obj.det,ward:obj.ward,wardhp:obj.wardhp,reap:obj.reap,grow:obj.grow,hatch:obj.hatch,into:obj.into,entrench:obj.entrench,tribe:obj.tribe||null,subtype:obj.subtype||null};
  else if(obj.kind==='building') rec={type:'building',nm:obj.nm,h:obj.maxh??obj.h,c:obj.c,eff:obj.eff,val:obj.val,sup:obj.sup,ic:obj.ic};
  else if(obj.kind==='charge') rec={type:obj.ctype||'creature',nm:obj.card.nm,a:obj.card.a,h:obj.card.h,c:obj.card.c,up:obj.card.up,sup:obj.card.sup,eff:obj.card.eff,val:obj.card.val,ic:obj.card.ic};
  else if(obj.kind==='trap') rec={type:'spell',nm:obj.card.nm,c:obj.card.c,trap:true,effect:obj.card.effect,val:obj.card.val,ic:obj.card.ic};
  else return;
  G.P[owner].grave.push(rec);
}

function bootstrap(){
  document.body.appendChild($('cardActions'));
  window.addEventListener('scroll',()=>placeCardMenu(),true);
  window.addEventListener('resize',()=>placeCardMenu());
  showMainMenu();
}
function ccPips(def){return def.colors.map(c=>elemBadge(c,15)).join('');}
function applyCharacterUI(){
  const cy=CCS[G.P.you.cc], cf=CCS[G.P.foe.cc];
  document.documentElement.style.setProperty('--youelem', `var(--${cy.colors[0]})`); // tint the HUD frame by the player's element
  document.documentElement.style.setProperty('--foeelem', `var(--${cf.colors[0]})`); // tint the foe wall by the opponent's element
  { const gl=$('hudGlyphL'), gr=$('hudGlyphR'); if(gl)gl.textContent=ELEMENTS[cy.colors[0]].glyph; if(gr)gr.textContent=ELEMENTS[cy.colors[cy.colors.length>1?1:0]].glyph; }
  { const gl=$('hudGlyphFL'), gr=$('hudGlyphFR'); if(gl)gl.textContent=ELEMENTS[cf.colors[0]].glyph; if(gr)gr.textContent=ELEMENTS[cf.colors[cf.colors.length>1?1:0]].glyph; }
  const fName=$('foeName'); fName.style.color=`var(--${cf.colors[0]})`;
  fName.innerHTML=`<span class="cdot" style="background:var(--${cf.colors[0]})"></span>${cf.name}`;
  const yName=$('youName'); yName.style.color=`var(--${cy.colors[0]})`;
  yName.innerHTML=`${cy.name}<span class="cdot" style="background:var(--${cy.colors[0]})"></span>`;
  const cn=$('cmdName'); cn.style.color=`var(--${cy.colors[0]})`;
  cn.innerHTML=`${ccPips(cy)}${cy.name}`;
  $('cmdPass').innerHTML=`♥${cy.hp} · ⚒${cy.wk} workers · ${cy.colors.map(cap).join(' + ')} mana. Your command center holds the base — it cannot move or attack. <b>If it falls, you lose.</b>`;
  $('foeSide').style.borderColor=`var(--${cf.colors[0]})`;
  $('youSide').style.borderColor=`var(--${cy.colors[0]})`;
}
