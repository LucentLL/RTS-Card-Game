/* ---------- FX: overlay engine — flights, arrows, popups, ribbons, splashes ---------- */
const FX=(()=>{
  const RM=(window.matchMedia&&matchMedia('(prefers-reduced-motion: reduce)').matches)||false;
  const layer=document.createElement('div'); layer.id='fxLayer';
  const svg=document.createElementNS('http://www.w3.org/2000/svg','svg'); layer.appendChild(svg);
  const vig=document.createElement('div'); vig.id='hurtVig';
  const rib=document.createElement('div'); rib.id='turnRibbon'; rib.innerHTML='<span></span>';
  const spl=document.createElement('div'); spl.id='splashFx';
  spl.innerHTML='<div class="spbox"><img alt=""><div class="spname"></div><div class="spstats"></div></div>';
  document.body.append(layer,vig,rib,spl);
  function svgSize(){ svg.setAttribute('width',innerWidth); svg.setAttribute('height',innerHeight); }
  addEventListener('resize',svgSize); svgSize();
  const mk=(cls,html)=>{ const d=document.createElement('div'); d.className=cls; if(html!=null)d.innerHTML=html; layer.appendChild(d); return d; };
  const center=r=>({x:r.left+r.width/2, y:r.top+r.height/2});
  function flyRect(fr,tr,html,ms=300){ if(RM||!fr||!tr)return;
    const d=mk('fx-fly',html);
    d.style.cssText+=`left:${fr.left}px;top:${fr.top}px;width:${fr.width}px;height:${fr.height}px;transition:transform ${ms}ms cubic-bezier(.5,-.2,.6,1.15),opacity ${ms}ms;`;
    requestAnimationFrame(()=>{ d.style.transform=`translate(${tr.left-fr.left+(tr.width-fr.width)/2}px,${tr.top-fr.top+(tr.height-fr.height)/2}px) scale(.88)`; d.style.opacity='.15'; });
    setTimeout(()=>d.remove(),ms+80); }
  function pop(r,text,cls='dmg'){ if(!r)return; const c=center(r); const d=mk('fx-pop '+cls,text);
    d.style.left=c.x+'px'; d.style.top=(c.y-6)+'px'; setTimeout(()=>d.remove(),980); }
  function slash(r){ if(RM||!r)return; const c=center(r); const d=mk('fx-slash');
    d.style.left=(c.x-36)+'px'; d.style.top=(c.y-36)+'px'; setTimeout(()=>d.remove(),380); }
  function ring(r){ if(RM||!r)return; const c=center(r); const d=mk('fx-ring');
    d.style.left=(c.x-31)+'px'; d.style.top=(c.y-31)+'px'; setTimeout(()=>d.remove(),640); }
  function burstRect(r,color='#ffd98a',n=10){ if(RM||!r)return; const c=center(r);
    for(let i=0;i<n;i++){ const d=mk('fx-spark'); const a=Math.random()*Math.PI*2, v=22+Math.random()*40;
      d.style.left=c.x+'px'; d.style.top=c.y+'px'; d.style.background=color; d.style.color=color;
      d.style.setProperty('--dx',Math.cos(a)*v+'px'); d.style.setProperty('--dy',Math.sin(a)*v+'px');
      setTimeout(()=>d.remove(),680); } }
  function shake(){ if(RM)return; const m=document.querySelector('.mat'); if(!m)return;
    m.classList.remove('fx-shake'); void m.offsetWidth; m.classList.add('fx-shake'); }
  function hurt(){ vig.classList.remove('on'); void vig.offsetWidth; vig.classList.add('on'); }
  function ribbon(text,color){ rib.firstChild.textContent=text; rib.style.setProperty('--rc',color||'var(--gold)');
    rib.classList.remove('on'); void rib.offsetWidth; rib.classList.add('on'); }
  function arcPath(fr,tr){ const a=center(fr), b=center(tr);
    return `M ${a.x} ${a.y} Q ${(a.x+b.x)/2} ${Math.min(a.y,b.y)-44} ${b.x} ${b.y}`; }
  function arrow(fr,tr){ if(RM||!fr||!tr)return; const p=document.createElementNS('http://www.w3.org/2000/svg','path');
    p.setAttribute('d',arcPath(fr,tr)); p.setAttribute('class','fx-arrow'); svg.appendChild(p);
    setTimeout(()=>p.remove(),440); }
  let aimP=null;
  function aimArrow(fr,tr){ clearAim(); if(!fr||!tr)return; aimP=document.createElementNS('http://www.w3.org/2000/svg','path');
    aimP.setAttribute('d',arcPath(fr,tr)); aimP.setAttribute('class','fx-arrow aim'); svg.appendChild(aimP); }
  function clearAim(){ if(aimP){aimP.remove();aimP=null;} }
  let splT=null;
  function splash(unit,owner){ if(RM||!unit)return;
    (function(_i){_i.onerror=function(){_i.onerror=null;_i.src=(PLACEHOLDERS[unit.nm]||'');};_i.src=artPath(unit.nm);})(spl.querySelector('img'));
    spl.querySelector('.spname').textContent=unit.nm;
    spl.querySelector('.spstats').textContent=unit.kind==='building'?`STRUCTURE · ♥${unit.h}`:`⚔${unit.a} / ♥${unit.h}${unit.fs?' · FIRST STRIKE':''}`;
    spl.style.setProperty('--sc', owner==='you'?'var(--gold)':'var(--tide)');
    spl.classList.remove('on'); void spl.offsetWidth; spl.classList.add('on');
    clearTimeout(splT); splT=setTimeout(()=>spl.classList.remove('on'),1100); }
  function confetti(){ if(RM)return; for(let i=0;i<26;i++){ const d=mk('fx-conf');
    d.style.left=(8+Math.random()*84)+'vw'; d.style.background=['#d9b04a','#e0613f','#5fc46a','#52c6ec'][i%4];
    d.style.animationDelay=(Math.random()*.6)+'s'; setTimeout(()=>d.remove(),3100); } }
  // a soft coloured glow puff at a cell — deploy shimmer, impact flash, block parry
  function flash(r,color='#fff7e0',size=88){ if(RM||!r)return; const c=center(r); const d=mk('fx-flash');
    d.style.left=(c.x-size/2)+'px'; d.style.top=(c.y-size/2)+'px'; d.style.width=d.style.height=size+'px';
    d.style.background=`radial-gradient(circle,${color} 0%,${color}88 34%,transparent 70%)`; setTimeout(()=>d.remove(),440); }
  // a fading afterimage streak left along a move path
  function trail(fr,tr,color){ if(RM||!fr||!tr)return; const a=center(fr),b=center(tr);
    const steps=5; for(let i=1;i<=steps;i++){ const t=i/(steps+1); const d=mk('fx-trail');
      const w=fr.width*.6,h=fr.height*.6; d.style.left=(a.x+(b.x-a.x)*t-w/2)+'px'; d.style.top=(a.y+(b.y-a.y)*t-h/2)+'px';
      d.style.width=w+'px'; d.style.height=h+'px'; if(color)d.style.setProperty('--tc',color); d.style.animationDelay=(i*22)+'ms';
      setTimeout(()=>d.remove(),560+i*22); } }
  return {flyRect,pop,slash,ring,burstRect,shake,hurt,ribbon,arrow,aimArrow,clearAim,splash,confetti,flash,trail};
})();

