/* ===== campaign hexsphere globe: geometry + canvas renderer ===== */
/* A Goldberg polyhedron GP(f,0) — subdivided icosahedron dual: 10f²+2 tiles
   (12 pentagons, rest hexagons). Geometry is deterministic for a given f, so
   saves store only tile→territory assignments and rebuild the sphere on load.
   Renderer: orthographic canvas projection, painter-sorted extruded tiles,
   drag-to-rotate (inertia + idle spin), tap-to-pick via inverse rotation. */
const CAMP_FREQ=4;
let CAMP_SPHERES={};
function getSphere(f){
  if(CAMP_SPHERES[f]) return CAMP_SPHERES[f];
  const PHI=(1+Math.sqrt(5))/2;
  const IV=[[-1,PHI,0],[1,PHI,0],[-1,-PHI,0],[1,-PHI,0],[0,-1,PHI],[0,1,PHI],[0,-1,-PHI],[0,1,-PHI],[PHI,0,-1],[PHI,0,1],[-PHI,0,-1],[-PHI,0,1]];
  const IF=[[0,11,5],[0,5,1],[0,1,7],[0,7,10],[0,10,11],[1,5,9],[5,11,4],[11,10,2],[10,7,6],[7,1,8],[3,9,4],[3,4,2],[3,2,6],[3,6,8],[3,8,9],[4,9,5],[2,4,11],[6,2,10],[8,6,7],[9,8,1]];
  const norm=v=>{const l=Math.hypot(v[0],v[1],v[2]);return [v[0]/l,v[1]/l,v[2]/l];};
  const verts=[], vkey=new Map();
  function addV(v){ v=norm(v); const k=v.map(x=>x.toFixed(6)).join(','); let i=vkey.get(k); if(i==null){ i=verts.length; verts.push(v); vkey.set(k,i);} return i; }
  const tris=[];
  for(const [a,b,c] of IF){
    const A=norm(IV[a]),B=norm(IV[b]),C=norm(IV[c]); const grid=[];
    for(let i=0;i<=f;i++){ grid.push([]); for(let j=0;j<=i;j++){
      const p=[0,1,2].map(k=> A[k]+(B[k]-A[k])*(i/f)+(C[k]-B[k])*(i? (j/f):0) );
      grid[i].push(addV(p)); } }
    for(let i=1;i<=f;i++) for(let j=0;j<i;j++){
      tris.push([grid[i-1][j],grid[i][j],grid[i][j+1]]);
      if(j<i-1) tris.push([grid[i-1][j],grid[i][j+1],grid[i-1][j+1]]);
    }
  }
  const corners=tris.map(t=>norm([0,1,2].map(k=>(verts[t[0]][k]+verts[t[1]][k]+verts[t[2]][k])/3)));
  const inc=verts.map(()=>[]);
  tris.forEach((t,ti)=>{ for(const v of t) inc[v].push(ti); });
  const adjSet=verts.map(()=>new Set());
  tris.forEach(t=>{ adjSet[t[0]].add(t[1]).add(t[2]); adjSet[t[1]].add(t[0]).add(t[2]); adjSet[t[2]].add(t[0]).add(t[1]); });
  const cross=(a,b)=>[a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
  const dot=(a,b)=>a[0]*b[0]+a[1]*b[1]+a[2]*b[2];
  const tiles=verts.map((c,vi)=>{
    // order incident tri centroids CCW (from outside) around the vertex normal
    let u=Math.abs(c[0])<0.9?[1,0,0]:[0,1,0];
    u=norm(cross(c,u)); const v=cross(c,u);
    const ord=inc[vi].slice().sort((ta,tb)=>{
      const pa=corners[ta],pb=corners[tb];
      return Math.atan2(dot(pa,v),dot(pa,u))-Math.atan2(dot(pb,v),dot(pb,u));
    });
    return { c, corners:ord, adj:[...adjSet[vi]] };
  });
  return (CAMP_SPHERES[f]={ tiles, corners });
}

/* persistent view so End Turn / map rebuilds don't lose the player's angle */
let campView=null, campGlobeRAF=0, campGlobeDrawNow=null; // DrawNow: manual tick for headless/DOM-eval verification (rAF stalls in non-composited panes)
let campGlobeCleanup=null, campGlobeGen=0;
/* Stop the render loop and release the mount. MUST be called whenever the map
   leaves the screen: hideAllScreens only sets display:none, and a hidden
   element is still isConnected — without this the loop repaints the sphere at
   60fps through every battle and every other screen. */
function campGlobeStop(){ campGlobeGen++; cancelAnimationFrame(campGlobeRAF); campGlobeRAF=0; campGlobeDrawNow=null;
  if(campGlobeCleanup){ campGlobeCleanup(); campGlobeCleanup=null; } }
function campGlobeAimAt(c){ // yaw/pitch that bring unit vector c to face the viewer
  const yaw=Math.atan2(-c[0],c[2]);
  const z1=-c[0]*Math.sin(yaw)+c[2]*Math.cos(yaw);
  return { yaw, pitch:Math.atan2(c[1],z1), vyaw:0 };
}
function campGlobeResetView(){ campView=null; }

function campGlobeMount(canvas, M, faction, onPick){
  campGlobeStop();   // reap the superseded mount deterministically (listener, loop, detached canvas + its 2D context)
  const gen=++campGlobeGen;
  const sphere=getSphere(M.f||CAMP_FREQ);
  const T=sphere.tiles, CR=sphere.corners;
  if(!campView) campView=campGlobeAimAt(T[M.terr[M.capitals[faction]].anchor].c);
  const V=campView;
  const H=0.05, INSET=0.93, EXH=1+H;
  const ctx=canvas.getContext('2d');
  let R=100, CX=0, CY=0, DPR=1, W=0, HT=0;
  function fit(){ const box=canvas.parentElement; if(!box)return;
    const r=box.getBoundingClientRect(); W=Math.max(80,r.width); HT=Math.max(80,r.height);
    DPR=Math.min(2,window.devicePixelRatio||1);
    canvas.width=W*DPR; canvas.height=HT*DPR; canvas.style.width=W+'px'; canvas.style.height=HT+'px';
    R=Math.min(W,HT)*0.42; CX=W/2; CY=HT/2; }
  fit();
  const onRes=()=>{ if(!canvas.isConnected){ window.removeEventListener('resize',onRes); return; } fit(); };
  window.addEventListener('resize',onRes);
  campGlobeCleanup=()=>{ window.removeEventListener('resize',onRes); };
  const rot=v=>{ const cy=Math.cos(V.yaw), sy=Math.sin(V.yaw), cx=Math.cos(V.pitch), sx=Math.sin(V.pitch);
    const x=v[0]*cy+v[2]*sy, z=-v[0]*sy+v[2]*cy, y=v[1];
    return [x, y*cx-z*sx, y*sx+z*cx]; };
  const unrot=v=>{ const cy=Math.cos(-V.yaw), sy=Math.sin(-V.yaw), cx=Math.cos(-V.pitch), sx=Math.sin(-V.pitch);
    const y=v[1]*cx-v[2]*sx, z=v[1]*sx+v[2]*cx;
    return [v[0]*cy+z*sy, y, -v[0]*sy+z*cy]; };
  const P=v=>[CX+v[0]*R, CY-v[1]*R];
  const shadeCache={};
  function shade(hex,m){ const key=hex+'|'+(m=Math.round(m*40)/40); if(shadeCache[key])return shadeCache[key];
    const c=parseInt(hex.slice(1),16); const r=Math.min(255,((c>>16)&255)*m)|0, g=Math.min(255,((c>>8)&255)*m)|0, b=Math.min(255,(c&255)*m)|0;
    return (shadeCache[key]=`rgb(${r},${g},${b})`); }
  const LI=(()=>{ const l=[-0.45,0.55,0.72]; const n=Math.hypot(l[0],l[1],l[2]); return l.map(x=>x/n); })();
  function attackable(tid){ const t=M.terr[tid]; if(t.owner===faction)return false; return t.adj.some(u=>M.terr[u].owner===faction); }
  let lastInteract=0, dragging=false;
  let fitTick=0;
  function draw(ts){
    if(gen!==campGlobeGen) return;                                   // superseded by a newer mount
    if(!canvas.isConnected || !canvas.getClientRects().length){       // detached OR display:none — stop, don't spin
      window.removeEventListener('resize',onRes); campGlobeRAF=0; return; }
    if(++fitTick>=20){ fitTick=0; const b=canvas.parentElement&&canvas.parentElement.getBoundingClientRect();
      if(b&&b.width>1&&(Math.abs(b.width-W)>1||Math.abs(b.height-HT)>1)) fit(); } // self-heal after a layout settle (never on a 0x0 hidden parent)
    ctx.setTransform(DPR,0,0,DPR,0,0);
    ctx.clearRect(0,0,W,HT);
    const g=ctx.createRadialGradient(CX,CY,R*0.88,CX,CY,R*1.2);
    g.addColorStop(0,'rgba(96,118,210,0.16)'); g.addColorStop(1,'rgba(96,118,210,0)');
    ctx.fillStyle=g; ctx.beginPath(); ctx.arc(CX,CY,R*1.2,0,7); ctx.fill();
    ctx.fillStyle='#0a1424'; ctx.beginPath(); ctx.arc(CX,CY,R*1.001,0,7); ctx.fill();
    // rotation is linear, so rotating each corner ONCE per frame also gives the
    // extruded (xEXH) and inset points — no per-corner rot() in the tile loop
    const rcen=T.map(t=>rot(t.c)), rcorn=CR.map(c=>rot(c));
    const order=T.map((_,i)=>i).sort((a,b)=>rcen[a][2]-rcen[b][2]);
    for(const i of order){
      const t=T[i], z=rcen[i][2];
      // cull on the CORNERS, not the centre: a centre-only test still draws
      // far-side tiles whose corners reach ~0.87R, and they show through the
      // 7% inset seams of the front tiles painted over them
      if(z<-0.35 || t.corners.every(ci=>rcorn[ci][2]<0)) continue;
      const own=M.terr[M.tileTerr[i]].owner;
      const base=ELEMENTS[own].color;
      const n=rcen[i];
      const lum=0.62+0.5*Math.max(0,n[0]*LI[0]+n[1]*LI[1]+n[2]*LI[2]);
      const mine=own===faction;
      // skirts always drawn: they self-collapse to sub-pixel width toward the
      // disc centre, and any threshold puts a visible tone ring where tiles cross it
      ctx.fillStyle=shade(base,lum*0.42);
      for(let k=0;k<t.corners.length;k++){
        const a=rcorn[t.corners[k]], b=rcorn[t.corners[(k+1)%t.corners.length]];
        const A1=P([a[0]*EXH,a[1]*EXH,0]), B1=P([b[0]*EXH,b[1]*EXH,0]);
        const A0=P(a), B0=P(b);
        ctx.beginPath(); ctx.moveTo(A1[0],A1[1]); ctx.lineTo(B1[0],B1[1]); ctx.lineTo(B0[0],B0[1]); ctx.lineTo(A0[0],A0[1]); ctx.closePath(); ctx.fill();
      }
      ctx.fillStyle=shade(base, lum*(mine?1.18:1));
      ctx.beginPath();
      t.corners.forEach((ci,k)=>{ const p=rcorn[ci];
        const q=P([(p[0]+(n[0]-p[0])*(1-INSET))*EXH,(p[1]+(n[1]-p[1])*(1-INSET))*EXH,0]);
        k?ctx.lineTo(q[0],q[1]):ctx.moveTo(q[0],q[1]); });
      ctx.closePath(); ctx.fill();
      ctx.strokeStyle='rgba(0,0,0,0.25)'; ctx.lineWidth=0.6; ctx.stroke();
    }
    // territory / empire borders drawn over the tiles
    ctx.lineCap='round';
    for(let i=0;i<T.length;i++){ if(rcen[i][2]<0.02)continue;
      for(const j of T[i].adj){ if(j<i || rcen[j][2]<0.02) continue;
        const ta=M.tileTerr[i], tb=M.tileTerr[j]; if(ta===tb)continue;
        const oa=M.terr[ta].owner, ob=M.terr[tb].owner;
        const shared=T[i].corners.filter(c=>T[j].corners.includes(c));
        if(shared.length!==2)continue;
        const sa=rcorn[shared[0]], sb=rcorn[shared[1]];
        const A=P([sa[0]*EXH,sa[1]*EXH,0]), B=P([sb[0]*EXH,sb[1]*EXH,0]);
        if(oa===ob){ ctx.strokeStyle='rgba(0,0,0,0.35)'; ctx.lineWidth=1.1; }
        else if(oa===faction||ob===faction){ ctx.strokeStyle='#d9b64a'; ctx.lineWidth=3; }
        else { ctx.strokeStyle='rgba(244,240,255,0.85)'; ctx.lineWidth=2.2; }
        ctx.beginPath(); ctx.moveTo(A[0],A[1]); ctx.lineTo(B[0],B[1]); ctx.stroke();
      }
    }
    // garrison / capital markers at each territory anchor
    const pulse=0.55+0.45*Math.sin(ts/450);
    const mk=Math.max(0.8,Math.min(1.25,R/240));
    for(const tid of M.ids){ const t=M.terr[tid]; const rz=rcen[t.anchor];
      if(rz[2]<0.18)continue;
      const p=P([rz[0]*(EXH+0.02),rz[1]*(EXH+0.02),0]);
      let capEl=null; for(const el in M.capitals){ if(M.capitals[el]===tid){ capEl=el; break; } }
      const att=attackable(tid); const mine=t.owner===faction;
      const rr=(capEl?16:11.5)*mk*Math.min(1,0.75+rz[2]*0.35);
      ctx.beginPath(); ctx.arc(p[0],p[1],rr,0,7);
      ctx.fillStyle='rgba(8,6,14,0.78)'; ctx.fill();
      ctx.lineWidth=att?2.6:(mine?2:1.4);
      ctx.strokeStyle=att?`rgba(217,182,74,${pulse})`:(mine?'#fff':'rgba(255,255,255,0.3)');
      ctx.stroke();
      ctx.fillStyle='#fff'; ctx.textAlign='center'; ctx.textBaseline='alphabetic';
      if(capEl){ ctx.font=`700 ${Math.round(13*mk)}px serif`; ctx.fillText(ELEMENTS[capEl].glyph,p[0],p[1]-1);
        ctx.font=`700 ${Math.round(9.5*mk)}px serif`; ctx.fillText(t.garrison,p[0],p[1]+10*mk); }
      else { ctx.font=`700 ${Math.round(12*mk)}px serif`; ctx.fillText(t.garrison,p[0],p[1]+4*mk); }
    }
    if(!dragging && ts-lastInteract>2600) V.yaw+=0.0011;
    V.yaw+=V.vyaw; V.vyaw*=0.93;
    campGlobeRAF=requestAnimationFrame(draw);
  }
  cancelAnimationFrame(campGlobeRAF);
  campGlobeRAF=requestAnimationFrame(draw);
  campGlobeDrawNow=()=>{ cancelAnimationFrame(campGlobeRAF); draw(performance.now()); };
  /* input — stopPropagation so document-level game handlers (edge-tap wall
     toggles, global off-click) never see globe gestures */
  let pd=null;
  canvas.addEventListener('pointerdown',e=>{ e.stopPropagation();
    if(pd) return;   // a 2nd finger must not hijack the drag or reset `moved` into a spurious pick
    pd={x:e.clientX,y:e.clientY,id:e.pointerId,moved:false}; dragging=true; lastInteract=performance.now();
    try{ canvas.setPointerCapture(e.pointerId); }catch(err){} });
  canvas.addEventListener('pointermove',e=>{ if(!pd||e.pointerId!==pd.id)return;
    const dx=e.clientX-pd.x, dy=e.clientY-pd.y;
    const th=(e.pointerType==='touch')?15:7;
    if(Math.abs(dx)+Math.abs(dy)>th) pd.moved=true;
    if(pd.moved){ V.yaw+=dx*0.005; V.pitch=Math.max(-1.25,Math.min(1.25,V.pitch-dy*0.005));
      V.vyaw=dx*0.0009; pd.x=e.clientX; pd.y=e.clientY; lastInteract=performance.now(); } });
  canvas.addEventListener('pointerup',e=>{ if(!pd||e.pointerId!==pd.id)return;
    e.stopPropagation(); dragging=false; lastInteract=performance.now();
    if(!pd.moved){
      // invert against the radius the tiles are actually DRAWN on (R*EXH, since
      // every face is extruded) — dividing by R alone biases the ray outward,
      // which lands roughly one tap in seven on the wrong territory
      const rect=canvas.getBoundingClientRect(), RS=R*EXH;
      const x=(e.clientX-rect.left-CX)/RS, y=-(e.clientY-rect.top-CY)/RS;
      const rr=x*x+y*y;
      if(rr<=1.06){ const z=Math.sqrt(1-Math.min(1,rr));
        const v=unrot([x,y,z]);
        let bt=0,bd=-2; T.forEach((t,i)=>{ const d=t.c[0]*v[0]+t.c[1]*v[1]+t.c[2]*v[2]; if(d>bd){bd=d;bt=i;} });
        if(onPick) onPick(M.tileTerr[bt]);
      } }
    pd=null; });
  const endPointer=e=>{ if(!pd||e.pointerId!==pd.id)return;   // a cancel on ANY pointer must not kill an unrelated live drag
    const id=pd.id; pd=null; dragging=false;                  // null FIRST: releasing capture re-fires lostpointercapture
    try{ canvas.releasePointerCapture(id); }catch(err){} };
  canvas.addEventListener('pointercancel',endPointer);
  canvas.addEventListener('lostpointercapture',endPointer);  // capture lost without up/cancel would otherwise wedge `pd`
  canvas.addEventListener('click',e=>e.stopPropagation());
}
(function injectGlobeCSS(){ const s=document.createElement('style'); s.textContent=`
.campglobe{width:100%;height:100%;display:block;touch-action:none;cursor:grab;}
.campglobe:active{cursor:grabbing;}
`; document.head.appendChild(s); })();
/* ===== end campaign globe ===== */
