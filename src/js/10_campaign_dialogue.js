/* ===== campaign challenge dialogue (Fire Emblem-style pre-battle scene) ===== */
/* Each element speaks through its flagship creature; portraits reuse the
   spriteImg fieldart→cardart→placeholder chain, so elements without field
   cut-outs yet degrade gracefully. campDialogue() overlays the campaign
   screen, plays a 4-line exchange (defender opens, attacker declares,
   defender retorts, attacker closes), then hands off to onDone. */
const CAMP_CHAMPS={ fire:'Magmaw', water:'Leviath', earth:'Titanore', wind:'Tempest',
  forest:'Hive Cradle', electric:'Galvanwyrm', light:'Seraphine', dark:'Voidwyrm' };

const CAMP_LINES={
  fire:{
    open:["Who dares scorch their boots on my doorstep? Speak fast — the ground here eats the slow.",
      "You smell that? Slag and ash. That's what becomes of banners that march on Fire."],
    capital:["This is the Furnace-Keep itself. Every army that reached these walls is part of the walls now.",
      "You bring an army to the heart of the forge? Good. We were running low on fuel."],
    taunt:["Burn it all down. What's left standing, we keep.",
      "I'll give your line one chance to run. One. It's more than the last ones got."],
    retort:["Then come closer. Everything you love is kindling.",
      "Ha! Stoke the coals. This one thinks it can outlast a furnace."],
    close:["Enough talk. Light the field.",
      "Then it's settled — by fire, as all things are."]
  },
  water:{
    open:["The tide brought you to us. The tide will carry what's left of you away.",
      "Still waters, stranger. Turn back before they remember how to drown."],
    capital:["You stand before the Drowned Tower. Deeper powers than you have broken on this current.",
      "The throne of the deep does not fall. It closes over, and is calm again."],
    taunt:["Every wall erodes. Yours simply erodes today.",
      "We are patient as rain and sudden as the flood. Choose which one meets you."],
    retort:["Come then. The undertow is patient, and you look tired already.",
      "Waves do not argue with stone. They simply return, and return, and return."],
    close:["The current has decided. Let it pull.",
      "Enough. Let the water speak."]
  },
  earth:{
    open:["You are standing on me, little thing. That is as far as you will ever get.",
      "Turn around. The mountain has outlasted better invasions than yours."],
    capital:["This is the Hollow Mountain. Its walls have never fallen. You will not be the first to see them fall.",
      "You march on bedrock. Bedrock does not surrender."],
    taunt:["I do not need to be fast. You will tire, and I will still be here.",
      "Stone remembers every siege. Yours will be a short memory."],
    retort:["Dig in, then. We will see whose roots go deeper.",
      "Strike. The mountain will count your blows and forget them."],
    close:["The earth has spoken. It says: stay down.",
      "Come. Break yourself against me."]
  },
  wind:{
    open:["You're slow. Everything about you is slow. This will be over before your banners unfurl.",
      "The updrafts carried word of your little march. We laughed, mostly."],
    capital:["This crag belongs to the sky. You'd need wings to take it, and I don't see any on you.",
      "The Screaming Crag stands because nothing can catch it. Certainly not you."],
    taunt:["Try to hit me. Go on. I'll wait — no, actually, I won't.",
      "We'll scour your back line before your front line knows we've passed."],
    retort:["Catch the wind, then. Others have tried. Their bones make lovely whistles.",
      "You brought walls to a sky fight. Adorable."],
    close:["Skies darken. Time to fly.",
      "Enough hovering. Strike like a gale."]
  },
  forest:{
    open:["The grove counted your soldiers as they crossed the treeline. The grove is patient. We are patient.",
      "Root and bough remember every axe. Yours will join the mulch."],
    capital:["This is the First Grove. Everything you see grew from it. Everything you see will defend it.",
      "The Cradle wakes. The brood stirs. You should not have come here."],
    taunt:["We grow through everything, given time. Your walls are no different.",
      "The canopy closes over all things. Today it closes over you."],
    retort:["Then the vines will take you slowly, as they take all impatient things.",
      "Hatch, my broodlings. Show them what patience becomes."],
    close:["The forest marches. Root by root.",
      "Grow. Strangle. Bloom."]
  },
  electric:{
    open:["Signal detected. Response time: instant. That's the difference between us, friend.",
      "You walked here? We ARRIVED. Before you finished deciding to come."],
    capital:["This is the Pylon-Hold. Ten thousand volts of no-you-don't. Touch the fence and find out.",
      "The storm's heart doesn't get conquered. It gets survived. Briefly."],
    taunt:["I've already won this fight nine times in my head. Care to see the live version?",
      "First strike, last laugh. That's the whole doctrine."],
    retort:["Cute speech. I overcharged during it. Your move.",
      "Thunder answers lightning. Try to keep up."],
    close:["Storm's rolling in. Let's ride it.",
      "Charge to full. DISCHARGE."]
  },
  light:{
    open:["Dawn finds all who trespass here. Lay down your banner and be forgiven — this once.",
      "The cloister's light does not flicker for armies. Approach, and be seen for what you are."],
    capital:["You stand before the Gold Vault of Dawn. Its light has never failed, and never will.",
      "The dawnlight judges all who reach these gates. Few are found worthy. None by force."],
    taunt:["We come not in anger, but in certainty. The light goes where it will.",
      "Grace has an edge, stranger. You are about to see it drawn."],
    retort:["Then the ward is raised, and the judgement is begun.",
      "Radiance does not yield. It reveals. Stand in it, if you dare."],
    close:["By dawn's mandate — advance.",
      "Let the light fall where it may."]
  },
  dark:{
    open:["Ah. Fresh souls, walking themselves to the crypt. How considerate.",
      "The dark whispered your coming days ago. It also whispered how you end."],
    capital:["This is the Sunken Crypt. Everything that enters feeds it. You will feed it magnificently.",
      "The void keeps its throne the old way: it simply never gives anything back."],
    taunt:["Everything you field, I harvest. Your army is just my army, waiting.",
      "The void is patient and I am not. Lucky for you, only one of us is merciful. Unlucky: it's neither."],
    retort:["Yes... struggle. The reaping is sweeter when the crop resists.",
      "Every soldier you lose joins my line. Do the arithmetic, then despair."],
    close:["The dark is done whispering.",
      "Reap them all."]
  }
};
/* bespoke exchanges for natural rivalries — [attacker taunt, defender retort] */
const CAMP_RIVALS={
  'fire>water':["Steam. That's all your ocean is to me — steam I haven't made yet.",
    "Oceans have swallowed a thousand fires like you. You won't even hiss."],
  'water>fire':["Every forge goes cold, ember. Yours goes cold today.",
    "Come and try, puddle. I've boiled seas for less."],
  'light>dark':["The dark is only the absence of my arrival. I have arrived.",
    "Little candle, the dark was here before you and will be here after. Come — be snuffed."],
  'dark>light':["Every dawn ends, Seraphine. I am what it ends INTO.",
    "The dark has knelt at every sunrise since the first. Kneel again."],
  'earth>wind':["Even the wind must land somewhere, breeze. And everywhere it lands is mine.",
    "Landing? Sweet old rock — why would I ever come down for you?"],
  'wind>earth':["Mountains erode, boulder. I am the thing that erodes them. Grain by grain.",
    "Blow, then. When you tire, the mountain will still be counting."],
  'forest>electric':["Wood does not conduct, storm-worm. But it burns SLOWLY, and grows back faster.",
    "Nature's rebuttal to a tree: lightning. Ask any tall one what it thinks of me."],
  'electric>forest':["Tallest thing on the field gets the bolt, cradle. Guess what you are.",
    "Strike, spark. The grove has drunk a million storms and grown from every one."]
};

