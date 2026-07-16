/* Placeholder art. Element backgrounds are derived from each element's palette;
   wood/arc/snare stay neutral for element-agnostic structures, spells and traps.
   Swap for licensed/custom art at release via each template's `art` field, or just
   drop a <slug>_cardart file in assets/cards/ (these are only the fallback). */
const A_BG=(function(){
  const g=s=>'<radialGradient id="bg" cx="50%" cy="38%" r="82%"><stop offset="0" stop-color="'+s[0]+'"/><stop offset=".55" stop-color="'+s[1]+'"/><stop offset="1" stop-color="'+s[2]+'"/></radialGradient>';
  const o={}; for(const k in ELEMENTS) o[k]=g(ELEMENTS[k].bg);
  o.wood=g(['#3a2c14','#241a0a','#0a0704']);   // neutral timber — generic structures
  o.arc =g(['#3a1f5e','#1d1030','#080510']);    // neutral arcane — spells
  o.snare=g(['#3a1410','#1a0a08','#060303']);   // neutral — traps
  return o;
})();
const frame=(bg,inner)=>'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 120 120"><defs>'+A_BG[bg]+'</defs><rect width="120" height="120" fill="url(#bg)"/>'+inner+'</svg>';
const artURI=s=>'data:image/svg+xml,'+encodeURIComponent(s);
/* ---- parametric placeholder art for the elements that have no hand-drawn art ----
   Each draws an element-tinted scene over the element's bg; real art still wins when
   a matching <slug>_cardart file is present. */
function glyphWM(el){ const E=ELEMENTS[el]; return '<text x="60" y="80" font-size="62" text-anchor="middle" fill="'+E.color+'" opacity=".09" font-family="serif" font-weight="700">'+E.glyph+'</text>'; }
function creInner(el,tier){ const E=ELEMENTS[el], body=E.deep, belly=E.color, eye=E.accent; const sc=(0.78+(tier||1)*0.045).toFixed(3);
  let horns=''; if((tier||1)>=3) horns+='<path d="M44 40 l-6 -15 l13 9 z" fill="'+body+'"/><path d="M76 40 l6 -15 l-13 9 z" fill="'+body+'"/>';
  if((tier||1)>=5) horns+='<path d="M60 32 l-5 -16 l10 0 z" fill="'+body+'"/>';
  return '<g transform="translate(60 70) scale('+sc+') translate(-60 -70)">'+horns+
    '<path d="M28 94 q6 -39 32 -43 q26 4 32 43 z" fill="'+body+'" stroke="#0a0a10" stroke-width="1.6"/>'+
    '<path d="M46 84 q14 7 28 0 q-5 13 -14 13 q-9 0 -14 -13 z" fill="'+belly+'" opacity=".9"/>'+
    '<ellipse cx="50" cy="58" rx="5" ry="6" fill="'+eye+'"/><ellipse cx="70" cy="58" rx="5" ry="6" fill="'+eye+'"/>'+
    '<circle cx="50" cy="59" r="2" fill="#0a0406"/><circle cx="70" cy="59" r="2" fill="#0a0406"/></g>'; }
function forgeInner(el){ const E=ELEMENTS[el];
  return '<rect x="40" y="78" width="40" height="14" rx="2" fill="#3a3a44"/>'+
    '<path d="M38 58 h44 l-8 14 h-20 q-11 0 -16 -14z" fill="#8a8a9a"/>'+
    '<rect x="34" y="70" width="52" height="8" rx="2" fill="#5a5a66"/>'+
    '<path d="M60 32 q-8 15 0 24 q8 -11 0 -24z" fill="'+E.color+'"/>'+
    '<path d="M60 38 q-4 10 0 15 q4 -7 0 -15z" fill="'+E.accent+'"/>'; }
