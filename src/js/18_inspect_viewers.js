/* ---------- tap-to-inspect: every card explains itself ---------- */
const FINE_POINTER=!!(window.matchMedia&&matchMedia('(hover:hover) and (pointer:fine)').matches);
function addInspect(host,fn,own){
  host._inspect=fn;                 // read by hover-to-inspect (fine pointers) and the selection preview —
  if(FINE_POINTER) return;          // re-set on every render, so it survives rebuilds. The ⓘ button is gone.
  // Touch replacement: when the board is INERT (opponent's turn, busy/FX, a respond window, MP
  // freeze, draw/end phase, game over — states where decorate wires no game click) a tap has no
  // game meaning, so it READS the card instead; ⓘ used to cover exactly these moments. During
  // your own action phase, taps keep their game meaning — onCell/onHand show card text there
  // (foe-tap inspect + the left selection preview).
  host.addEventListener('click',function(){
    var inert = typeof G==='undefined' || G.over || G.busy || G.turn!=='you'
      || G.phase==='draw' || G.phase==='end'
      || (typeof RESP!=='undefined' && RESP.active)
      || (typeof MP!=='undefined' && MP.frozen)
      || (G.upkeep && own!=='you');            // upkeep: your creatures keep their settle-tap, foe cards read
    if(inert) fn();
  });
}
function bldEffectText(eff,val,sup){
  const supTxt=(sup>0)?` Raises <b>⚒+${sup}</b> in its row.`:(sup<0)?` Costs <b>⚒${sup}</b> in its row — build it where workers are to spare.`:'';
  if(eff==='mana') return `<b>Forge.</b> Yields <b>◆${val} mana</b> at the start of its owner's turn.${supTxt}`;
  if(eff==='villager') return `<b>Longhouse.</b> Quarters workers: its ⚒ support is what staffs the row it stands in.${supTxt}`;
  if(eff==='wall') return `<b>Bulwark.</b> A heavy body that can be raided instead of the line behind it, but never moves, attacks, or interposes.${supTxt}`;
  if(eff==='damage') return `<b>Cannon Tower.</b> Strikes the nearest enemy creature for <b>${val}</b> at the start of its owner's turn.${supTxt}`;
  if(eff==='vault') return `<b>Mana Vault.</b> Unspent mana <b>drains at the end of your turn</b> — your vaults keep up to <b>◆${val}</b> of it banked. Upgrade it to hold more.${supTxt}`;
  if(eff==='revive') return `<b>Reliquary.</b> Once per turn at upkeep, returns your most recently fallen creature to your hand.${supTxt}`;
  return `Structure with no upkeep effect.${supTxt}`;
}
function showInspect(title,body){
  const vp=$('viewerPanel');
  const box=vp.querySelector('.box');
  const parts=title.split('·');
  const name=parts.shift().trim();
  const sub=parts.join(' · ').trim();
  const hov=!!window.__hoverInspecting;   // hover mode: same panel, non-blocking, no Close button
  box.innerHTML=`<div class="ihead">${name}${sub?`<span class="isub">${sub}</span>`:''}</div>
    <div class="ibody">${body}</div>${hov?'':'<button class="pclose" onclick="closeViewer()">Close</button>'}`;
  vp.classList.add('left');
  vp.classList.toggle('hover',hov);
  vp.style.display='flex';
}
function bigArt(card){return (card&&card.nm)?`<div class="iart">${cardArtImg(card,'big')}</div>`:'';}
// a full-size DM-framed card for the inspect panel — the same chrome classes the hand cards use,
// scaled up (cost circle · name/race banner · art · type plate · white ability box · power/gem/♥ footer)
function inspCardHTML(c,opts){
  opts=opts||{};
  const isB=(c.type==='building')||(c.kind==='building');
  const isS=c.type==='spell'||!!c.trap;
  const cls=isS?'hcs':isB?'hcb':(clsOf[c.color]||'');
  const tl=opts.tl||(isS?(c.trap?'Trap':'Spell'):isB?'Structure':(typeLine(c)||'Creature'));
  const rib=isS?(c.trap?'⚠ TRAP':'✦ SPELL'):isB?'STRUCTURE':'CREATURE';
  const art=c.art?`<div class="artwin">${cardArtImg(c,'big')}</div>`:`<div class="artwin ph"><span class="bic">${isS?(c.trap?'⚠':'✦'):isB?(c.ic||'⌂'):'⚔'}</span></div>`;
  const rules=(opts.rules!=null?opts.rules:abilityBrief(c))||'';
  const wd=isB?((c.sup)?`<span class="cap plus">⚒+${c.sup}</span>`:'')
             :(!isS&&c.up?`<span class="cap neg">⚒-${c.up}</span>`:'');
  const pow=isS?'<span class="atk"></span>':isB?`<span class="eff">${c.eff==='mana'?('◆+'+(c.val||0)):'⚒'}</span>`:`<span class="atk">${c.a!=null?c.a:''}</span>`;
  const hp=isS?'<span class="hp"></span>':`<span class="hp">♥${opts.hp!=null?opts.hp:c.h}</span>`;
  const gem=c.color?`<span class="costgem">${elemGem(c.color,18)}</span>`:'';   // element beside the cost
  return `<div class="hc big ${cls}"><div class="hchead"><div class="cost">${c.c!=null?c.c:''}</div>${gem}<div class="nmw"><div class="nm">${c.nm}</div><div class="tl">${tl}</div></div></div>`+
    `<div class="hcbody">${art}<div class="ribbon">${rib}</div><div class="rules">${rules}</div>`+
    `<div class="stats">${pow}<span class="mid">${wd}</span>${hp}</div></div></div>`;
}
function showInspectCard(html,extra){
  const vp=$('viewerPanel'); const box=vp.querySelector('.box');
  const hov=!!window.__hoverInspecting;
  box.innerHTML=`<div class="icardwrap">${html}${extra||''}</div>${hov?'':'<button class="pclose" onclick="closeViewer()">Close</button>'}`;
  vp.classList.add('left'); vp.classList.toggle('hover',hov); vp.style.display='flex';
}
function inspectRef(owner,which,i){
  const o=cellArr(owner,which)[i]; if(!o)return; const me=owner==='you';
  if(o.kind==='charge'){
    if(!me){ showInspect('Face-down · hidden',
      `Your opponent's set card — its identity is concealed.<br>Banked so far: <b>◆${o.inv}</b> (resources are always public; the card is not).<br>It could be a creature <i>or</i> a structure. <b>Attack it to provoke it:</b> if it's under-funded it's interrupted and destroyed (banked ◆ lost); if it's fully funded it flips up and fights back.`); return; }
    const isB=o.ctype==='building';
    const eff=isB?bldEffectText(o.card.eff,o.card.val,o.card.sup):([o.card.up?`<b>Upkeep ⚒-${o.card.up}.</b>`:'',o.card.fs?'<b>First Strike.</b> Deals its damage first.':'',kwText(o.card)].filter(Boolean).join('<br>')||'');
    const ready=o.inv>=o.card.c;
    showInspectCard(inspCardHTML(Object.assign({type:isB?'building':'creature'},o.card),{rules:eff}),
      `<div class="ifund">face-down — banked <b>◆${o.inv}</b>${ready?' ✓ funded':` · ◆${o.card.c-o.inv} more to fund`}</div>`);
    return;
  }
  if(o.kind==='trap'){
    if(!me){ showInspect('Face-down · hidden', `Your opponent's set card — concealed. It may be a <b>trap</b>: attacking or summoning into it can spring it. Probe with care.`); return; }
    showInspectCard(inspCardHTML(Object.assign({type:'spell'},o.card),{rules:spellText(o.card)}),
      `<div class="ifund">armed — springs on your opponent's turn</div>`);
    return;
  }
  if(o.kind==='building'){
    const bchips=[]; if(o.bank>0)bchips.push(`<span class="ichip gold" title="stored mana">◆${o.bank}</span>`);
    let rules=bldEffectText(o.eff,o.val,o.sup);
    const ups=me?upgradeTargets(o):[];   // show the upgrade path on your own structures
    if(ups.length) rules+=`<br><b>⬆ Upgrades to:</b> ${ups.map(d=>`${escHtml(d.nm)} <span style="color:var(--ink-dim)">(◆${d.c}${d.row?', '+d.row+' row':''})</span>`).join(' · ')}`;
    showInspectCard(inspCardHTML(o,{rules,hp:`${o.h}/${o.maxh}`}),
      bchips.length?`<div class="ichips">${bchips.join('')}</div>`:'');
    return;
  }
  // creature — the full DM frame; status is symbol chips under the card, never prose on it
  if(o.worker){ showInspect('⚒ Worker · Minion','<b>Harvester.</b> Harvests with its row. Blocks; cannot attack.'); return; }
  const abilities=[];
  if(o.up>0) abilities.push(`<b>Upkeep ⚒-${o.up}.</b>`);
  if(o.fs) abilities.push('<b>First Strike.</b> Deals its damage first.');
  { const kt=kwText(o); if(kt) abilities.push(kt); }
  const chips=[];
  if(o.sick)chips.push('<span class="ichip" title="summoning-sick">💤</span>');
  if(o.moved)chips.push(`<span class="ichip" title="moved${o.moved2?' twice':''} this turn">⤧${o.moved2?'×2':''}</span>`);
  if(o.tapped)chips.push('<span class="ichip" title="has acted — tapped">⟳</span>');
  if(o.bank>0)chips.push(`<span class="ichip gold" title="stored mana">◆${o.bank}</span>`);
  if(which==='center')chips.push('<span class="ichip" title="contesting the center">⚑</span>');
  showInspectCard(inspCardHTML(o,{rules:abilities.join('<br>'),hp:`${o.h}/${o.maxh}`}),
    chips.length?`<div class="ichips">${chips.join('')}</div>`:'');
}
function inspectHand(i){
  const c=G.P.you.hand[i]; if(!c)return; const isB=c.type==='building';
  let rules;
  if(c.type==='spell') rules=spellText(c);
  else if(isB) rules=bldEffectText(c.eff,c.val,c.sup);
  else rules=[c.up?`<b>Upkeep ⚒-${c.up}.</b>`:'',c.fs?'<b>First Strike.</b> Deals its damage first.':'',kwText(c)].filter(Boolean).join('<br>');
  showInspectCard(inspCardHTML(c,{rules}));
}