let campDlg=null;
function campDialogue(opts){ // {atkEl, defEl, capital, onDone}
  const host=campEl(); if(!host){ opts.onDone(); return; }
  const A=opts.atkEl, D=opts.defEl;
  const an=CAMP_CHAMPS[A]||ELEMENTS[A].name, dn=CAMP_CHAMPS[D]||ELEMENTS[D].name;
  const pick=a=>a[Math.floor(Math.random()*a.length)];
  const rivalAtk=CAMP_RIVALS[A+'>'+D];
  const L=CAMP_LINES;
  const lines=[
    { el:D, nm:dn, side:'def', text:pick(opts.capital?(L[D].capital||L[D].open):L[D].open) },
    { el:A, nm:an, side:'atk', text:rivalAtk?rivalAtk[0]:pick(L[A].taunt) },
    { el:D, nm:dn, side:'def', text:rivalAtk?rivalAtk[1]:pick(L[D].retort) },
    { el:A, nm:an, side:'atk', text:pick(L[A].close) }
  ];
  const box=document.createElement('div'); box.id='campDlg';
  box.innerHTML=
    `<div class="cdg-strip">${elemBadge(A,15)} <b style="color:${ELEMENTS[A].color}">${ELEMENTS[A].name}</b>`+
    ` marches on ${opts.capital?'the <b style="color:var(--gold)">capital</b> of ':''}`+
    `<b style="color:${ELEMENTS[D].color}">${ELEMENTS[D].name}</b> ${elemBadge(D,15)}</div>`+
    `<button class="cdg-skip">Skip ▸▸</button>`+
    `<div class="cdg-glow atk" style="background:radial-gradient(60% 80% at 18% 88%,${ELEMENTS[A].color}33 0%,transparent 70%)"></div>`+
    `<div class="cdg-glow def" style="background:radial-gradient(60% 80% at 82% 88%,${ELEMENTS[D].color}33 0%,transparent 70%)"></div>`+
    `<div class="cdg-fig atk">${spriteImg({nm:an})}</div>`+
    `<div class="cdg-fig def">${spriteImg({nm:dn})}</div>`+
    `<div class="cdg-box"><div class="cdg-name"></div><div class="cdg-text"></div><div class="cdg-more">▼</div></div>`;
  host.appendChild(box);
  const nameEl=box.querySelector('.cdg-name'), textEl=box.querySelector('.cdg-text'),
    moreEl=box.querySelector('.cdg-more'), figA=box.querySelector('.cdg-fig.atk'), figD=box.querySelector('.cdg-fig.def');
  let li=-1, timer=null, done=false;
  function finish(){ if(done)return; done=true; clearInterval(timer);
    if(campDlg===box) campDlg=null; box.remove(); opts.onDone(); }
  function showLine(){ li++;
    if(li>=lines.length){ finish(); return; }
    const ln=lines[li];
    nameEl.innerHTML=`${elemBadge(ln.el,17)} <span style="color:${ELEMENTS[ln.el].color}">${ln.nm}</span>`;
    figA.classList.toggle('speaking',ln.side==='atk');
    figD.classList.toggle('speaking',ln.side==='def');
    moreEl.style.visibility='hidden';
    clearInterval(timer);
    let n=0; textEl.textContent='';
    timer=setInterval(()=>{ n++; textEl.textContent=ln.text.slice(0,n);
      if(n>=ln.text.length){ clearInterval(timer); timer=null; moreEl.style.visibility='visible'; } },14);
  }
  let pdlg=null;
  box.addEventListener('pointerdown',e=>{ e.stopPropagation(); pdlg={x:e.clientX,y:e.clientY,id:e.pointerId}; });
  box.addEventListener('pointerup',e=>{ e.stopPropagation();
    if(done)return;
    if(e.target.closest('.cdg-skip')){ pdlg=null; finish(); return; }   // Skip works regardless of travel
    // a swipe across the overlay must not advance the scene (same thresholds as the globe)
    if(pdlg && e.pointerId===pdlg.id){
      const th=(e.pointerType==='touch')?15:7;
      const moved=Math.abs(e.clientX-pdlg.x)+Math.abs(e.clientY-pdlg.y)>th;
      pdlg=null; if(moved) return;
    }
    if(timer){ clearInterval(timer); timer=null; textEl.textContent=lines[li].text; moreEl.style.visibility='visible'; }
    else showLine(); });
  box.addEventListener('click',e=>e.stopPropagation());
  campDlg=box;
  showLine();
}
(function injectDlgCSS(){ const s=document.createElement('style'); s.textContent=`
#campDlg{position:absolute;inset:0;z-index:43;overflow:hidden;cursor:pointer;
  background:linear-gradient(180deg,rgba(6,4,12,.92) 0%,rgba(10,7,18,.97) 100%);
  animation:cdgin .35s ease;}
@keyframes cdgin{from{opacity:0;}to{opacity:1;}}
.cdg-strip{position:absolute;top:10px;left:50%;transform:translateX(-50%);white-space:nowrap;
  font-family:'Cinzel',serif;font-size:14px;letter-spacing:.06em;color:var(--ink);
  display:flex;align-items:center;gap:6px;background:rgba(12,9,20,.8);border:1px solid rgba(180,160,220,.3);
  border-radius:9px;padding:6px 14px;}
.cdg-skip{position:absolute;top:10px;right:12px;z-index:2;font-family:'Cinzel',serif;font-size:12px;
  color:var(--ink-dim);background:rgba(30,24,44,.85);border:1px solid rgba(180,160,220,.35);
  border-radius:8px;padding:6px 12px;cursor:pointer;}
.cdg-skip:hover{border-color:var(--gold);color:#fff;}
.cdg-glow{position:absolute;inset:0;pointer-events:none;}
.cdg-fig{position:absolute;bottom:24vh;width:34vw;height:52vh;display:flex;align-items:flex-end;justify-content:center;
  filter:brightness(.45) saturate(.7);transition:filter .25s,transform .25s;pointer-events:none;}
.cdg-fig.atk{left:4vw;}
.cdg-fig.def{right:4vw;}
.cdg-fig.def .spritefig{transform:scaleX(-1);}
.cdg-fig.speaking{filter:brightness(1.02) saturate(1) drop-shadow(0 12px 26px rgba(0,0,0,.55));transform:translateY(-6px) scale(1.04);}
.cdg-fig .spritefig{height:100%;width:auto;max-width:100%;max-height:100%;object-fit:contain;object-position:bottom;} /* override the board's cq-unit sizing — no container context here */
/* borrowed square card art must size to its OWN ratio, else a definite height
   letterboxes it and the border frames the empty space above the picture */
.cdg-fig .spritefig.fromart{height:auto;width:auto;max-height:100%;max-width:100%;
  border-radius:12px;border:1px solid rgba(180,160,220,.35);box-shadow:0 14px 40px rgba(0,0,0,.6);}
.cdg-box{position:absolute;left:50%;bottom:3vh;transform:translateX(-50%);width:min(860px,92vw);min-height:17vh;
  background:linear-gradient(180deg,#1c1630f2,#120c1ef7);border:1px solid rgba(180,160,220,.45);border-radius:14px;
  padding:12px 18px 26px;box-shadow:0 18px 50px rgba(0,0,0,.65);}
.cdg-name{font-family:'Cinzel',serif;font-size:16px;letter-spacing:.04em;display:flex;align-items:center;gap:7px;
  margin-bottom:7px;padding-bottom:6px;border-bottom:1px solid rgba(180,160,220,.18);}
.cdg-text{font-family:'EB Garamond',serif;font-size:17px;line-height:1.45;color:var(--ink);min-height:2.9em;}
.cdg-more{position:absolute;right:14px;bottom:8px;color:var(--gold);font-size:13px;animation:cdgbob 1s ease-in-out infinite;}
@keyframes cdgbob{0%,100%{transform:translateY(0);}50%{transform:translateY(3px);}}
@media (max-width:760px){
  .cdg-fig{width:40vw;height:44vh;bottom:26vh;}
  .cdg-fig.atk{left:1vw;} .cdg-fig.def{right:1vw;}
  .cdg-box{bottom:2vh;min-height:20vh;padding-bottom:24px;}
  .cdg-text{font-size:15px;}
  .cdg-strip{font-size:11px;max-width:94vw;white-space:normal;text-align:center;}
}
`; document.head.appendChild(s); })();
/* ===== end campaign dialogue ===== */
