function startGame(youId,foeId,youDeck,foeDeck){
  const cy=CCS[youId], cf=CCS[foeId];
  Object.assign(G.P.you,{color:cy.colors[0],cc:youId,life:cy.hp,mana:0,cmana:zc(),hand:[],deck:[],grave:[],front:Array(SLOTS).fill(null),back:Array(SLOTS).fill(null),min:{back:[],front:[],center:[]},firstExtract:true,villagerUsed:false,upaid:{back:0,front:0,center:0,raid:0}});
  Object.assign(G.P.foe,{color:cf.colors[0],cc:foeId,life:cf.hp,mana:0,cmana:zc(),hand:[],deck:[],grave:[],front:Array(SLOTS).fill(null),back:Array(SLOTS).fill(null),min:{back:[],front:[],center:[]},firstExtract:true,villagerUsed:false,upaid:{back:0,front:0,center:0,raid:0}});
  G.turn='you';G.busy=false;G.over=false;G.turnNo=1;G.sel=null;G.atk=[];G.moveFrom=null;G.moveMana=null;G.cardMenu=null;G.phase='upkeep';G.upkeep=true;
  G.center=Array(SLOTS).fill(null);
  // no command-center card: the WHOLE back row is each player's stronghold. Life is the standalone
  // pool an undefended strike into the back row drains; the element still sets colors + base workers.
  syncWorkers('you'); syncWorkers('foe');
  readyWorkers('you'); readyWorkers('foe'); // the opening workforce is settled and ready to harvest on turn 1
  G.P.you.deck=youDeck||deckOf(cy.colors); G.P.foe.deck=foeDeck||deckOf(cf.colors);
  dealOpening('you'); dealOpening('foe');
  applyCharacterUI();
  buildBattlefield(cy.colors[0], cf.colors[0]);
  hideAllScreens();
  $('log').innerHTML='';
  log(`<span class="y">${cy.name} stands against ${cf.name}${youId===foeId?' (a mirror!)':''}. Break through to the enemy back row and drain their ♥ to win — protect your own.</span>`);
  setPhase('upkeep'); upkeepHint(); render();   // turn 1 opens at Upkeep — ⛏ Harvest, then Draw
}
let selYou='fire', selFoe='water', csStep=1;
function csCard(c,col,on){
  return `<button class="cschar ${on?'on':''}" data-col="${col}" data-id="${c.id}">`+
    `<div class="cn" style="color:var(--${c.colors[0]})">${ccPips(c)}${c.name}</div>`+
    `<div class="cp">${c.desc}</div>`+
    `<div class="cpow">♥ <b>${c.hp}</b> health &nbsp;·&nbsp; ⚒ <b>${c.wk}</b> workers &nbsp;·&nbsp; ${c.colors.map(cap).join(' + ')} mana</div></button>`;
}
function renderCharSel(){
  const ids=Object.keys(CCS); const box=$('charsel').querySelector('.csbox');
  if(csStep===1){
    const cards=ids.map(id=>csCard(CCS[id],'you',false)).join('');
    box.innerHTML=`<h1>Choose Your Command Center</h1><div class="cssub">Step 1 of 2 — your keep sets health and workers; its element is a synergy attribute. Lose it and you lose.</div><div class="csrow">${cards}</div>`;
    box.querySelectorAll('.cschar').forEach(b=>b.addEventListener('click',()=>{ selYou=b.dataset.id; csStep=2; renderCharSel(); }));
  } else {
    const cy=CCS[selYou];
    const cards=ids.map(id=>csCard(CCS[id],'foe',false)).join('')+
      `<button class="cschar" data-col="foe" data-id="__rand"><div class="cn">🎲 Random</div><div class="cp">A surprise opponent, rolled when the duel begins.</div></button>`;
    box.innerHTML=`<h1>Choose Your Opponent</h1>`+
      `<div class="cssub">Step 2 of 2 — same or different. You: <b style="color:var(--${cy.colors[0]})">${cy.name}</b>.</div>`+
      `<div class="csrow">${cards}</div>`+
      `<button class="csback" id="csBack">← back</button>`;
    box.querySelectorAll('.cschar').forEach(b=>b.addEventListener('click',()=>{
      let foeId=b.dataset.id; if(foeId==='__rand')foeId=ids[Math.floor(Math.random()*ids.length)];
      startGame(selYou,foeId);
    }));
    $('csBack').addEventListener('click',()=>{ csStep=1; renderCharSel(); });
  }
}
function showCharSelect(){ csStep=1; renderCharSel(); $('charsel').style.display='flex'; }

