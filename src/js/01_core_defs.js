const C=7, SLOTS=7;
const CENTER_LANES=[1,3,5];          // the contested center is a mountain pass: monster lanes at 1/3/5, structure slots at 0/2/4/6
const isLane=i=>CENTER_LANES.includes(i);
const BASE_COL=3;                    // the keep sits at back-center (column 3)
function colReach(aCol,tCol){ return Math.abs(aCol-tCol)<=1; } // a unit reaches columns C-1,C,C+1
// center placement: monsters fight in the lanes (1/3/5); structures build on the flanking ground (0/2/4/6)
function centerSlotOK(which,slot,isBld){ return which!=='center' || (isBld ? !isLane(slot) : isLane(slot)); }
let uid=1;
/* ---------- ELEMENTS (7) ----------
   The 7 attributes (Spell & Trap are NOT elements — they stay color:null/neutral).
   Each element carries: display name, kanji glyph, a theme palette (primary color +
   highlight accent + deep shade + 3 radial-gradient bg stops) and its command-center
   identity (starting HP / starting workers). Everything downstream — pools, builds,
   command centers, mana, CSS classes, card registry — is derived from this table. */
const ELEMENTS={
  fire:    {name:'Fire',    glyph:'炎', color:'#e0613f', accent:'#ff8a1f', deep:'#86291c', bg:['#5e1d10','#2a0f08','#080403'], hp:10000, wk:2, lore:'A furnace-keep of slag and iron. Thick walls, single-minded fire.'},
  water:   {name:'Water',   glyph:'水', color:'#3fa3e0', accent:'#7fd0f5', deep:'#0e5a7a', bg:['#0f3a52','#0a2230','#03090f'], hp:10000, wk:3, lore:'A drowned tower humming with current. A fast economy behind thinner walls.'},
  earth:   {name:'Earth',   glyph:'地', color:'#c0863c', accent:'#e5b66a', deep:'#7a5320', bg:['#4a3413','#2a1c0a','#0a0704'], hp:10000, wk:2, lore:'A mountain hollowed into a fortress. Roots in bedrock, walls that have never fallen.'},
  wind:    {name:'Wind',    glyph:'風', color:'#76c7c0', accent:'#cdeeea', deep:'#2f726b', bg:['#123d3a','#0c2422','#04100f'], hp:10000, wk:3, lore:'A wind-scoured crag of open sky and screaming updrafts. Nothing lingers; everything strikes and is gone.'},
  forest:  {name:'Forest',  glyph:'森', color:'#4fae5e', accent:'#a6f0ac', deep:'#27692f', bg:['#173d1d','#0d250f','#041206'], hp:10000, wk:2, lore:'A living rampart of root and bough. Slow to rouse, impossible to clear.'},
  electric:{name:'Electric',glyph:'雷', color:'#f2cf3b', accent:'#fff7a8', deep:'#9a7a16', bg:['#3e3408','#241d05','#0a0802'], hp:10000, wk:3, lore:'A crackling pylon-hold. Everything here moves first and hits like a storm.'},
  light:   {name:'Light',   glyph:'光', color:'#ece3c0', accent:'#ffffff', deep:'#b0a45e', bg:['#3a3622','#221f12','#0a0905'], hp:10000, wk:3, lore:'A gold-vaulted cloister where dawnlight never fails. Patient walls, unyielding grace.'},
  dark:    {name:'Dark',    glyph:'闇', color:'#9a5cc6', accent:'#caa0ec', deep:'#56307a', bg:['#2e1a40','#1a0f26','#080510'], hp:10000, wk:2, lore:'A sunken crypt of whispering dark. Everything here is sharpened, spent, and fed to the void.'},
  /* Divine is NOT a major/deckable element — reserved for Ace / Boss / God cards (e.g. a campaign "God" NPC). */
  divine:  {name:'Divine',  glyph:'神', color:'#c9d4ec', accent:'#ffffff', deep:'#5a6a96', bg:['#2b3450','#171d2e','#070a12'], hp:10000, wk:2, lore:'A vaulted sanctum of judgement-light, beyond the reach of any single banner.'},
};
const MAJORS=['fire','water','earth','wind','forest','electric','light','dark']; // the 8 deckable / commander elements (Divine excluded)
const COLORS=MAJORS.slice();                                                     // colored-mana identities, in canonical order
const clsOf=Object.fromEntries(Object.keys(ELEMENTS).map(k=>[k,k+'-c']));        // element -> creature CSS class (incl. divine, for art)
function zc(){ return Object.fromEntries(COLORS.map(c=>[c,0])); }                // a fresh zeroed colored-mana pool