function ccTowerInner(c1,c2){ const A=ELEMENTS[c1], B=c2?ELEMENTS[c2]:A;
  return '<rect x="34" y="56" width="52" height="40" fill="'+A.deep+'"/>'+(c2?'<rect x="60" y="56" width="26" height="40" fill="'+B.deep+'"/>':'')+
    '<rect x="30" y="48" width="60" height="10" fill="'+A.color+'"/>'+(c2?'<rect x="60" y="48" width="30" height="10" fill="'+B.color+'"/>':'')+
    '<rect x="34" y="40" width="8" height="10" fill="'+A.color+'"/><rect x="56" y="40" width="8" height="10" fill="'+(c2?B.color:A.color)+'"/><rect x="78" y="40" width="8" height="10" fill="'+(c2?B.color:A.color)+'"/>'+
    '<rect x="54" y="74" width="12" height="22" fill="#160a05"/>'+
    '<circle cx="46" cy="33" r="4" fill="'+A.accent+'"/><circle cx="74" cy="33" r="4" fill="'+(c2?B.accent:A.accent)+'"/>'; }
function phArt(el,kind,tier){ const inner = kind==='bld' ? glyphWM(el)+forgeInner(el) : glyphWM(el)+creInner(el,tier); return artURI(frame(el,inner)); }
function ccArt(colors){ return artURI(frame(colors[0], ccTowerInner(colors[0], colors[1]||null))); }
/* crisp kanji-in-gem element badge (UI) — sized inline */
function elemBadge(el,size){ const E=ELEMENTS[el], s=size||16, g='eb_'+el;
  return '<svg class="elembadge" viewBox="0 0 40 40" width="'+s+'" height="'+s+'" aria-label="'+E.name+'"><defs><radialGradient id="'+g+'" cx="40%" cy="34%" r="78%"><stop offset="0" stop-color="'+E.accent+'"/><stop offset=".55" stop-color="'+E.color+'"/><stop offset="1" stop-color="'+E.deep+'"/></radialGradient></defs>'+
    '<circle cx="20" cy="20" r="18" fill="url(#'+g+')" stroke="rgba(0,0,0,.5)" stroke-width="2"/>'+
    '<text x="20" y="27" font-size="20" text-anchor="middle" fill="#fff" font-family="serif" font-weight="700" style="paint-order:stroke" stroke="rgba(0,0,0,.45)" stroke-width="1">'+E.glyph+'</text></svg>'; }
