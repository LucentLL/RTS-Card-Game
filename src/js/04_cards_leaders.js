/* ---------- Command Centers ----------
   The commander IS a structure. Your CC choice sets starting HP, starting
   workers (and worker support), and your mana-color identity. It cannot move
   or attack; if it is destroyed, you lose. Generated from ELEMENTS: one solo
   commander per element (id = element id) + one dual "Compact" for every pair
   (id = "elemA_elemB"). 8 solo + 28 dual = 36 commanders. */
const DUAL_LORE=["A steam-forge keep, hissing between anvil and cistern. It boils its own moat to fight.","A kiln-foundry of baked clay walls. Fire sets what the earth has shaped.","A bellows-citadel where wind feeds the furnace. It breathes, and the fire screams.","A char-grove smoldering behind green walls. It burns the forest to fertilize the next.","A plasma-spire crowned in welding light. Heat and current fused past parting.","A solar-furnace of mirrored fire. It does not warm; it consumes by daylight.","An ember-vault banked in cold ash. Its fire burns blackest where no light reaches.","A delta-hold of silt and slow water. It builds new ground from what the river drowns.","A mist-cistern wreathed in cold fog. It hides its walls behind its own breath.","A mangrove-keep half-swallowed by tide. Root and water hold the same line.","A storm-cistern wired to the rain. Every drop it catches it returns as a spark.","A glacier-hall of refracting ice. It bends the dawn and keeps the cold.","A drowned-vault below the black tide. What sinks there is never given back.","A canyon-bastion carved by the gale. The wind shapes the stone and the stone shapes the wind.","A terraced grove of stone and root. The mountain wears the forest like a cloak.","A geode-tower veined with raw current. It stores the storm in solid rock.","A marble-fastness facing the sun. Mountain bones lit white to their core.","A barrow-keep dug into lightless rock. The deep stone keeps its dead unburied.","A storm-canopy of singing leaves. The gale combs the branches and the branches answer in sparks.","A galleon-spire riding the lightning front. It sails on its own thunder.","A lantern-spire of open air and light. Nothing it holds is ever in shadow.","A gale-vault of howling dark corridors. The wind carries off what the dark takes.","A storm-canopy bristling with charged thorns. The forest fruits in lightning.","A bloom-bastion of luminous boughs. It grows toward the light and feeds on it.","A thornwood-vault tangled in night. Its roots drink the dark and the dark drinks back.","A beacon-coil ringed in radiant current. It burns and shines as one act.","A blackout-tower of caged storms. Its lightning never escapes its own walls.","A dusk-bastion balanced on the line of day. It gives light with one hand and takes it with the other."];
const CC_ART={}, CCS={};
(function(){
  for(const el of COLORS){ const E=ELEMENTS[el];
    CCS[el]={ id:el, name:E.name, hp:E.hp, wk:E.wk, colors:[el], desc:E.lore };
    CC_ART[el]=ccArt([el]);
  }
  let n=0;
  for(let i=0;i<COLORS.length;i++) for(let j=i+1;j<COLORS.length;j++){
    const a=COLORS[i], b=COLORS[j], id=a+'_'+b;
    CCS[id]={ id, name:ELEMENTS[a].name+' / '+ELEMENTS[b].name,
      hp:Math.round((ELEMENTS[a].hp+ELEMENTS[b].hp)/2), wk:Math.round((ELEMENTS[a].wk+ELEMENTS[b].wk)/2),
      colors:[a,b], desc:DUAL_LORE[n++]||'Two banners over one keep.' };
    CC_ART[id]=ccArt([a,b]);
  }
})();
function mkCC(def,owner){ return {kind:'building',cc:true,id:uid++,owner,color:def.colors[0],colors:def.colors.slice(),
  nm:def.name,h:def.hp,maxh:def.hp,c:0,eff:'command',val:0,sup:def.wk,ic:'♜',art:CC_ART[def.id],bank:0}; }
