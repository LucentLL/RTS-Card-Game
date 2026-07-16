/* ===== BATTLEFIELD SCENERY ==================================================
   buildBattlefield(youEl, foeEl) injects a full-bleed scenery layer as the first
   children of .matmain: element-tinted territories, a scorched contested frontier,
   lane paths, and ~20 seeded props. Idempotent — replaces any previous layer.
   Called from startGame(); nothing else touches .matmain children (renderRow only
   clears the row divs), so it persists across renders/resizes/angle toggles. */
function bfRng(seed){ let t=seed>>>0; return ()=>{ t+=0x6D2B79F5; let r=Math.imul(t^t>>>15,1|t);
  r^=r+Math.imul(r^r>>>7,61|r); return ((r^r>>>14)>>>0)/4294967296; }; }
const BF_ROCK=(a,b)=>`<svg viewBox='0 0 40 26' xmlns='http://www.w3.org/2000/svg'><path d='M4 24 L9 9 L21 4 L33 11 L37 24 Z' fill='${a}' stroke='#0d0a07' stroke-width='1'/><path d='M9 9 L21 4 L25 11 L13 15 Z' fill='${b}' opacity='.85'/></svg>`;
const BF_TUFT=c=>`<svg viewBox='0 0 30 22' xmlns='http://www.w3.org/2000/svg'><g fill='none' stroke='${c}' stroke-width='1.6' stroke-linecap='round'><path d='M15 21q0-9-3-13M15 21q1-8 5-11M15 21q-2-6-7-8M15 21q3-5 8-6'/></g><path d='M15 21q0-9-3-13' fill='none' stroke='#fff' stroke-opacity='.18' stroke-width='.7'/></svg>`;
const BF_BANNER=c=>`<svg viewBox='0 0 30 48' xmlns='http://www.w3.org/2000/svg'><path d='M8 46 L24 4' stroke='#6b543a' stroke-width='2.2' stroke-linecap='round'/><path d='M24 4 L21 10 L27 9 Z' fill='#b8b2a4'/><path d='M22 9 L9 16 L11 22 L14 19 L16 25 L21 21 Z' fill='${c}' stroke='#0d0a07' stroke-width='.8' opacity='.92'/></svg>`;
const BF_BRAZIER=`<svg viewBox='0 0 34 30' xmlns='http://www.w3.org/2000/svg'><path d='M5 12 H29 L26 20 H8 Z' fill='#2c2622' stroke='#0d0a07'/><ellipse cx='17' cy='12' rx='12' ry='3' fill='#141008'/><path d='M10 20 L7 29 M24 20 L27 29 M17 20 V29' stroke='#1a1512' stroke-width='2.5' fill='none'/></svg>`;
const BF_TENT=(a,b)=>`<svg viewBox='0 0 52 34' xmlns='http://www.w3.org/2000/svg'><path d='M3 32 L26 4 L49 32 Z' fill='${a}' stroke='#0d0a07'/><path d='M26 4 L49 32 H36 Z' fill='${b}' opacity='.55'/><path d='M21 32 L26 21 L31 32 Z' fill='#0f0a06'/></svg>`;
const BF_STAKES=`<svg viewBox='0 0 70 22' xmlns='http://www.w3.org/2000/svg'><g fill='#3a2c1a' stroke='#150f08' stroke-width='.8'><path d='M4 22V8l3-4 3 4v14z'/><path d='M16 22V6l3-4 3 4v16z'/><path d='M28 22V9l3-4 3 4v13z'/><path d='M40 22V6l3-4 3 4v16z'/><path d='M52 22V9l3-4 3 4v13z'/></g><path d='M2 13h60' stroke='#241a10' stroke-width='2'/></svg>`;
function buildBattlefield(youEl,foeEl){
  const mm=document.querySelector('.matmain'); if(!mm)return;
  ['battlefield','battlefieldProps'].forEach(id=>{const e=document.getElementById(id); if(e)e.remove();});
  const Y=ELEMENTS[youEl]||ELEMENTS.earth, F=ELEMENTS[foeEl]||ELEMENTS.dark;
  const bf=document.createElement('div'); bf.id='battlefield';
  const st=bf.style;
  st.setProperty('--terr-you',Y.bg[0]);  st.setProperty('--terr-you-deep',Y.bg[1]);  st.setProperty('--terr-you-col',Y.color);
  st.setProperty('--terr-foe',F.bg[0]);  st.setProperty('--terr-foe-deep',F.bg[1]);  st.setProperty('--terr-foe-col',F.color);
  bf.innerHTML='<div class="bf-ground"></div>'
    +[-2,0,2].map(k=>`<div class="bf-path" style="left:calc(50% + ${k}*(var(--cw,60px) + var(--bfgap,6px)))"></div>`).join('')
    +'<div class="bf-center"></div>'
    +'<div class="bf-ember" style="left:31%;top:49%"></div><div class="bf-ember" style="left:73%;top:52%;animation-delay:-1.9s"></div>'
    +'<div class="bf-smoke s1"></div><div class="bf-smoke s2"></div><div class="bf-parts"></div><div class="bf-cloud"></div>';
  const props=document.createElement('div'); props.id='battlefieldProps';
  const rng=bfRng((Math.random()*0xffffffff)>>>0), R=(a,b)=>a+rng()*(b-a);
  const prop=(svg,x,y,w,extra)=>{ const d=document.createElement('div'); d.className='bf-prop up'+(extra?' '+extra:'');
    d.style.left=x.toFixed(1)+'%'; d.style.top=y.toFixed(1)+'%'; d.style.width='calc(var(--cw,60px)*'+w.toFixed(2)+')';
    d.innerHTML=svg; props.appendChild(d); return d; };
  for(let i=0;i<6;i++){ // rocks + tufts in the side margins, outside the 7-column block
    const x=i%2?R(93,98.5):R(1.5,7), y=R(10,90);
    if(rng()<.5) prop(BF_ROCK('#3d3630','#5a5148'),x,y,R(.28,.48));
    else prop(BF_TUFT(y<50?F.deep:Y.deep),x,y,R(.22,.38)); }
  for(let i=0;i<4;i++){ // small tufts along the row seams either side of the frontier
    const y=rng()<.5?R(33,36):R(64,67); prop(BF_TUFT(y<50?F.deep:Y.deep),R(12,88),y,R(.14,.24)); }
  for(let i=0;i<3;i++) // fallen war banners on the contested frontier
    prop(BF_BANNER(i%2?F.color:Y.color),R(12,88),R(46,55),R(.2,.3));
  [[4.5,10,F],[95.5,10,F],[4.5,91,Y],[95.5,91,Y]].forEach(([x,y])=>{ // braziers flanking each back line
    const d=prop(BF_BRAZIER,x,y,.4,'bf-brazier');
    d.insertAdjacentHTML('beforeend','<i class="bf-flame" style="animation-delay:'+(-R(0,.8)).toFixed(2)+'s"></i>'); });
  prop(BF_TENT(F.deep,'#000'),R(14,24),7,.8);    prop(BF_STAKES,R(62,80),6.2,1.05);   // foe camp hints, top edge
  prop(BF_TENT(Y.deep,'#000'),R(74,86),101.5,.85); prop(BF_STAKES,R(18,36),101,1.05); // your camp hints, bottom edge
  const parts=bf.querySelector('.bf-parts');
  for(let i=0;i<8;i++){ // ambient motes: 4 per half, tinted by that side's element accent
    const you=i%2===0, p=document.createElement('i'); p.className='bf-part';
    p.style.setProperty('--pc',you?Y.accent:F.accent);
    p.style.left=R(6,94).toFixed(1)+'%'; p.style.top=(you?R(56,92):R(8,42)).toFixed(1)+'%';
    p.style.setProperty('--pd',R(6,11).toFixed(1)+'s'); p.style.animationDelay=(-R(0,10)).toFixed(1)+'s';
    parts.appendChild(p); }
  mm.insertBefore(props,mm.firstChild); mm.insertBefore(bf,props); // ground first, props second, rows after
  const row=document.getElementById('center'); // align the lane paths to the real column pitch
  if(row){ const g=parseFloat(getComputedStyle(row).columnGap)||6; bf.style.setProperty('--bfgap',g.toFixed(1)+'px'); }
}