/* element gem for card frames — kanji gem for colored cards, plain ◇ gem for neutral */
function elemGem(el,size){
  if(el&&ELEMENTS[el]) return elemBadge(el,size);
  const s=size||16;
  return '<svg class="elembadge" viewBox="0 0 40 40" width="'+s+'" height="'+s+'" aria-label="Neutral"><defs><radialGradient id="eb_neutral" cx="40%" cy="34%" r="78%"><stop offset="0" stop-color="#d3cee0"/><stop offset=".55" stop-color="#7b7689"/><stop offset="1" stop-color="#3c3947"/></radialGradient></defs>'+
    '<circle cx="20" cy="20" r="18" fill="url(#eb_neutral)" stroke="rgba(0,0,0,.5)" stroke-width="2"/>'+
    '<text x="20" y="27" font-size="19" text-anchor="middle" fill="#fff" font-family="serif" font-weight="700" style="paint-order:stroke" stroke="rgba(0,0,0,.45)" stroke-width="1">◇</text></svg>';
}
const DRAGON_INNER='<defs><linearGradient id="sc" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#86291c"/><stop offset="1" stop-color="#360e07"/></linearGradient><linearGradient id="hn" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ecd09a"/><stop offset="1" stop-color="#6e4f1d"/></linearGradient><radialGradient id="ey"><stop offset="0" stop-color="#fff6cc"/><stop offset=".42" stop-color="#ffd23f"/><stop offset="1" stop-color="#bf360c"/></radialGradient><radialGradient id="fr"><stop offset="0" stop-color="#fff2ac"/><stop offset=".45" stop-color="#ff8a1f"/><stop offset="1" stop-color="#e0240c"/></radialGradient></defs><path d="M28 120 Q40 82 60 80 Q80 82 92 120 Z" fill="url(#sc)"/><path d="M60 80 l-7 -11 l11 3 z" fill="#8f2c1b"/><path d="M38 60 Q42 36 64 34 Q92 36 96 58 Q92 75 70 77 Q48 75 38 60 Z" fill="url(#sc)" stroke="#170704" stroke-width="1.6"/><path d="M44 64 Q33 70 27 83 Q42 72 51 68 Z" fill="#7c241a"/><path d="M88 51 Q109 49 111 60 Q109 71 85 69 Q83 60 88 51 Z" fill="#6c2016" stroke="#170704" stroke-width="1.2"/><path d="M85 66 Q100 71 109 64 Q104 77 87 75 Z" fill="#4a140c" stroke="#170704" stroke-width="1"/><path d="M91 67 l3 5 l3 -5 z M98 67 l3 5 l3 -5 z M105 66 l2.5 4 l2.5 -4 z" fill="#f4e8c4"/><circle cx="105" cy="57" r="2.1" fill="#170704"/><path d="M52 50 Q66 41 82 50 Q70 49 52 50 Z" fill="#8f2c1b"/><ellipse cx="68" cy="54" rx="7" ry="5.2" fill="url(#ey)"/><ellipse cx="68" cy="54" rx="2" ry="4.6" fill="#220604"/><path d="M50 40 Q39 17 21 11 Q34 26 44 45 Z" fill="url(#hn)" stroke="#352510" stroke-width="1"/><path d="M64 34 Q60 11 47 1 Q58 18 57 36 Z" fill="url(#hn)" stroke="#352510" stroke-width="1"/><path d="M111 61 Q123 57 119 66 Q125 64 119 73 Q113 70 110 66 Z" fill="url(#fr)"/>';
const ART={
 magmaw:artURI(frame('fire',DRAGON_INNER)),
 sparkimp:artURI(frame('fire','<ellipse cx="60" cy="74" rx="22" ry="20" fill="#86291c"/><path d="M44 58 l-6 -16 l14 8 z" fill="#6e1f12"/><path d="M76 58 l6 -16 l-14 8 z" fill="#6e1f12"/><circle cx="52" cy="72" r="4" fill="#ffd23f"/><circle cx="68" cy="72" r="4" fill="#ffd23f"/><circle cx="52" cy="72" r="1.8" fill="#3a0d05"/><circle cx="68" cy="72" r="1.8" fill="#3a0d05"/><path d="M54 82 q6 6 12 0 q-6 7 -12 0z" fill="#3a0d05"/><path d="M60 38 q-5 9 0 13 q5 -5 0 -13z" fill="#ff8a1f"/>')),
 cinderling:artURI(frame('fire','<circle cx="60" cy="66" r="26" fill="#2a0f08"/><path d="M44 60 l10 -4 l4 10 l-9 5z" fill="#ff8a1f"/><path d="M64 54 l12 3 l-2 11 l-11 -2z" fill="#ffd23f"/><path d="M52 74 l10 2 l-1 9 l-10 -1z" fill="#bf360c"/><path d="M70 72 l8 4 l-4 8 l-7 -3z" fill="#ff8a1f"/><circle cx="60" cy="66" r="26" fill="none" stroke="#86291c" stroke-width="2"/>')),
 ashfang:artURI(frame('fire','<path d="M30 50 q30 -16 60 0 q-6 30 -30 34 q-24 -4 -30 -34z" fill="#86291c" stroke="#360e07" stroke-width="2"/><path d="M40 56 l6 12 l6 -12z M54 58 l6 14 l6 -14z M68 56 l6 12 l6 -12z" fill="#ecd09a"/><circle cx="46" cy="50" r="4" fill="#ffd23f"/><circle cx="74" cy="50" r="4" fill="#ffd23f"/><circle cx="46" cy="50" r="1.6" fill="#2a0805"/><circle cx="74" cy="50" r="1.6" fill="#2a0805"/>')),
 pyrewing:artURI(frame('fire','<path d="M58 50 q-30 -6 -42 10 q26 2 42 6z" fill="#bf360c"/><path d="M62 50 q30 -6 42 10 q-26 2 -42 6z" fill="#bf360c"/><path d="M58 50 q-22 0 -34 10 q20 -2 34 2z" fill="#ffd23f"/><path d="M62 50 q22 0 34 10 q-20 -2 -34 2z" fill="#ffd23f"/><path d="M60 40 q-5 24 0 46 q5 -22 0 -46z" fill="#ff8a1f"/><circle cx="60" cy="44" r="6" fill="#ffd23f"/><circle cx="60" cy="44" r="2" fill="#3a0d05"/>')),
 mistling:artURI(frame('water','<path d="M40 60 q0 -22 20 -22 q20 0 20 22 q0 18 -8 24 q-3 -6 -6 0 q-3 -6 -6 0 q-3 -6 -6 0 q-8 -6 -8 -24z" fill="#7fd0f5" opacity=".88"/><circle cx="53" cy="58" r="3.5" fill="#082230"/><circle cx="67" cy="58" r="3.5" fill="#082230"/>')),
 rippler:artURI(frame('water','<circle cx="60" cy="64" r="40" fill="none" stroke="#3fa3e0" stroke-width="1.5" opacity=".3"/><circle cx="60" cy="64" r="30" fill="none" stroke="#3fa3e0" stroke-width="2" opacity=".5"/><circle cx="60" cy="64" r="20" fill="#3fa3e0"/><circle cx="60" cy="64" r="20" fill="none" stroke="#7fd0f5" stroke-width="2"/><circle cx="54" cy="60" r="3" fill="#082230"/><circle cx="68" cy="60" r="3" fill="#082230"/><path d="M54 70 q6 5 12 0" fill="none" stroke="#082230" stroke-width="2"/>')),
 tidecaller:artURI(frame('water','<path d="M44 96 q0 -40 16 -44 q16 4 16 44z" fill="#0e5a7a"/><circle cx="60" cy="44" r="10" fill="#0e5a7a"/><path d="M51 44 q9 -11 18 0 q-9 -3 -18 0z" fill="#06222e"/><circle cx="60" cy="72" r="9" fill="#7fd0f5"/><circle cx="60" cy="72" r="9" fill="none" stroke="#cfeffb" stroke-width="1.5"/><circle cx="60" cy="72" r="3" fill="#cfeffb"/>')),
 surgeling:artURI(frame('water','<path d="M18 88 q12 -42 42 -42 q34 0 42 28 q-16 -16 -32 -8 q10 -14 -2 -22 q-2 18 -14 14 q4 14 -8 18 q-12 4 -28 12z" fill="#3fa3e0"/><path d="M62 50 q18 -2 28 12 q-16 -6 -28 -2z" fill="#cfeffb" opacity=".8"/><circle cx="44" cy="66" r="3.5" fill="#06222e"/><circle cx="56" cy="64" r="3.5" fill="#06222e"/>')),
 leviath:artURI(frame('water','<path d="M30 100 q4 -30 24 -36 q-10 -10 -4 -22 q8 8 14 8 q18 0 24 16 q4 22 -16 28 q-18 4 -42 6z" fill="#0e5a7a" stroke="#03141d" stroke-width="2"/><path d="M60 38 q-6 -14 2 -24 q4 14 8 18z" fill="#3fa3e0"/><ellipse cx="74" cy="58" rx="6" ry="4.5" fill="#7fd0f5"/><ellipse cx="74" cy="58" rx="2" ry="4" fill="#03141d"/><path d="M84 64 q12 -1 16 5 q-9 -1 -16 2z" fill="#0e5a7a"/><path d="M86 67 l3 5 l3 -5z M93 67 l3 5 l3 -5z" fill="#cfeffb"/>')),
 emberforge:artURI(frame('wood','<rect x="44" y="80" width="32" height="14" rx="2" fill="#3a3a44"/><path d="M40 56 h40 l-7 13 h-19 q-10 0 -14 -13z" fill="#8a8a9a"/><rect x="34" y="68" width="52" height="8" rx="2" fill="#5a5a66"/><path d="M60 36 q-7 12 0 20 q7 -8 0 -20z" fill="#ff8a1f"/><path d="M60 42 q-3 8 0 13 q3 -5 0 -13z" fill="#ffd23f"/>')),
 tidewell:artURI(frame('wood','<rect x="44" y="36" width="4" height="24" fill="#6a5a46"/><rect x="72" y="36" width="4" height="24" fill="#6a5a46"/><path d="M38 40 l22 -13 l22 13z" fill="#7a3a2a"/><rect x="40" y="60" width="40" height="30" rx="2" fill="#5a4a3a"/><rect x="37" y="56" width="46" height="8" rx="2" fill="#6a5a46"/><ellipse cx="60" cy="62" rx="18" ry="5" fill="#3fa3e0"/>')),
 longhouse:artURI(frame('wood','<rect x="34" y="58" width="52" height="34" fill="#5a4029"/><path d="M26 58 l34 -24 l34 24z" fill="#7a3a2a"/><rect x="54" y="72" width="12" height="20" fill="#241208"/><rect x="40" y="66" width="9" height="9" fill="#241208"/><rect x="71" y="66" width="9" height="9" fill="#241208"/>')),
 villager:artURI(frame('wood','<path d="M42 96 q0 -32 18 -34 q18 2 18 34z" fill="#6a5238"/><circle cx="60" cy="46" r="11" fill="#caa178"/><path d="M49 45 q11 -13 22 0 q-11 -5 -22 0z" fill="#4a3320"/><rect x="78" y="40" width="4" height="52" rx="2" fill="#8a6a40" transform="rotate(10 80 66)"/><rect x="74" y="38" width="15" height="6" rx="1" fill="#9a9aa6" transform="rotate(10 81 41)"/>')),
 emberbolt:artURI(frame('arc','<path d="M64 24 q-16 22 -6 42 q-10 -2 -13 -13 q-7 17 6 30 q15 13 32 0 q15 -15 4 -36 q-2 9 -11 11 q9 -19 -12 -34z" fill="#ff8a1f"/><path d="M62 52 q-6 13 0 24 q9 -7 6 -17 q6 6 2 15 q11 -9 4 -23z" fill="#ffd23f"/><circle cx="64" cy="68" r="7" fill="#fff2ac"/>')),
 caveIn:artURI(frame('arc','<path d="M30 92 h60 l-6 -16 h-48z" fill="#5a5a66"/><path d="M40 76 h40 l-4 -22 h-32z" fill="#6a6a76"/><circle cx="42" cy="34" r="9" fill="#8a8a9a"/><circle cx="64" cy="25" r="7" fill="#9a9aa6"/><circle cx="81" cy="38" r="8" fill="#7a7a86"/><circle cx="56" cy="44" r="6" fill="#9a9aa6"/><path d="M50 54 l4 12 M67 56 l-3 12" stroke="#2a2a32" stroke-width="2"/>')),
 snarePit:artURI(frame('snare','<ellipse cx="60" cy="48" rx="42" ry="11" fill="#160c06"/><path d="M20 48 q4 38 40 42 q36 -4 40 -42 q-11 27 -40 29 q-29 -2 -40 -29z" fill="#0a0503"/><path d="M38 62 l4 -15 l4 15z M50 66 l4 -17 l4 17z M62 66 l4 -17 l4 17z M74 62 l4 -15 l4 15z" fill="#caa178"/>')),
 frostlance:artURI(frame('arc','<path d="M60 18 l11 30 l-7 50 l-4 9 l-4 -9 l-7 -50z" fill="#7fd0f5"/><path d="M60 18 l11 30 l-11 6 l-11 -6z" fill="#cfeffb"/><path d="M60 52 l8 4 l-8 5 l-8 -5z" fill="#3fa3e0"/><path d="M44 40 l9 11 M76 40 l-9 11" stroke="#cfeffb" stroke-width="2" fill="none"/>')),
 dissolve:artURI(frame('arc','<path d="M40 50 h40 v30 h-40z" fill="#5a4a3a"/><path d="M36 50 h48 l-8 -12 h-32z" fill="#7a3a2a"/><path d="M44 80 q2 11 -2 17 M56 80 q-2 13 2 19 M68 80 q2 11 -2 17" stroke="#3fa3e0" stroke-width="3" fill="none" stroke-linecap="round"/><circle cx="48" cy="64" r="3" fill="#7fd0f5"/><circle cx="62" cy="60" r="2.6" fill="#7fd0f5"/><circle cx="72" cy="66" r="3" fill="#7fd0f5"/>')),
 whirltrap:artURI(frame('snare','<circle cx="60" cy="60" r="34" fill="none" stroke="#0e5a7a" stroke-width="5"/><circle cx="60" cy="60" r="34" fill="none" stroke="#3fa3e0" stroke-width="5" stroke-dasharray="120 130"/><circle cx="60" cy="60" r="22" fill="none" stroke="#3fa3e0" stroke-width="5" stroke-dasharray="80 70" transform="rotate(40 60 60)"/><circle cx="60" cy="60" r="11" fill="none" stroke="#7fd0f5" stroke-width="4" stroke-dasharray="40 30" transform="rotate(90 60 60)"/><circle cx="60" cy="60" r="4" fill="#cfeffb"/>'))
};

