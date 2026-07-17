/* ---------- input ---------- */
function onHand(i){
  if(G.powerMode||G.phase!=='action')return;   // hand plays only in the Action phase
  G.atk=[]; G.moveFrom=null; G.moveMana=null; G.minSel=null;
  if(G.sel&&G.sel.kind==='hand'&&G.sel.idx===i){G.sel=null;G.cardMenu=null;defaultHint();render();return;}
  const card=G.P.you.hand[i];
  G.sel={kind:'hand',idx:i,mode:null};
  const act=(mode,ico,lbl,sub,ok,why)=>`<button class="act${ok?'':' off'}" ${ok?`onclick="chooseMode('${mode}')"`:`disabled title="${why||''}"`}>`+
    `<span class="ico">${ico}</span><span class="lbl">${lbl}</span><span class="sub">${ok?sub:(why||sub)}</span></button>`;
  let html='';
  const can1=manaTotal('you')>=1;   // setting face-down demands ◆1 placed on the card — no free hand-dumping
  if(card.type==='building'){
    const can=canPay('you',card);
    html=act('build','🜂','Build','◆'+card.c,can,'not enough mana')+act('set','⊡','Set','◆1',can1,'needs ◆1');
  } else if(card.type==='spell'){
    if(card.trap){ html=act('settrap','⊡','Set','◆1',can1,'needs ◆1'); }
    else { const okMana=canPay('you',card); const can=okMana&&spellHasTarget(card); const why=!okMana?'not enough mana':'no legal target';
      html=act('cast','✦','Cast','◆'+card.c,can,why); }
  } else {
    const can=canPay('you',card);
    html=act('summon','⬆','Summon','◆'+card.c,can,'not enough mana')+act('set','⊡','Set','◆1',can1,'needs ◆1');
  }
  G.cardMenu={hand:true,i,html};
  setHint(`<b style="color:var(--ink)">${card.nm}</b> — choose an action above the card.`);
  render();
}
window.chooseMode=function(m){
  if(!G.sel||G.sel.kind!=='hand')return;
  G.sel.mode=m; G.cardMenu=null;
  const msg=m==='build'?'Tap an empty slot to build (your rows, or a dark center flank) — or tap one of your cards holding ◆ to raise it on top, spending that stored mana.'
    :m==='summon'?'Tap an empty slot in your rows to summon — or tap one of your cards holding ◆ to play on top, spending that stored mana (the card beneath is lost, surplus ◆ stays).'
    :m==='settrap'?'Tap an empty slot in your rows to set your trap face-down \u2014 \u25c61 is placed on it (it springs on your opponent\u2019s turn).'
    :m==='cast'?'Tap a highlighted enemy target.'
    :'Tap an empty slot in your rows to set it face-down — ◆1 is banked toward its cost.';
  setHint(msg);
  render();
};
// where the SELECTED hand card may be dropped: your back/front rows always; structures may also
// claim the center's flanking slots (matching the commander build menu). Creatures march to the center.
function handDeployOK(key,i){
  if(!(G.sel&&G.sel.kind==='hand'))return false;
  const c=G.P.you.hand[G.sel.idx]; if(!c)return false;
  if(key==='youBack'||key==='youFront')return true;
  return key==='center'&&G.sel.mode==='build'&&!isLane(i)&&placeRowOK('you','center',c);
}
function spellHasTarget(card){
  for(const key of ROWS) for(let i=0;i<SLOTS;i++){ const o=rowArr(key)[i]; if(o&&o.owner==='foe'&&validSpellTarget(card,o)) return true; }
  return false;
}
function validSpellTarget(card,o){
  if(!o)return false;
  if(o.cc)return false; // the command center can only be felled by combat reaching it through the rows
  if(card.effect==='raze') return o.kind==='building';
  if(card.effect==='burn') return o.kind==='creature'||o.kind==='building'||o.kind==='charge';
  if(card.effect==='chain') return o.kind==='creature'&&!o.worker;
  if(card.effect==='bounce') return o.kind==='creature'&&!o.worker;
  return false;
}
function spellText(card){
  if(card.effect==='burn') return `<b>Bolt.</b> Deal <b>${card.val}</b> damage to an enemy creature, structure, or face-down card.`;
  if(card.effect==='raze') return `<b>Sunder.</b> Destroy a target enemy <b>structure</b>.`;
  if(card.effect==='pitfall') return `<b>Snare.</b> When your opponent <b>summons</b> a creature, destroy it.`;
  if(card.effect==='chain') return `<b>Arc.</b> Deal <b>${card.val}</b> to the two highest-attack enemy creatures.`;
  if(card.effect==='bounce') return `<b>Riptide.</b> Return target enemy creature to its owner's hand (Entrench resists).`;
  if(card.effect==='thornmail') return `<b>Overgrowth.</b> When your line is struck, the defending creature gains <b>+500/+1000</b> permanently.`;
  return 'A spell.';
}
function spellRec(card){return {type:'spell',nm:card.nm,c:card.c,trap:!!card.trap,effect:card.effect,val:card.val,ic:card.ic};}
// short keyword label for the small ability box on hand cards (full text lives on the inspect card)
function kwName(c){ switch(c&&c.kw){
  case 'detonate':return 'Detonate '+(c.det||''); case 'undertow':return 'Undertow';
  case 'entrench':return 'Entrench'; case 'ward':return 'Ward'; case 'reap':return 'Reap '+(c.reap||'');
  case 'chrysalis':return 'Chrysalis'; case 'scour':return 'Scour'; case 'overcharge':return 'Overcharge';
} return ''; }
function abilityBrief(card){
  if(card.type==='spell'||card.trap) return spellText(card);
  if(card.type==='building'||card.kind==='building'){ const p=[];
    if(card.eff==='mana')p.push('<b>Forge.</b> ◆'+(card.val||0)+' each turn');
    else if(card.eff==='villager')p.push('<b>Longhouse.</b> Trains a worker each turn');
    else if(card.eff==='damage')p.push('<b>Tower.</b> ⚔'+(card.val||0)+' each turn');
    else if(card.eff==='wall')p.push('<b>Bulwark.</b> Screens the line');
    else if(card.eff==='revive')p.push('<b>Reliquary.</b> Recalls the fallen');
    if(card.sup)p.push('⚒+'+card.sup+' workers');
    return p.join(' · '); }
  const p=[];
  if(card.up)p.push('<b>Upkeep ⚒-'+card.up+'</b>');
  if(card.fs)p.push('<b>First Strike</b>');
  const k=kwName(card); if(k)p.push('<b>'+k+'</b>');
  return p.join(' · ');
}
function clearAtk(){G.atk=[];G.cardMenu=null;G.minSel=null;defaultHint();}

