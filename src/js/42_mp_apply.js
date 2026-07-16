/* ---------- 4.5 MPAPPLY: the host re-validates and applies every guest intent as 'foe' ---------- */
const MPAPPLY=(function(){
  let chain=Promise.resolve();
  const foesTurn=()=>MPNET.active&&MP.started&&G.turn==='foe'&&!G.over;
  function bad(q,why){ log(`<span class="e">MP: a guest action was rejected (${why}).</span>`,'e');
    try{MPNET.send({t:'reject',q,why});}catch(e){} }
  function resolveRefs(refs){ return (refs||[]).map(r=>{ if(r.pw!=null)return minPool(r.po,r.pw)[r.pi]||null;
      const a=rowArr(r.k); return (a&&a[r.i])||null; }).filter(Boolean); }

  function harvest(m){ if(!foesTurn()||G.phase!=='upkeep')return bad(m.q,'phase');   // mirrors doHarvest for 'foe'
    { const owe=totalDeficit('foe');                     // creature shortfalls need explicit move/pay/sac intents first;
      if(owe>0){                                         // a purely structural shortfall may be paid here (mirrors doHarvest)
        if(orphanDeficit('foe')<owe||manaTotal('foe')<owe)return bad(m.q,'deficit');
        payAny('foe',owe);
        ZONES.forEach(z=>{ const d=zoneDeficit('foe',z); if(d>0)G.P.foe.upaid[z]=(G.P.foe.upaid[z]||0)+d; });
        log(`<span class="e">The opponent pays ◆${owe} to keep their unsupported works running.</span>`,'e'); } }
    let sum=0;
    for(const z of ['back','front','center']){
      const pool=minPool('foe',z); const up=pool.filter(w=>!w.tapped&&!w.sick).length;
      if(up<=0)continue;
      const total=up*minYield(z);
      pool.forEach(w=>{ if(!w.sick) w.tapped=true; });
      G.P.foe.mana=Math.min(99,G.P.foe.mana+total); sum+=total;
    }
    setPhase('draw');
    log(sum>0?`<span class="e">Enemy harvest: ◆${sum}.</span>`:'<span class="e">— The enemy skips harvest —</span>','e');
    render(); }

  function harvestRowI(m){ if(!foesTurn())return bad(m.q,'phase');                   // mirrors harvestRow L3694–3708
    const which=m.w; if(!['back','front','center'].includes(which))return bad(m.q,'row');
    const ready=minPool('foe',which).filter(x=>!x.tapped&&!x.sick);
    if(!ready.length)return bad(m.q,'workers');
    ready.forEach(x=>x.tapped=true);
    const total=ready.length*minYield(which);
    G.P.foe.mana=Math.min(99,G.P.foe.mana+total);
    log(`<span class="e">Enemy workers harvest ◆${total}.</span>`,'e');
    render(); checkWin(); }

  function sac(m){ if(!foesTurn()||!G.upkeep)return bad(m.q,'phase');                // mirrors upkeepSac L3998–4003
    const a=rowArr(m.k); const o=a&&a[m.i];
    if(!o||o.owner!=='foe')return bad(m.q,'target');
    a[m.i]=null; toGrave('foe',o);
    log(`<span class="e">The opponent sacrifices ${o.nm} to ease their workforce.</span>`,'e');
    syncWorkers('foe'); render(); }

  function pay(m){ if(!foesTurn()||!G.upkeep)return bad(m.q,'phase');                // mirrors upkeepPay for 'foe'
    const a=rowArr(m.k); const o=a&&a[m.i|0];
    if(!o||o.kind!=='creature'||o.owner!=='foe'||o.paid)return bad(m.q,'target');
    const z=zoneForRow('foe',m.k); if(!z)return bad(m.q,'zone');
    const cost=Math.min(o.up||0,zoneDeficit('foe',z));
    if(cost<=0)return bad(m.q,'zone');
    if(manaTotal('foe')<cost)return bad(m.q,'mana');
    payAny('foe',cost); G.P.foe.upaid[z]=(G.P.foe.upaid[z]||0)+cost; o.paid=true;
    log(`<span class="e">The opponent pays ◆${cost} to keep ${o.nm} at its post.</span>`,'e');
    render(); }

  function draw(m){ if(!foesTurn()||G.phase!=='draw')return bad(m.q,'phase');        // mirrors doDraw L3977–3983 (empty deck still advances)
    if(G.P.foe.deck.length){ drawCard('foe'); log('<span class="e">The opponent draws a card.</span>','e'); }
    else log('<span class="e">The opponent\'s deck is empty — nothing to draw.</span>','e');
    setPhase('action'); render(); }

  function move(m){ if(!foesTurn())return bad(m.q,'phase');                          // mirrors doMove with adjacentK('foe',…)
    if(G.phase!=='action'&&G.phase!=='upkeep')return bad(m.q,'phase');
    const a=rowArr(m.fk); const c=a&&a[m.fi];
    if(!c||c.kind!=='creature'||c.owner!=='foe'||moveSpent(c))return bad(m.q,'unit');
    const d=rowArr(m.tk); if(!d)return bad(m.q,'dest');
    if(d[m.ti]||!adjacentK('foe',m.fk,m.fi,m.tk,m.ti))return bad(m.q,'reach');
    a[m.fi]=null;
    if(c.moved){ c.moved2=true; c.tapped=true; } else c.moved=true;   // upkeep second move spends its turn
    d[m.ti]=c;
    log(`<span class="e">${c.nm} repositions to ${rowName(m.tk)}.</span>`,'e');
    syncWorkers('foe'); render(); }

  async function placeI(m){ if(!foesTurn()||G.phase!=='action')return bad(m.q,'phase');   // mirrors place L3364–3418 incl. play-on-top
    const idx=m.idx|0, mode=m.mode, which=m.w, slot=m.i|0;
    const card=G.P.foe.hand[idx];
    if(!card||!['summon','build','set','settrap'].includes(mode))return bad(m.q,'card');
    const whichOK=['back','front'].includes(which)||(which==='center'&&mode==='build');   // structures may claim the center flanks
    if(!whichOK||!(slot>=0&&slot<SLOTS))return bad(m.q,'slot');
    if(!centerSlotOK(which,slot,card.type==='building'))return bad(m.q,'slot');
    const arr=cellArr('foe',which);
    const occ=arr[slot];
    if(occ){                                            // play face-up ON TOP of a banked card (summon/build only)
      if(occ.owner!=='foe')return bad(m.q,'slot');      // the shared center holds BOTH sides' units — never over the host's
      if(occ.cc||((mode!=='summon'&&mode!=='build')||!(occ.bank>0)))return bad(m.q,'slot');
      if(mode==='build'&&card.type!=='building')return bad(m.q,'card');
      if(mode==='summon'&&card.type!=='creature')return bad(m.q,'card');
      const fromBank=Math.min(occ.bank,card.c);
      const need=card.c-fromBank;
      if(need>manaTotal('foe'))return bad(m.q,'mana');
      payAny('foe',need);
      const carry=Math.max(0,occ.bank-card.c);
      const oldName=occ.nm;
      toGrave('foe',occ);
      G.P.foe.hand.splice(idx,1);
      if(mode==='summon'){
        const cr=mkCre(card,'foe',false); cr.sick=true; cr.bank=carry; arr[slot]=cr;
        log(`<span class="e">Opponent plays ${card.nm} over ${oldName}.</span>`,'e');
        onCreatureEnter(cr,'foe'); render();
        MPNET.send({t:'wait',what:'trap'});
        await playerTrapOnSummon(cr,which,slot);        // late-bound → the RESP bar once Step 9 is in (host holds the trap)
      } else {
        const b=mkBld(card,'foe'); b.bank=carry; arr[slot]=b;
        log(`<span class="e">Opponent raises ${card.nm} over ${oldName}.</span>`,'e');
      }
      afterDeploy('foe'); render(); checkWin(); return;
    }
    if(mode==='build'){
      if(card.type!=='building')return bad(m.q,'card');
      if(!placeRowOK('foe',which,card))return bad(m.q,'slot');   // worker-costing structures need a row that stays non-negative
      if(card.c>manaTotal('foe'))return bad(m.q,'mana');
      payAny('foe',card.c); G.P.foe.hand.splice(idx,1);
      arr[slot]=mkBld(card,'foe');
      log(`<span class="e">Opponent builds a ${card.nm} (♥${card.h}).</span>`,'e');
    } else if(mode==='summon'){
      if(card.type!=='creature')return bad(m.q,'card');
      if(manaTotal('foe')<card.c)return bad(m.q,'mana');
      payCost('foe',card); G.P.foe.hand.splice(idx,1);
      const cr=mkCre(card,'foe',false); cr.sick=true; arr[slot]=cr;
      log(`<span class="e">Opponent summons ${card.nm} (⚔${card.a}/♥${card.h}).</span>`,'e');
      onCreatureEnter(cr,'foe'); render();
      MPNET.send({t:'wait',what:'trap'});
      await playerTrapOnSummon(cr,which,slot);
    } else if(mode==='settrap'){
      if(card.type!=='spell'||!card.trap)return bad(m.q,'card');
      if(manaTotal('foe')<1)return bad(m.q,'mana');     // setting costs ◆1 placed on the card
      payAny('foe',1);
      G.P.foe.hand.splice(idx,1);
      arr[slot]={kind:'trap',owner:'foe',w:which,card:{nm:card.nm,c:card.c,effect:card.effect,trigger:card.trigger,val:card.val,ic:card.ic,art:card.art,trap:true},setTurn:G.turnNo};
      log('<span class="e">Opponent sets a face-down card (◆1 placed on it).</span>','e');
    } else {                                            // 'set' — face-down charge (creature OR building); ◆1 banks toward its cost
      if(card.type!=='creature'&&card.type!=='building')return bad(m.q,'card');
      if(manaTotal('foe')<1)return bad(m.q,'mana');
      payAny('foe',1);
      G.P.foe.hand.splice(idx,1);
      const ctype=card.type;
      const cdata=ctype==='building'?{nm:card.nm,c:card.c,h:card.h,eff:card.eff,val:card.val,sup:card.sup,ic:card.ic,art:card.art}:{nm:card.nm,a:card.a,h:card.h,c:card.c,fs:card.fs,up:card.up,art:card.art,
        kw:card.kw,det:card.det,ward:card.ward,wardhp:card.wardhp,reap:card.reap,grow:card.grow,hatch:card.hatch,into:card.into,entrench:card.entrench,tribe:card.tribe,subtype:card.subtype};
      arr[slot]={kind:'charge',owner:'foe',w:which,ctype,card:cdata,inv:1,setTurn:G.turnNo};
      log('<span class="e">Opponent sets a face-down card (◆1 banked on it).</span>','e');
    }
    afterDeploy('foe'); render(); checkWin(); }

  function pour(m){ if(!foesTurn()||G.phase!=='action')return bad(m.q,'phase');      // mirrors camtPour L3526 on a foe-owned charge
    const a=rowArr(m.k); const ch=a&&a[m.i];
    if(!ch||ch.kind!=='charge'||ch.owner!=='foe')return bad(m.q,'target');
    const p=Math.min(m.amt|0,manaTotal('foe'));
    if(p<=0)return bad(m.q,'mana');
    payAny('foe',p); ch.inv+=p;
    log(`<span class="e">Opponent pours ◆${p} into a face-down card (◆${ch.inv}/${ch.card.c}).</span>`,'e');
    render(); }

  function flipI(m){ if(!foesTurn()||G.phase!=='action')return bad(m.q,'phase');     // mirrors camtFlip L3527
    const a=rowArr(m.k); const ch=a&&a[m.i];
    if(!ch||ch.kind!=='charge'||ch.owner!=='foe'||ch.inv<ch.card.c)return bad(m.q,'target');
    flip('foe',m.k,m.i);
    render(); checkWin(); }

  function sendMana(m){ if(!foesTurn())return bad(m.q,'phase');                      // mirrors doSendMana L3493–3499
    const src=rowArr(m.fk)&&rowArr(m.fk)[m.fi]; const dst=rowArr(m.tk)&&rowArr(m.tk)[m.ti];
    if(!src||!dst||(m.fk===m.tk&&m.fi===m.ti)||src.owner!=='foe'||dst.owner!=='foe'||!(src.bank>0)||!(dst.kind==='creature'||dst.kind==='building'))return bad(m.q,'target');
    const amt=src.bank||0; dst.bank=(dst.bank||0)+amt; src.bank=0;
    log(`<span class="e">Opponent moves ◆${amt} of stored mana.</span>`,'e');
    render(); }

  function cast(m){ if(!foesTurn()||G.phase!=='action')return bad(m.q,'phase');      // mirrors castSpell L3445–3451 via resolveSpell
    const card=G.P.foe.hand[m.idx|0];
    if(!card||card.type!=='spell'||card.trap)return bad(m.q,'card');
    if(manaTotal('foe')<card.c)return bad(m.q,'mana');
    const tgt=rowArr(m.k)&&rowArr(m.k)[m.i];
    if(!tgt||tgt.owner!=='you'||!validSpellTarget(card,tgt))return bad(m.q,'target');
    if(!resolveSpell(card,m.k,m.i))return bad(m.q,'target');
    payCost('foe',card); G.P.foe.hand.splice(m.idx|0,1); G.P.foe.grave.push(spellRec(card));
    render(); checkWin(); }

  function build(m){ if(!foesTurn()||G.phase!=='action')return bad(m.q,'phase');     // mirrors placeBuild L2162–2168, bid re-validated against the HOST build DB
    const def=resolveStruct(m.bid,m.color||null);
    if(!def)return bad(m.q,'bid');
    const which=m.w, i=m.i|0;
    if(!['back','front','center'].includes(which)||!(i>=0&&i<SLOTS))return bad(m.q,'slot');
    if(which==='center'&&isLane(i))return bad(m.q,'slot');
    const arr=cellArr('foe',which);
    if(arr[i]||!placeRowOK('foe',which,def))return bad(m.q,'slot');
    if(!canBuild('foe',def))return bad(m.q,'build');
    payAny('foe',def.c); arr[i]=mkBld(def,'foe');
    log(`<span class="e">Opponent raises a ${escHtml(def.nm)}.</span>`,'e');
    afterDeploy('foe'); render(); checkWin(); }

  function upgrade(m){ if(!foesTurn()||G.phase!=='action')return bad(m.q,'phase');   // mirrors upgradeStruct for 'foe'
    const a=rowArr(m.k); const o=a&&a[m.i|0];
    if(!o||o.kind!=='building'||o.owner!=='foe'||o.cc)return bad(m.q,'target');
    const def=upgradeTargets(o).find(d=>d.bid===m.bid);
    if(!def)return bad(m.q,'bid');
    if(!canUpgradeTo('foe',o,m.k,def))return bad(m.q,'illegal');
    const old=o.nm; payAny('foe',def.c); applyUpgrade(o,def);
    log(`<span class="e">Opponent upgrades ${escHtml(old)} into a ${escHtml(def.nm)}.</span>`,'e');
    syncWorkers('foe'); afterDeploy('foe'); render(); checkWin(); }

  async function attack(m){                                        // NEW GLUE — guest attacks; host defends
    if(!foesTurn()||G.phase!=='action')return bad(m.q,'phase');
    if(!Array.isArray(m.atk)||!m.atk.length||m.atk.some(s=>s.k!==m.atk[0].k))return bad(m.q,'row'); // group shares a row (onCell L3346)
    const attackers=m.atk.map(s=>rowArr(s.k)&&rowArr(s.k)[s.i])
      .filter(x=>x&&x.kind==='creature'&&x.owner==='foe'&&!x.worker&&!x.sick&&!x.tapped);
    if(attackers.length!==m.atk.length)return bad(m.q,'attackers');
    const aKey=m.atk[0].k, aIdx=rowIdx(aKey), aCol=m.atk[0].i;
    // resolve target + validate BEFORE tapping anything
    let tIdx, tgt=null;
    if(m.kind==='back'){ if(!(m.col>=0&&m.col<SLOTS))return bad(m.q,'col');   // a life ('back') strike can target any column, occupied or not (the cmdzone ♥ path bypasses the wall) — mirrors attackBackRow
      tIdx=rowIdx('youBack'); }
    else if(m.kind==='workers'){ if(!minPool('you',m.wWhich).length)return bad(m.q,'pool');
      tIdx=rowIdx(zoneKey('you',m.wWhich)); }
    else { tgt=rowArr(m.tk)&&rowArr(m.tk)[m.ti];
      if(!tgt||tgt.owner!=='you')return bad(m.q,'target');
      tIdx=rowIdx(m.tk); }
    // from here on: mirrors doAttack L3788–3816 / attackBackRow L3819–3843 / attackMinionStack L3719–3738 with sides swapped
    attackers.forEach(a=>a.tapped=true);
    const scour=groupIsScour(attackers);
    dischargeOvercharge(attackers);
    // impact FX for the guest (it already played its own lunge locally)
    MP.fx({ev:'impact', k:(m.kind==='back'?'youBack':(m.kind==='workers'?zoneKey('you',m.wWhich):m.tk)),
           i:(m.kind==='back'?m.col:(m.kind==='workers'?0:m.ti)), el:attackers[0].color||null, well:m.kind==='workers'});
    let chosen=[];
    const canBlock=(m.kind==='workers') ? Math.abs(aIdx-tIdx)>1 : (!scour&&Math.abs(aIdx-tIdx)>1);
    if(canBlock){
      const elig=eligibleInterceptors('foe',aIdx,tIdx,aCol);       // attacker-owner arg = 'foe' (recon §2)
      if(elig.length){
        MPNET.send({t:'wait',what:'block'});
        chosen=await askBlock({attacker:attackers[0],elig,title:'Incoming Attack',
          desc:`${attackers[0].nm} (⚔${attackers[0].a}/♥${attackers[0].h})${attackers.length>1?' +'+(attackers.length-1)+' more':''} strikes from ${rowName(aKey)}.`});
      }
    }
    if(chosen.length){
      const defs=chosen.map(r=>r.c||unitAt(r.key,r.i)).filter(Boolean);
      defs.forEach(d=>{d.tapped=true;d.blocked=true;});
      log(`<span class="y">You interpose ${defs.length}!</span>`,'y');
      resolveCombat(attackers,defs);
      clearDischarge(attackers); render(); checkWin(); return;
    }
    if(m.kind==='back'){
      const dmg=sumA(attackers);                                   // same formula as attackBackRow L3837
      G.P.you.life=Math.max(0,G.P.you.life-dmg);
      log(`<span class="e">The enemy breaches your line — ⚔${dmg} strikes your stronghold! (♥${G.P.you.life} remains)</span>`,'e');
      if(scour&&attackers[0]){ scourStrike(attackers[0],'you'); cleanup(); }
    } else if(m.kind==='workers'){
      log(`<span class="e">The enemy strikes your Minions with ${attackers.length} creature(s).</span>`,'e');
      resolveCombat(attackers,minPool('you',m.wWhich).slice());
    } else {
      if(tgt.kind==='creature'||tgt.kind==='building') springAttackTrap('you',attackers,tgt);   // your attack-trigger trap (L3657)
      if(tgt.kind==='charge'){ provokeFaceDown('you',m.tk,m.ti,attackers); }
      else if(tgt.kind==='trap'){ springTrap('you',m.tk,m.ti,attackers); }
      else if(tgt.kind==='building'){ log(`<span class="e">The enemy strikes your ${tgt.nm}.</span>`,'e');
        clashFx(attackers,[tgt]); applyDmg(focusFire(attackers,[tgt])); cleanup(); }
      else if(tgt.kind==='creature'){ log(`<span class="e">The enemy attacks your ${tgt.nm}.</span>`,'e');
        resolveCombat(attackers,[tgt]); }
      if(scour&&attackers[0]){ scourStrike(attackers[0],'you'); cleanup(); }
    }
    clearDischarge(attackers);
    render(); checkWin(); }

  function end(m){ if(!foesTurn()||G.phase!=='action')return bad(m.q,'phase');
    G.atk=[]; G.sel=null;
    setPhase('end'); endPhaseEffects('foe');
    startTurn('you');                                              // 'you' branch: Upkeep + hint + FX ribbon (L3962, L4831)
    render(); }

  function dispatch(m){ chain=chain.then(async()=>{ switch(m.a){
      case 'harvest':harvest(m);break;    case 'harvestRow':harvestRowI(m);break;
      case 'sac':sac(m);break;            case 'pay':pay(m);break;
      case 'draw':draw(m);break;
      case 'move':move(m);break;          case 'place':await placeI(m);break;
      case 'pour':pour(m);break;          case 'flip':flipI(m);break;
      case 'sendmana':sendMana(m);break;  case 'cast':cast(m);break;
      case 'build':build(m);break;        case 'upgrade':upgrade(m);break;
      case 'attack':await attack(m);break;
      case 'end':end(m);break;            default:bad(m.q,'unknown');
    } MP.pushNow(); }).catch(e=>{ console.warn('MP apply',e); MP.pushNow(); }); }
  return {dispatch,resolveRefs};
})();