/* ---------- ELEMFX: elemental impact FX — element-tinted bursts + attack projectiles ---------- */
const ELEMFX=(()=>{
  const RM=(window.matchMedia&&matchMedia('(prefers-reduced-motion: reduce)').matches)||false;
  const layer=document.getElementById('fxLayer'); const svg=layer&&layer.querySelector('svg');
  const NS='http://www.w3.org/2000/svg';
  const ctr=r=>({x:r.left+r.width/2,y:r.top+r.height/2});
  const col=k=>(ELEMENTS[k]&&ELEMENTS[k].color)||'#ffd98a';
  const acc=k=>(ELEMENTS[k]&&ELEMENTS[k].accent)||'#fff3c4';
  const rnd=(a,b)=>a+Math.random()*(b-a);
  // one particle: a tiny inline-SVG shape ridden by the generic .efx-p motion classes
  function part(x,y,html,vars,cls='',life=700){
    if(!layer)return;
    const d=document.createElement('div'); d.className='efx-p'+(cls?' '+cls:''); d.innerHTML=html;
    d.style.left=x+'px'; d.style.top=y+'px';
    for(const k in vars)d.style.setProperty(k,vars[k]);
    layer.appendChild(d); setTimeout(()=>d.remove(),life);
  }
  function ray(x,y,w,color,rot,dur){
    if(!layer)return;
    const d=document.createElement('div'); d.className='efx-ray';
    d.style.left=x+'px'; d.style.top=y+'px'; d.style.width=w+'px'; d.style.color=color;
    d.style.setProperty('--rot',rot+'deg'); d.style.setProperty('--dur',dur+'s');
    layer.appendChild(d); setTimeout(()=>d.remove(),dur*1000+140);
  }
  // a jagged lightning path in the shared fx svg
  function bolt(x0,y0,x1,y1,c){
    if(!svg)return;
    const p=document.createElementNS(NS,'path'); let d=`M ${x0} ${y0}`;
    for(let i=1;i<5;i++){ const t=i/5; d+=` L ${(x0+(x1-x0)*t+rnd(-9,9)).toFixed(1)} ${(y0+(y1-y0)*t+rnd(-6,6)).toFixed(1)}`; }
    d+=` L ${x1} ${y1}`;
    p.setAttribute('d',d); p.setAttribute('class','efx-bolt'); if(c)p.style.stroke=c;
    svg.appendChild(p); setTimeout(()=>p.remove(),340);
  }
  function whip(cx,cy){ // forest thorn-lash
    if(!svg)return;
    const p=document.createElementNS(NS,'path');
    p.setAttribute('d',`M ${cx-38} ${cy+26} Q ${cx-14} ${cy-34} ${cx+30} ${cy-6} Q ${cx+40} ${cy+2} ${cx+34} ${cy+12}`);
    p.setAttribute('class','efx-whip'); svg.appendChild(p); setTimeout(()=>p.remove(),420);
  }
  /* per-element particle shapes — each well under 300 bytes */
  const S={
    tear:(c,a)=>`<svg width="9" height="13" viewBox="0 0 9 13"><path d="M4.5 0C6.4 4.6 9 6.2 9 9a4.5 4 0 1 1-9 0C0 6.2 2.6 4.6 4.5 0Z" fill="${c}"/><path d="M4.5 3.4C5.6 6 7 7 7 9a2.5 2.2 0 1 1-5 0c0-2 1.4-3 2.5-5.6Z" fill="${a}"/></svg>`,
    plume:(c,a)=>`<svg width="26" height="38" viewBox="0 0 26 38"><path d="M13 0C21 12 26 18 26 27a13 11 0 1 1-26 0C0 18 5 12 13 0Z" fill="${c}"/><path d="M13 9c5 8 8 12 8 18a8 7 0 1 1-16 0c0-6 3-10 8-18Z" fill="${a}"/></svg>`,
    drop:c=>`<svg width="7" height="10" viewBox="0 0 7 10"><path d="M3.5 0C5 3.4 7 4.8 7 6.8a3.5 3.2 0 1 1-7 0C0 4.8 2 3.4 3.5 0Z" fill="${c}"/></svg>`,
    puffEl:c=>`<svg width="58" height="16" viewBox="0 0 58 16"><ellipse cx="29" cy="8" rx="28" ry="7" fill="${c}" opacity=".55"/></svg>`,
    shard:c=>`<svg width="10" height="10" viewBox="0 0 10 10"><polygon points="5,0 10,4 7,10 1,8 0,3" fill="${c}"/><polygon points="5,2 8,4.5 6.4,8 2.6,6.6" fill="#0006"/></svg>`,
    cres:c=>`<svg width="26" height="26" viewBox="0 0 26 26"><path d="M2 13A11 11 0 0 1 24 8 9 9 0 1 0 22 20 11 11 0 0 1 2 13Z" fill="${c}"/></svg>`,
    streak:c=>`<svg width="16" height="3" viewBox="0 0 16 3"><rect width="16" height="3" rx="1.5" fill="${c}"/></svg>`,
    leaf:c=>`<svg width="10" height="12" viewBox="0 0 10 12"><path d="M5 0C9 3 10 8 5 12 0 8 1 3 5 0Z" fill="${c}"/><path d="M5 1.6V10.6" stroke="#0007" stroke-width=".8"/></svg>`,
    bit:c=>`<svg width="5" height="5" viewBox="0 0 5 5"><rect width="5" height="5" fill="${c}"/></svg>`,
    mote:c=>`<svg width="6" height="6" viewBox="0 0 6 6"><circle cx="3" cy="3" r="3" fill="${c}"/></svg>`,
  };
  /* the signature impact at a defender cell — tinted core + ≤12 particles + one signature piece */
  function elemBurst(rect,el,big){
    if(RM||!rect)return;
    if(!el||!ELEMENTS[el]){ FX.burstRect(rect,'#ffd98a',big?14:10); return; }   // neutral fallback
    const c=ctr(rect), m=big?1.5:1, K=col(el), A=acc(el);
    if(el!=='divine')FX.flash(rect,K,big?130:88);                               // tinted core flash
    switch(el){
      case 'fire':      // teardrop flames rise + a central plume
        for(let i=0;i<9;i++) part(c.x+rnd(-16,16)*m,c.y+rnd(-4,8),S.tear(K,A),
          {'--dx':rnd(-14,14)+'px','--dy':(-rnd(28,66)*m)+'px','--rot':rnd(-40,40)+'deg','--dur':rnd(.5,.72)+'s'},'',860);
        part(c.x-13*m,c.y-10,S.plume(K,A),{'--dy':(-30*m)+'px','--dur':'.5s','--psc':'1.25'},'',640);
        break;
      case 'water':     // droplets arc out then FALL + splash ellipse
        for(let i=0;i<10;i++){ const dx=rnd(14,40)*m*(i%2?1:-1);
          part(c.x-3,c.y-4,S.drop(K),{'--dx':dx+'px','--dy':(-rnd(18,42)*m)+'px','--fall':(rnd(46,70)*m)+'px',
            '--rot':rnd(-30,30)+'deg','--dur':rnd(.55,.75)+'s'},'grav',900); }
        part(c.x-29,c.y+rect.height*.28,S.puffEl(A),{'--dy':'-4px','--dur':'.45s','--psc':'1.6'},'',580);
        break;
      case 'earth':     // tumbling shards under gravity + dust puff
        for(let i=0;i<9;i++){ const dx=rnd(12,38)*m*(i%2?1:-1);
          part(c.x-5,c.y-5,S.shard(K),{'--dx':dx+'px','--dy':(-rnd(14,40)*m)+'px','--fall':(rnd(50,78)*m)+'px',
            '--rot':rnd(-260,260)+'deg','--dur':rnd(.55,.8)+'s'},'grav',940); }
        FX.flash(rect,'#8a6b42',big?150:110);
        break;
      case 'wind':      // spinning crescents + thin streaks
        for(let i=0;i<3;i++){ const a2=rnd(0,Math.PI*2);
          part(c.x-13,c.y-13,S.cres(A),{'--dx':Math.cos(a2)*46*m+'px','--dy':Math.sin(a2)*30*m+'px',
            '--rot':(300+i*40)+'deg','--dur':'.5s','--psc':'.7'},'',620); }
        for(let i=0;i<8;i++){ const a2=rnd(0,Math.PI*2),v=rnd(30,58)*m;
          part(c.x-8,c.y-1,S.streak(K),{'--dx':Math.cos(a2)*v+'px','--dy':Math.sin(a2)*v*.5+'px',
            '--rot':(a2*57.3).toFixed(0)+'deg','--dur':'.36s'},'',480); }
        break;
      case 'forest':    // sway-fall leaves + thorn whip
        for(let i=0;i<9;i++) part(c.x+rnd(-20,20),c.y+rnd(-14,0),S.leaf(i%3?K:A),
          {'--dx':rnd(-30,30)*m+'px','--dy':rnd(30,56)*m+'px','--rot':rnd(40,90)+'deg','--dur':rnd(.7,.95)+'s'},'sway',1080);
        whip(c.x,c.y);
        break;
      case 'electric':  // bolt STRIKES DOWN onto the cell + square sparks
        bolt(c.x+rnd(-14,14),c.y-(big?110:78),c.x,c.y,K);
        for(let i=0;i<10;i++){ const a2=rnd(0,Math.PI*2),v=rnd(20,52)*m;
          part(c.x-2,c.y-2,S.bit(i%2?K:A),{'--dx':Math.cos(a2)*v+'px','--dy':Math.sin(a2)*v+'px',
            '--rot':rnd(-90,90)+'deg','--dur':'.32s'},'',440); }
        break;
      case 'light':     // ray starburst + rising motes
        for(let i=0;i<8;i++)ray(c.x,c.y,46*m,i%2?A:K,i*45+rnd(-8,8),.5);
        for(let i=0;i<5;i++) part(c.x+rnd(-18,18),c.y+rnd(-6,6),S.mote(A),
          {'--dx':rnd(-8,8)+'px','--dy':(-rnd(26,48)*m)+'px','--dur':rnd(.7,.9)+'s'},'',1020);
        break;
      case 'dark':      // IMPLOSION — motes converge, then a violet void ring
        for(let i=0;i<10;i++){ const a2=(i/10)*Math.PI*2,v=rnd(30,54)*m;
          part(c.x-3,c.y-3,S.mote(i%2?K:'#3a2050'),{'--dx':Math.cos(a2)*v+'px','--dy':Math.sin(a2)*v+'px',
            '--rot':'160deg','--dur':'.42s'},'in',560); }
        setTimeout(()=>{ if(!layer)return; const g=document.createElement('div'); g.className='fx-ring';
          g.style.left=(c.x-31)+'px'; g.style.top=(c.y-31)+'px'; g.style.borderColor=K;
          g.style.boxShadow=`0 0 18px ${K}88, inset 0 0 10px ${K}55`;
          layer.appendChild(g); setTimeout(()=>g.remove(),640); },170);
        break;
      case 'divine':    // oversized white flood + gold/white rays
        FX.flash(rect,'#ffffff',big?170:150);
        for(let i=0;i<10;i++)ray(c.x,c.y,(big?86:68),i%2?'#ffffff':'#ffd98a',i*36+rnd(-6,6),.6);
        for(let i=0;i<4;i++) part(c.x+rnd(-16,16),c.y,S.mote('#fff'),
          {'--dx':rnd(-10,10)+'px','--dy':-rnd(30,56)+'px','--dur':'.85s'},'',980);
        break;
    }
  }
  /* element-tinted comet attacker→defender (electric: instant jagged bolt instead) */
  function elemShot(fromRect,toRect,el,ms=260){
    if(RM||!fromRect||!toRect||!layer)return;
    if(el&&!ELEMENTS[el])el=null;
    const a=ctr(fromRect),b=ctr(toRect);
    if(el==='electric'){ bolt(a.x,a.y,b.x,b.y,col('electric')); return; }
    const K=el?col(el):'#ffd98a', A=el?acc(el):'#fff3c4';
    const ang=(Math.atan2(b.y-a.y,b.x-a.x)*180/Math.PI).toFixed(1);
    const d=document.createElement('div'); d.className='efx-shot';
    d.innerHTML=`<svg width="34" height="12" viewBox="0 0 34 12"><path d="M34 6 8 11C3 11 0 8.8 0 6s3-5 8-5Z" fill="${K}" opacity=".5"/><circle cx="27" cy="6" r="5" fill="${K}"/><circle cx="28.5" cy="6" r="2.6" fill="${A}"/></svg>`;
    d.style.left=(a.x-27)+'px'; d.style.top=(a.y-6)+'px';
    d.style.transform=`rotate(${ang}deg)`;
    d.style.transition=`transform ${ms}ms cubic-bezier(.45,0,.7,1),opacity ${ms}ms`;
    layer.appendChild(d);
    requestAnimationFrame(()=>{ d.style.transform=`translate(${b.x-a.x}px,${b.y-a.y}px) rotate(${ang}deg)`; d.style.opacity='.25'; });
    setTimeout(()=>d.remove(),ms+80);
    [.35,.65].forEach(t=>setTimeout(()=>part(a.x+(b.x-a.x)*t,a.y+(b.y-a.y)*t,S.mote(K),
      {'--dx':rnd(-6,6)+'px','--dy':rnd(-10,4)+'px','--dur':'.4s'},'',520),ms*t));      // 2 trail embers
  }
  /* trap snap — dark/red jaws converge + red ring */
  function trapSnap(rect){
    if(RM||!rect||!layer)return;
    const c=ctr(rect);
    FX.flash(rect,'#e35b4f',80);
    for(let i=0;i<10;i++){ const a2=(i/10)*Math.PI*2,v=rnd(28,48);
      part(c.x-3,c.y-3,S.mote(i%2?'#9a5cc6':'#e35b4f'),{'--dx':Math.cos(a2)*v+'px','--dy':Math.sin(a2)*v+'px',
        '--rot':'120deg','--dur':'.38s'},'in',520); }
    setTimeout(()=>{ const g=document.createElement('div'); g.className='fx-ring';
      g.style.left=(c.x-31)+'px'; g.style.top=(c.y-31)+'px'; g.style.borderColor='#e35b4f';
      g.style.boxShadow='0 0 18px rgba(227,91,79,.75), inset 0 0 10px rgba(227,91,79,.4)';
      layer.appendChild(g); setTimeout(()=>g.remove(),640); },140);
  }
  return {elemBurst,elemShot,trapSnap};
})();