function onCell(key,i,o){
  if(G.phase==='draw'||G.phase==='end')return;   // board is inert during draw / end
  G.cardMenu=null; G.minSel=null;
  const mine=o&&o.owner==='you', foe=o&&o.owner==='foe';
  const which=whichOf(key);
  const deployKey=key==='youBack'||key==='youFront';   // new cards enter only your back + front rows
  if(G.upkeep){ if(mine&&o.kind==='creature'&&!G.moveFrom) upkeepPick(key,i); return; }
  if(G.build){ // placing a structure from the build menu
    if((deployKey||key==='center')&&!o){ placeBuild(which,i); } else { G.build=null; defaultHint(); render(); }
    return;
  }
  if(G.sel&&G.sel.kind==='hand'){
    const sc=G.P.you.hand[G.sel.idx];
    if(G.sel.mode==='cast'){ if(foe&&sc&&validSpellTarget(sc,o)){ castSpell(G.sel.idx,key,i); } else { G.sel=null; defaultHint(); render(); } return; }
    if(G.sel.mode==='settrap'&&!o&&handDeployOK(key,i)){ place(G.sel.idx,'settrap',which,i); return; }
    if((G.sel.mode==='summon'||G.sel.mode==='build')&&mine&&o.bank>0&&deployKey){ place(G.sel.idx,G.sel.mode,which,i); return; }
    if(G.sel.mode&&G.sel.mode!=='cast'&&!o&&handDeployOK(key,i)){ place(G.sel.idx,G.sel.mode,which,i); return; }
    if(G.sel.mode&&G.sel.mode!=='cast'&&!o){   // open slot, but not a legal drop — say why instead of silently deselecting
      setHint(key==='center'
        ? (G.sel.mode==='build'?'Build on the dark flanking slots — the glowing lanes are for marching monsters.':'New cards can’t deploy to the contested center — summon to your rows, then march forward.')
        : 'New cards deploy in your back or front row.');
      render(); return; }
    G.sel=null; defaultHint(); render(); return;
  }
  if(mine&&o.kind==='trap'){ setHint('A set <b>trap</b> — it springs on its own when provoked (a summon or an attack) on your opponent\u2019s turn.');
    if(!FINE_POINTER) inspectRef('you', key==='center'?'center':whichOf(key), i);   // touch: the full trap card is otherwise unreadable
    return; }
  if(mine&&o.kind==='charge'){ if(G.atk.length===0) openCharge(key,i); return; }
  if(mine&&o.kind==='building'){
    const ups=(!o.cc&&acting())?upgradeTargets(o):[];
    const upBtns=ups.map(d=>{ const ok=canUpgradeTo('you',o,key,d);
      return `<button ${ok?`onclick="upgradeStruct('${key}',${i},'${d.bid}')"`:`disabled title="${escHtml(upgradeWhy('you',o,key,d))}"`}>⬆ ${escHtml(d.nm)} ◆${d.c}</button>`; }).join('');
    const send=o.bank>0?`<button onclick="startSendMana('${key}',${i})">◆ Send ${o.bank}</button>`:'';
    if(upBtns||send){ const hint=ups.length?(send?'upgrade this structure, or move its stored ◆':'upgrade this structure to the next tier'):'stored mana — move it, or play a card on top to spend it';
      G.cardMenu={k:key,i,html:`${upBtns}${send}<span class="taphint">${hint}</span>`};
      setHint(ups.length?`<b>${escHtml(o.nm)}</b> — upgrade it${send?', or manage its stored ◆':''}.`:'Structure holding stored ◆ — Send it elsewhere, or play a card on top.'); }
    else { setHint('Structures hold the base — they don\u2019t move or fight.');
      if(!FINE_POINTER) inspectRef('you', key==='center'?'center':whichOf(key), i); }  // menu-less structure: touch still reads it
    render(); return;
  }
  if(mine&&o.kind==='creature'){
    const mv=moveBtn(key,i);
    const send=o.bank>0?`<button onclick="startSendMana('${key}',${i})">◆ Send ${o.bank}</button>`:'';
    const rowOwn=key==='center'?o.owner:(key.startsWith('you')?'you':'foe');
    if(o.sick){ const h=`${mv}${send}`; if(h)G.cardMenu={k:key,i,html:`${h}<span class="taphint">summoning-sick — acts next turn</span>`};
      else if(!FINE_POINTER) inspectRef(rowOwn, key==='center'?'center':whichOf(key), i);   // no menu -> touch still reads the card
      setHint('Summoning-sick — it can act next turn.'); render(); return; }
    if(o.tapped){ const h=`${mv}${send}`; if(h)G.cardMenu={k:key,i,html:`${h}<span class="taphint">tapped until your next turn</span>`};
      else if(!FINE_POINTER) inspectRef(rowOwn, key==='center'?'center':whichOf(key), i);
      setHint('Tapped until your next turn.'); render(); return; }
    // soldier
    const k2=G.atk.findIndex(s=>s.k===key&&s.i===i);
    if(k2>=0){ G.atk.splice(k2,1); }
    else {
      if(G.atk.length&&G.atk[0].k!==key){ setHint('Group attackers must share a row.'); render(); return; }
      G.atk.push({k:key,i});
    }
    if(G.atk.length===1){
      const s=G.atk[0]; const su=rowArr(s.k)[s.i]; const sd=su.bank>0?`<button onclick="startSendMana('${s.k}',${s.i})">◆ Send ${su.bank}</button>`:'';
      G.cardMenu={k:s.k,i:s.i,html:`${moveBtn(s.k,s.i)}${sd}<span class="taphint">⚔ tap any enemy unit, face-down, structure, an open back-row column, or their ♥ life to strike</span>`};
      setHint(`<b>1</b> attacker · ⚔${su.a} — strike any foe or their ♥ life, tap row-mates to join the attack, or use an action above the card.`);
    } else if(G.atk.length){
      setHint(`<b>${G.atk.length}</b> attackers · ⚔${sumA(selCres())} combined — tap a target to strike, or tap a glowing creature to drop it. (Move is solo only.)`);
    } else defaultHint();
    render(); return;
  }
  if(G.atk.length&&canAttack()){
    if(foe){ doAttack(key,i); return; }
    if(!o&&key==='foeBack'){ attackBackRow('foe',i); return; }
  }
  // touch: tapping an enemy card (with no attack group held) reads it — replaces the removed ⓘ button.
  // inspectRef is addressed by the ROW's owner (a foe raider standing in YOUR front row lives in
  // cellArr('you','front')) — the occupant's owner would read the mirrored row's card.
  if(o&&o.owner==='foe'&&!G.atk.length&&!FINE_POINTER){
    render();   // sweep any open card menu before the modal opens over it
    inspectRef(key==='center'?o.owner:(key.startsWith('you')?'you':'foe'), key==='center'?'center':whichOf(key), i);
  }
}