function findCC(o){ return null; } // command centers removed — the back row itself is the stronghold (life pool)
const poolFor=color=>POOLS[color]||EMBER;
const cap=s=>s.charAt(0).toUpperCase()+s.slice(1);
/* ===========================================================================
   CARD ART  —  no table to edit. Every card's art file is derived straight
   from its NAME, so you only ever drop in a file and refresh:

       <slug>_cardart.<ext>      in   assets/cards/

   The <slug> is the card name lowercased with spaces/punctuation removed and a
   leading "The " dropped; <ext> may be png, jpg, jpeg or webp (tried in that
   order). Examples:
       Magmaw                ->  magmaw_cardart.png
       Snare Pit             ->  snarepit_cardart.png
       The Tide Spire        ->  tidespire_cardart.png
   If no matching file is found the built-in placeholder drawing shows, so the
   game always runs. (assets/cards/README.md lists every card's exact filename.)
   IMPORTANT: opening this .html directly (file://) can block loading the
   sibling image files — especially on phones — so you may see placeholders.
   To see your art on any device, SERVE the folder instead of opening the raw
   file: e.g.  python3 -m http.server 8000  then open localhost:8000 , or push
   to GitHub and turn on GitHub Pages. (Or run tools/embed-art.py to bake the
   files into the portable build.)
=========================================================================== */
(function(w){
  w.ART_DIR = 'assets/cards/';
  w.ART_EXTS = ['png','jpg','jpeg','webp'];      // tried in order before the placeholder
  w.EMBEDDED = {};                                // slug -> data URI; filled by tools/embed-art.py for the portable build
  w.slugify = function(n){ return String(n||'').toLowerCase().replace(/^the\s+/,'').replace(/[^a-z0-9]+/g,''); };
  /* Art lives in TYPED subfolders (assets/cards/Creatures/<Element>/, Spells/, Traps/, Structures/)
     with the flat assets/cards/ layout kept as a fallback — both work, drop-a-file stays intact.
     The slug->folder table is derived lazily from the card data itself (POOLS/SPELL_NEUTRAL load
     before any art request; STRUCT_DEFS loads in 07 — lazy build sees them all). */
  w.DIR_BY_SLUG = null;
  function dirTable(){
    if(w.DIR_BY_SLUG) return w.DIR_BY_SLUG;
    var t = {}, cap = function(c){ return c ? c.charAt(0).toUpperCase()+c.slice(1) : ''; };
    try{
      COLORS.forEach(function(el){ (POOLS[el]||[]).forEach(function(c){
        t[w.slugify(c.nm)] = c.type==='spell' ? (c.trap?'Traps/':'Spells/')
          : c.type==='building' ? 'Structures/' : 'Creatures/'+cap(c.color||el)+'/'; }); });
      (typeof SPELL_NEUTRAL!=='undefined'?SPELL_NEUTRAL:[]).forEach(function(c){
        t[w.slugify(c.nm)] = c.trap ? 'Traps/' : 'Spells/'; });
      if(typeof STRUCT_DEFS!=='undefined') Object.keys(STRUCT_DEFS).forEach(function(k){
        t[w.slugify(STRUCT_DEFS[k].nm)] = 'Structures/'; });
      // forges aren't in STRUCT_DEFS (built by forgeDef/grandForgeDef) — map them + their Grand tiers
      if(typeof FORGE_NAMES!=='undefined') Object.keys(FORGE_NAMES).forEach(function(el){
        var s=w.slugify(FORGE_NAMES[el]); t[s]='Structures/'; t['grand'+s]='Structures/'; });
    }catch(e){}
    return (w.DIR_BY_SLUG = t);
  }
  w.artDirs = function(n){ var d = dirTable()[w.slugify(n)];
    return d ? [w.ART_DIR+d, w.ART_DIR] : [w.ART_DIR]; };
  // every candidate URL for a card's art, in probe order: typed folder then flat, each x extensions
  w.artURLs = function(n){ var s=w.slugify(n), out=[];
    w.artDirs(n).forEach(function(d){ w.ART_EXTS.forEach(function(x){ out.push(d+s+'_cardart.'+x); }); });
    return out; };
  w.artBase = function(n){ return w.artDirs(n)[0] + w.slugify(n) + '_cardart'; };   // primary dir, no extension
  w.artPath = function(n){ var s=w.slugify(n); return w.EMBEDDED[s] || w.artURLs(n)[0]; };
  function esc(s){ return String(s==null?'':s).replace(/&/g,'&amp;').replace(/"/g,'&quot;').replace(/</g,'&lt;').replace(/>/g,'&gt;'); }
  w.PLACEHOLDERS = w.PLACEHOLDERS || {};
  w.cardArtImg = function(card, extra){
    var nm = card && card.nm ? card.nm : '';
    var cls = 'cardart' + (extra ? ' ' + extra : '');
    return '<img class="'+cls+'" alt="'+esc(nm)+'" data-card="'+esc(nm)+'" data-ext="0" src="'+w.artPath(nm)+'" onerror="artFallback(this)">';
  };
  // on a 404, walk the remaining candidates (typed folder then flat, each x extensions),
  // then fall back to the built-in placeholder. data-ext indexes the artURLs list.
  w.artFallback = function(img){
    var nm = (img.getAttribute && img.getAttribute('data-card')) || '';
    var s = w.slugify(nm);
    if(w.EMBEDDED[s]){ img.onerror = null; img.src = w.EMBEDDED[s]; return; }
    var ei = (parseInt(img.getAttribute('data-ext'),10) || 0) + 1;
    var urls = w.artURLs(nm);
    if(ei < urls.length){ img.setAttribute('data-ext', ei); img.src = urls[ei]; return; }
    img.onerror = null;
    var ph = w.PLACEHOLDERS[nm];
    if(ph) img.src = ph; else img.removeAttribute('src');
  };
  /* ---- floating board SPRITES (Duel Links-style figures that hover above a card) ----
     Drop  assets/sprites/<slug>_sprite.<ext>  (png/webp/jpg) — transparent PNG cut-outs
     look best. Until a sprite file exists, the figure falls back to the card's own art
     so the effect is visible right away, then your dedicated sprite overrides it. */
  w.SPRITE_DIR = 'assets/sprites/';
  w.SPRITE_EXTS = ['png','webp','jpg'];
  w.EMBEDDED_SPRITES = {};                 // slug -> data URI; filled by tools/embed-art.py for the portable build
  w.SPRITES_ON = true;                     // toggled in-game by the 🧍 Figures button
  w.spriteBase = function(n){ return w.SPRITE_DIR + w.slugify(n) + '_sprite'; };
  w.spritePath = function(n){ var s=w.slugify(n); return w.EMBEDDED_SPRITES[s] || (w.spriteBase(n)+'.'+w.SPRITE_EXTS[0]); };
  // ON-FIELD figure art: prefer a "_fieldart" cut-out (assets/cards/<slug>_fieldart.<ext>) for a
  // creature standing on the board; if none exists, borrow the square card art as a framed standee.
  w.FIELD_EXTS = ['png','webp','jpg'];
  w.EMBEDDED_FIELD = {};                       // slug -> data URI (filled by tools/embed-art.py for the portable build)
  w.FIELD_MISS = {};                           // slug -> true once we know there's no field cut-out (skip the 404 on later renders)
  w.fieldURLs = function(n){ var s=w.slugify(n), out=[];   // same typed-then-flat probe order as card art
    w.artDirs(n).forEach(function(d){ w.FIELD_EXTS.forEach(function(x){ out.push(d+s+'_fieldart.'+x); }); });
    return out; };
  w.fieldBase = function(n){ return w.artDirs(n)[0] + w.slugify(n) + '_fieldart'; };
  w.fieldPath = function(n){ var s=w.slugify(n); return w.EMBEDDED_FIELD[s] || w.fieldURLs(n)[0]; };
  w.spriteImg = function(card){
    var nm = card && card.nm ? card.nm : ''; var s = w.slugify(nm);
    if(w.FIELD_MISS[s] && !w.EMBEDDED_FIELD[s])   // known: no field cut-out — borrow the card art straight away (no 404)
      return '<img class="spritefig fromart" alt="'+esc(nm)+'" data-card="'+esc(nm)+'" data-stage="cardart" data-ext="0" src="'+w.artPath(nm)+'" onerror="spriteFallback(this)">';
    return '<img class="spritefig" alt="'+esc(nm)+'" data-card="'+esc(nm)+'" data-stage="field" data-ext="0" src="'+w.fieldPath(nm)+'" onerror="spriteFallback(this)">';
  };
  // 404 chain: remaining _fieldart exts -> the square card art (borrowed, framed) -> built-in placeholder
  w.spriteFallback = function(img){
    var nm = (img.getAttribute && img.getAttribute('data-card')) || '';
    var s = w.slugify(nm);
    var stage = img.getAttribute('data-stage') || 'field';
    var ei = (parseInt(img.getAttribute('data-ext'),10) || 0) + 1;
    if(stage==='field'){
      var furls = w.fieldURLs(nm);
      if(ei < furls.length){ img.setAttribute('data-ext', ei); img.src = furls[ei]; return; }
      w.FIELD_MISS[s] = true;                   // no field cut-out for this card — stop re-requesting it every render
      img.classList.add('fromart'); img.setAttribute('data-stage','cardart'); img.setAttribute('data-ext','0');
      img.src = w.artPath(nm); return;
    }
    // cardart stage: walk the remaining card-art candidates, then the built-in placeholder
    if(w.EMBEDDED[s]){ img.onerror=null; img.src=w.EMBEDDED[s]; return; }
    var urls = w.artURLs(nm);
    if(ei < urls.length){ img.setAttribute('data-ext', ei); img.src = urls[ei]; return; }
    img.onerror = null; var ph = w.PLACEHOLDERS[nm]; if(ph) img.src = ph; else img.removeAttribute('src');
  };
  /* placeholder map auto-synced from the pools above — the always-works safety net */
  var add = function(t){ if(t && t.nm && t.art) w.PLACEHOLDERS[t.nm] = t.art; };
  COLORS.forEach(function(el){ (POOLS[el]||[]).forEach(add); });
  SPELL_NEUTRAL.forEach(add); add(WORKER);
  // every structure (base + upgrade tiers + per-element forges) so placed/upgraded buildings have art
  Object.keys(STRUCT_DEFS).forEach(function(k){ add(STRUCT_DEFS[k]); });
  COLORS.forEach(function(el){ add(forgeDef(el)); add(grandForgeDef(el)); });
  Object.keys(CCS).forEach(function(id){ if(CC_ART[id]) w.PLACEHOLDERS[CCS[id].name] = CC_ART[id]; });
  w.PLACEHOLDERS['Worker'] = ART.villager;
})(window);

/* ===== SLEEVES & FRAMES — optional image skins (assets/sleeves/) ======================
   probeSleeves() tests for user-supplied art with Image() (no fetch, silent when files
   are absent) and flips <html> classes + CSS vars once per session:
     assets/sleeves/cardback.(png|webp)        -> html.sleeve-img        + --sleeve-back-url
     assets/sleeves/frame_<element>.(png|webp) -> html.frame-img-<el>    + --frame-<el>-url
   Cards re-render constantly, so all swapping is CSS-driven — no inline styles on cards.
   The per-element frame rules are injected here (precedent: injectCampaignCSS). */
(function(w){
  var DIR='assets/sleeves/', EXTS=['png','webp'];               // probed in this order
  var FRAME_ELS=Object.keys(ELEMENTS).concat(['neutral']);      // 9 elements + neutral chrome
  function probe(base,ok){                                      // walk extensions; silent on miss
    var i=0;
    (function next(){
      if(i>=EXTS.length) return;
      var url=DIR+base+'.'+EXTS[i++], im=new Image();
      im.onload=function(){ ok(url); };
      im.onerror=next;
      im.src=url;
    })();
  }
  var probedOnce=false;
  w.probeSleeves=function(){                                    // memoized; add files -> reload page
    if(probedOnce) return; probedOnce=true;
    var root=document.documentElement;
    probe('cardback',function(url){
      root.style.setProperty('--sleeve-back-url','url("'+url+'")');
      root.classList.add('sleeve-img');
    });
    FRAME_ELS.forEach(function(el){
      probe('frame_'+el,function(url){
        root.style.setProperty('--frame-'+el+'-url','url("'+url+'")');
        root.classList.add('frame-img-'+el);
      });
    });
  };
  /* frame art draws full-bleed UNDER the card chrome: the procedural body gradient turns
     off, while the name plate / type ribbon / stats bar / --ec rings render on top. */
  var UNDERLAY='linear-gradient(165deg,#1a1a22,#0c0c12)';       // base under transparent frames
  var css=Object.keys(ELEMENTS).map(function(el){
    var sel='html.frame-img-'+el+' .hc.'+el+'-c,html.frame-img-'+el+' .card.'+el+'-c';
    if(el==='fire')  sel+=',html.frame-img-fire .hc.ember-c,html.frame-img-fire .card.ember-c'; // legacy alias
    if(el==='water') sel+=',html.frame-img-water .hc.tide-c,html.frame-img-water .card.tide-c'; // legacy alias
    return sel+'{background:var(--frame-'+el+'-url) center/100% 100% no-repeat,'+UNDERLAY+';}';
  }).join('\n')
  /* neutral = element-agnostic chrome: hand structures (.hcb) + spells (.hcs), placed
     structures (.bld) and worker tokens (.vil). The command center (.ccx) keeps its gold frame. */
  +'\nhtml.frame-img-neutral .hc.hcb,html.frame-img-neutral .hc.hcs,'
  +'html.frame-img-neutral .card.bld:not(.ccx),html.frame-img-neutral .card.vil'
  +'{background:var(--frame-neutral-url) center/100% 100% no-repeat,'+UNDERLAY+';}';
  var s=document.createElement('style'); s.id='sleeveFrameCSS'; s.textContent=css;
  document.head.appendChild(s);
  w.probeSleeves();
})(window);

const G={
  turn:'you',busy:false,over:false,turnNo:1,
  phase:'action', upkeep:false,   // phase: draw → upkeep → action (combat = a sub-phase while attackers are declared) → end
  sel:null, atk:[], decls:[], moveFrom:null, moveMana:null,   // decls = committed attack declarations awaiting Resolve
  center:Array(SLOTS).fill(null),
  P:{
    you:{color:'fire',life:25,mana:0,cmana:zc(),hand:[],deck:[],grave:[],front:Array(SLOTS).fill(null),back:Array(SLOTS).fill(null),min:{back:[],front:[],center:[]},firstExtract:true,villagerUsed:false,cc:'fire',upaid:{back:0,front:0,center:0,raid:0}},
    foe:{color:'water',life:25,mana:0,cmana:zc(),hand:[],deck:[],grave:[],front:Array(SLOTS).fill(null),back:Array(SLOTS).fill(null),min:{back:[],front:[],center:[]},firstExtract:true,villagerUsed:false,cc:'water',upaid:{back:0,front:0,center:0,raid:0}},
  }
};