/* ---------- deck / graveyard viewer ---------- */
window.openViewer=function(zone,owner){
  $('viewerPanel').classList.remove('left','hover');
  const isDeck=zone==='deck';
  const cards=isDeck?G.P[owner].deck:G.P[owner].grave;
  const title=(owner==='you'?'Your ':'Opponent ')+(isDeck?'Deck':'Graveyard');
  const box=$('viewerPanel').querySelector('.box');
  if(isDeck&&owner==='foe'){
    box.innerHTML=`<div class="vtitle">${title}</div><div class="vmeta">Hidden — ${cards.length} card${cards.length===1?'':'s'} remaining.</div><button class="pclose" onclick="closeViewer()">Close</button>`;
    $('viewerPanel').style.display='flex'; return;
  }
  const groups={};
  cards.forEach(c=>{ const key=c.nm+'|'+c.c+'|'+(c.type||''); (groups[key]=groups[key]||{c,n:0}).n++; });
  const items=Object.values(groups).sort((a,b)=>((a.c.c||0)-(b.c.c||0))||a.c.nm.localeCompare(b.c.nm));
  const grid=items.map(({c,n})=>{
    const isB=c.type==='building'; const isS=c.type==='spell';
    const cls=isS?'hcs':isB?'hcb':(c.type==='villager'?'vil':clsOf[G.P[owner].color]||'crt');
    const stat=isS?`<span class="eff" style="color:#c9a0ff">${c.trap?'⚠ Trap':'✦ Spell'}</span>`
                :isB?`<span style="color:var(--spawn)">${c.eff==='mana'?('◆+'+(c.val||0)):'⚒'}</span><span class="hp">♥${c.h}</span>`
                  :`<span class="atk">⚔${c.a||0}</span><span class="hp">♥${c.h}</span>`;
    return `<div class="vcard ${cls}">${n>1?`<div class="xn">×${n}</div>`:''}<div class="nm">${c.nm}</div><div class="vc">◆${c.c}</div><div class="stats">${stat}</div></div>`;
  }).join('');
  box.innerHTML=`<div class="vtitle">${title}</div>
    <div class="vmeta">${cards.length} card${cards.length===1?'':'s'}${isDeck?' · order hidden, grouped by type':' · everything destroyed so far'}</div>
    <div class="vgrid">${grid||'<div class="vmeta">Empty.</div>'}</div>
    <button class="pclose" onclick="closeViewer()">Close</button>`;
  $('viewerPanel').style.display='flex';
};
window.closeViewer=()=>{$('viewerPanel').style.display='none';$('viewerPanel').classList.remove('left','hover');};
/* ================================================================
   v16 PRESENTATION LAYER — sound, animation, menus. Zero rules changes.
   Everything below wraps existing functions; delete this block and the
   game plays identically, just silent and still.
   ================================================================ */

