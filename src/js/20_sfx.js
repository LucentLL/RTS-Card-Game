/* ---------- SFX: all sounds synthesized live via WebAudio (no assets) ---------- */
const SFX=(()=>{ let ctx=null,master=null,muted=false;
  function ac(){ const AC=window.AudioContext||window.webkitAudioContext; if(!AC)return null;
    if(!ctx){ ctx=new AC(); master=ctx.createGain(); master.gain.value=.5; master.connect(ctx.destination); }
    if(ctx.state==='suspended')ctx.resume(); return ctx; }
  function env(g,t,a,d,peak){ g.gain.setValueAtTime(0,t); g.gain.linearRampToValueAtTime(peak,t+a); g.gain.exponentialRampToValueAtTime(.0001,t+a+d); }
  function tone(o){ const c=ac(); if(!c)return; const{f=440,f2,type='sine',a=.005,d=.2,v=.4,delay=0}=o;
    const t=c.currentTime+delay, osc=c.createOscillator(), g=c.createGain();
    osc.type=type; osc.frequency.setValueAtTime(f,t); if(f2)osc.frequency.exponentialRampToValueAtTime(Math.max(20,f2),t+a+d);
    env(g,t,a,d,v); osc.connect(g); g.connect(master); osc.start(t); osc.stop(t+a+d+.06); }
  function noise(o){ const c=ac(); if(!c)return; const{d=.2,v=.35,from=4000,to=400,type='bandpass',q=1,delay=0}=o;
    const t=c.currentTime+delay, len=Math.ceil(c.sampleRate*(d+.05)), buf=c.createBuffer(1,len,c.sampleRate), ch=buf.getChannelData(0);
    for(let i=0;i<len;i++)ch[i]=Math.random()*2-1;
    const src=c.createBufferSource(); src.buffer=buf;
    const flt=c.createBiquadFilter(); flt.type=type; flt.Q.value=q;
    flt.frequency.setValueAtTime(from,t); flt.frequency.exponentialRampToValueAtTime(Math.max(30,to),t+d);
    const g=c.createGain(); env(g,t,.005,d,v); src.connect(flt); flt.connect(g); g.connect(master); src.start(t); src.stop(t+d+.06); }
  const S={
    click(){tone({f:900,type:'triangle',d:.05,v:.1});},
    draw(){noise({d:.15,from:1200,to:5200,v:.16});},
    place(){tone({f:160,f2:70,d:.18,v:.5}); noise({d:.07,from:900,to:200,v:.12}); noise({d:.04,from:2000,to:600,v:.1,type:'bandpass',q:2});},
    set(){tone({f:300,f2:120,d:.12,v:.22});},
    summon(){tone({f:220,f2:660,type:'sawtooth',d:.3,v:.16}); tone({f:880,d:.4,v:.12,delay:.12}); tone({f:1320,d:.5,v:.07,delay:.2});},
    raise(){tone({f:90,f2:58,d:.4,v:.55}); noise({d:.3,from:500,to:120,v:.2}); tone({f:523,type:'triangle',d:.3,v:.1,delay:.18});},
    whoosh(){noise({d:.18,from:2600,to:300,v:.3});},
    hit(){noise({d:.12,from:1800,to:150,v:.45}); tone({f:120,f2:48,d:.16,v:.55});},
    clash(){S.whoosh(); tone({f:120,f2:48,d:.16,v:.55,delay:.08}); noise({d:.12,from:1800,to:150,v:.45,delay:.08});},
    raze(){tone({f:78,f2:30,d:.7,v:.65}); noise({d:.6,from:700,to:60,v:.42,type:'lowpass'});},
    spell(){[660,880,1100,1320].forEach((f,i)=>tone({f,d:.25,v:.11,delay:i*.05}));},
    trap(){tone({f:1400,f2:200,type:'square',d:.2,v:.16}); noise({d:.12,from:3000,to:500,v:.18,delay:.04});},
    mana(){tone({f:1046,d:.28,v:.1}); tone({f:1568,d:.34,v:.07,delay:.04});},
    train(){tone({f:520,f2:380,type:'square',d:.06,v:.1}); tone({f:560,f2:400,type:'square',d:.06,v:.1,delay:.12});},
    turnYou(){tone({f:392,type:'triangle',d:.16,v:.18}); tone({f:587,type:'triangle',d:.3,v:.18,delay:.12});},
    turnFoe(){tone({f:330,type:'triangle',d:.16,v:.14}); tone({f:247,type:'triangle',d:.3,v:.14,delay:.12});},
    win(){[523,659,784,1046].forEach((f,i)=>tone({f,type:'triangle',d:.45,v:.2,delay:i*.13})); tone({f:1318,type:'triangle',d:.8,v:.14,delay:.55});},
    lose(){[392,330,262,196].forEach((f,i)=>tone({f,d:.5,v:.18,delay:i*.16}));},
    move(){noise({d:.13,from:600,to:1500,v:.11,type:'bandpass',q:.7}); tone({f:220,f2:340,type:'triangle',d:.1,v:.12});},
    block(){tone({f:2400,f2:1200,type:'square',d:.05,v:.13}); tone({f:1800,f2:900,type:'triangle',d:.09,v:.09,delay:.01}); noise({d:.06,from:5000,to:1500,v:.15,type:'bandpass',q:3});},
    swing(){noise({d:.14,from:1500,to:240,v:.32,type:'bandpass',q:.6}); tone({f:170,f2:70,type:'sawtooth',d:.1,v:.15});},
    build(){tone({f:90,f2:60,d:.34,v:.5}); noise({d:.18,from:800,to:150,v:.2,type:'lowpass'}); tone({f:392,type:'triangle',d:.14,v:.09,delay:.05}); tone({f:523,type:'triangle',d:.16,v:.08,delay:.13});},
    shuffle(){for(let i=0;i<5;i++)noise({d:.05,from:2600-i*120,to:700,v:.08,type:'bandpass',q:2,delay:i*.045});},
  };
  const api={}; let vol=.5;
  Object.keys(S).forEach(k=>api[k]=function(){ if(muted)return; try{S[k]();}catch(e){} });
  api.toggle=()=>{ muted=!muted; return muted; };
  api.isMuted=()=>muted;
  api.setMuted=(m)=>{ muted=!!m; };
  api.setVolume=(v)=>{ vol=Math.max(0,Math.min(1,v)); ac(); if(master)master.gain.value=vol; };
  api.getVolume=()=>vol;
  api.unlock=()=>{ try{ac();}catch(e){} };
  return api;
})();
document.addEventListener('pointerdown',()=>SFX.unlock(),true);
document.addEventListener('click',e=>{ if(e.target&&e.target.closest&&e.target.closest('button'))SFX.click(); },true);
// PC anti-highlight backstop: kill the browser's native drag ghost (card art is <img>, so a drag
// otherwise "picks up" a translucent copy of the picture) and any stray text selection — except
// inside real inputs (MP codes, password, deck name), which still need the caret + selection.
document.addEventListener('dragstart',e=>e.preventDefault(),true);
document.addEventListener('selectstart',e=>{ if(!(e.target&&e.target.closest&&e.target.closest('input,textarea')))e.preventDefault(); },true);