function place(idx,mode,which,slot){
  const card=G.P.you.hand[idx];
  const where=which==='center'?'the contested center':(which==='front'?'front line':'base');
  if(!centerSlotOK(which,slot,card.type==='building')){ setHint(card.type==='building'?'Build on the dark flanking slots — the glowing lanes are for monsters.':'Monsters fight in the glowing lanes (columns 1, 3, 5).'); render(); return; }
  const arr=cellArr('you',which);
  const occ=arr[slot];
  // play face-up ON TOP of one of your cards that holds banked ◆ (summon/build only)
  if(occ){
    if(occ.cc){ setHint("You can't build over your own command center."); return; }
    if((mode!=='summon'&&mode!=='build')||!(occ.bank>0)){ setHint('That slot is taken.'); return; }
    const fromBank=Math.min(occ.bank,card.c);
    const need=card.c-fromBank;
    if(need>manaTotal('you')){ setHint(`Short by ◆${need-manaTotal('you')} — the bank beneath covers ◆${fromBank}.`); return; }
    payAny('you',need);
    const carry=Math.max(0,occ.bank-card.c);     // surplus is stored on the newcomer
    const oldName=occ.nm;
    toGrave('you',occ);                            // old card is destroyed; its summon mana is gone
    G.P.you.hand.splice(idx,1);
    if(mode==='summon'){
      const cr=mkCre(card,'you',false); cr.sick=true; cr.bank=carry; arr[slot]=cr;
      log(`<span class="y">You play ${card.nm} over ${oldName} — ◆${fromBank} from its bank${need>0?` + ◆${need} mana`:''}.${carry?` ◆${carry} stored.`:''}</span>`,'y');
      onCreatureEnter(cr,'you');
      foeTrapOnSummon(cr,which,slot);
    } else {
      const b=mkBld(card,'you'); b.bank=carry; arr[slot]=b;
      log(`<span class="y">You raise ${card.nm} over ${oldName} — ◆${fromBank} from its bank${need>0?` + ◆${need} mana`:''}.${carry?` ◆${carry} stored.`:''}</span>`,'y');
    }
    G.sel=null;defaultHint();afterDeploy('you');render();checkWin();return;
  }
  if(mode==='build'){
    if(card.c>manaTotal('you')){setHint('Not enough mana.');return;}
    payAny('you',card.c); G.P.you.hand.splice(idx,1);
    arr[slot]=mkBld(card,'you');
    log(`<span class="y">You build a ${card.nm} (♥${card.h}) in ${where}.</span>`,'y');
  } else if(mode==='summon'){
    if(manaTotal('you')<card.c){setHint('Not enough mana.');return;}
    if(!canPay('you',card)){setHint(`${card.nm} needs ◆${card.c} — you have ◆${manaTotal('you')}. Harvest more workers during your Upkeep.`);return;}
    payCost('you',card); G.P.you.hand.splice(idx,1);
    const cr=mkCre(card,'you',false); cr.sick=true; arr[slot]=cr;
    log(`<span class="y">You summon ${card.nm} (⚔${card.a}/♥${card.h}) to ${where}.</span>`,'y');
    onCreatureEnter(cr,'you');
    foeTrapOnSummon(cr,which,slot);
  } else if(mode==='settrap'){
    if(manaTotal('you')<1){setHint('Setting a card face-down costs ◆1 — placed on the card.');return;}
    payAny('you',1);
    G.P.you.hand.splice(idx,1);
    arr[slot]={kind:'trap',owner:'you',w:which,card:{nm:card.nm,c:card.c,effect:card.effect,trigger:card.trigger,val:card.val,ic:card.ic,art:card.art,trap:true},setTurn:G.turnNo};
    log(`<span class="y">You set a face-down trap in ${where} (◆1 placed on it).</span>`,'y');
  } else { // set face-down — creature OR building. ◆1 must be placed on it (banked toward its cost).
    if(manaTotal('you')<1){setHint('Setting a card face-down costs ◆1 — it banks toward the card’s cost.');return;}
    payAny('you',1);
    G.P.you.hand.splice(idx,1);
    const ctype=card.type;
    const cdata=ctype==='building'?{nm:card.nm,c:card.c,h:card.h,eff:card.eff,val:card.val,sup:card.sup,ic:card.ic,art:card.art}:{nm:card.nm,a:card.a,h:card.h,c:card.c,fs:card.fs,up:card.up,art:card.art,
      kw:card.kw,det:card.det,ward:card.ward,wardhp:card.wardhp,reap:card.reap,grow:card.grow,hatch:card.hatch,into:card.into,entrench:card.entrench,tribe:card.tribe,subtype:card.subtype};
    arr[slot]={kind:'charge',owner:'you',w:which,ctype,card:cdata,inv:1,setTurn:G.turnNo};
    log(`<span class="y">You set a face-down ${ctype==='building'?'structure':'card'} in ${where} — ◆1 banked on it.</span>`,'y');
  }
  G.sel=null;defaultHint();afterDeploy('you');render();checkWin();
}
