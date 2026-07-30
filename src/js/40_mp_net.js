/* ═══════════════════════ MULTIPLAYER LAYER (MP) · WebRTC host-authoritative duel ═══════════════════════
   Loads LAST so every re-binding below wraps the FX- and RESP-wrapped flow (call sites late-bind).
   Order inside this block: MPNET transport → MPMAP mirror → MPSER snapshots → MP core → MPAPPLY intents
   → §4.6 wrappers → freeze re-binds → FX replay stub + decisions + protocol pump + lobby JS. */

/* ---------- 4.1 MPNET: manual-signalling WebRTC transport (password-sealed, compressed connect codes) ---------- */
const MPNET=(function(){
  const N={PROTO:2,active:false,onOpen:null,onMsg:null,onDrop:null};   // 2: Combat v3 rules (row-interval blocking, wall targets, vault mana)
  let pc=null,dc=null,closing=false;
  // STUN discovers each player's public address so most pairs can hole-punch a direct link.
  // (The old free public TURN relays are gone — verified dead 2026-07-10 — so pairs that can't
  // hole-punch fall back to RELAYED PLAY over a public MQTT broker instead: see relayConnect below.)
  const RTC={iceServers:[
    {urls:['stun:stun.l.google.com:19302','stun:stun1.l.google.com:19302','stun:stun2.l.google.com:19302']}
  ]};
  const enc=new TextEncoder(), dec=new TextDecoder();
  const b64=b=>{let s='';const u=new Uint8Array(b);for(let i=0;i<u.length;i+=0x8000)s+=String.fromCharCode.apply(null,u.subarray(i,i+0x8000));return btoa(s).replace(/\+/g,'-').replace(/\//g,'_').replace(/=+$/,'');};
  const unb64=s=>{s=s.replace(/-/g,'+').replace(/_/g,'/');while(s.length%4)s+='=';const bin=atob(s);const u=new Uint8Array(bin.length);for(let i=0;i<bin.length;i++)u[i]=bin.charCodeAt(i);return u;};
  async function squeeze(str){ if(typeof CompressionStream==='undefined')return {z:0,u:enc.encode(str)};
    const buf=await new Response(new Blob([enc.encode(str)]).stream().pipeThrough(new CompressionStream('deflate-raw'))).arrayBuffer();
    return {z:1,u:new Uint8Array(buf)}; }
  async function unsqueeze(z,u){ if(!z)return dec.decode(u);
    const buf=await new Response(new Blob([u]).stream().pipeThrough(new DecompressionStream('deflate-raw'))).arrayBuffer();
    return dec.decode(buf); }
  async function pkey(pass,salt){ const km=await crypto.subtle.importKey('raw',enc.encode(String(pass)),'PBKDF2',false,['deriveKey']);
    return crypto.subtle.deriveKey({name:'PBKDF2',salt,iterations:60000,hash:'SHA-256'},km,{name:'AES-GCM',length:256},false,['encrypt','decrypt']); }
  async function seal(obj,pass){ const {z,u}=await squeeze(JSON.stringify(obj));
    const salt=crypto.getRandomValues(new Uint8Array(12)), iv=crypto.getRandomValues(new Uint8Array(12));
    const ct=new Uint8Array(await crypto.subtle.encrypt({name:'AES-GCM',iv},await pkey(pass,salt),u));
    const out=new Uint8Array(25+ct.length); out[0]=z; out.set(salt,1); out.set(iv,13); out.set(ct,25);
    return b64(out.buffer); }
  async function unseal(code,pass){ let u; try{ u=unb64(String(code||'').replace(/\s+/g,'')); }catch(e){ throw new Error('That code is garbled — paste it whole.'); }
    if(u.length<26) throw new Error('That code is garbled — paste it whole.');
    let pt; try{ pt=await crypto.subtle.decrypt({name:'AES-GCM',iv:u.slice(13,25)},await pkey(pass,u.slice(1,13)),u.slice(25)); }
    catch(e){ throw new Error('Wrong password'); }
    return JSON.parse(await unsqueeze(u[0],new Uint8Array(pt))); }
  function gathered(p){ return new Promise(res=>{ if(p.iceGatheringState==='complete'){res();return;}
    let done=false; const finish=()=>{ if(done)return; done=true; clearTimeout(t); res(); };
    const t=setTimeout(finish,3000);                               // cap the wait; TURN relay candidates need a round-trip to appear
    p.addEventListener('icegatheringstatechange',()=>{ if(p.iceGatheringState==='complete')finish(); });
    p.addEventListener('icecandidate',e=>{ if(e.candidate&&/typ relay/.test(e.candidate.candidate))setTimeout(finish,300); }); // a relay path exists → wrap up promptly (localhost has none → falls through to 'complete')
  }); }
  function watch(p){ p.addEventListener('connectionstatechange',()=>{ if(pc!==p)return;
    if(p.connectionState==='failed'||p.connectionState==='closed'){
      if(N.mode==='relay'){ closeP2P(); return; }        // the duel lives on the relay — shed the dead pc quietly
      drop(p.connectionState); } }); }
  function drop(why){ if(closing)return; const was=N.active; teardown();
    if(was&&N.onDrop)try{N.onDrop(why);}catch(e){} }
  function closeP2P(){ try{ if(dc)dc.close(); }catch(e){} try{ if(pc)pc.close(); }catch(e){} dc=null; pc=null; }
  function teardown(){ N.active=false; N.mode=null; closeP2P(); closeRelay(); }
  // --- fragmentation: snapshots run 25–40 KB; chunk under the 16 KB interop floor. Frame: '#id|k|n\n'+slice
  const CHUNK=12000; let txId=0,rxId=0,rxBuf=null,rxLeft=0;
  function wire(ch){ dc=ch;
    dc.onopen=()=>{
      if(N.active&&N.mode==='relay'){ try{dc.close();}catch(e){} return; }   // relay won the race — keep it
      N.mode='p2p'; closeRelay();                                            // direct link up — shed any relay attempt
      N.active=true; closing=false; if(N.onOpen)try{N.onOpen();}catch(e){} };
    dc.onclose=()=>{ if(N.mode==='relay')return; drop('channel closed'); }; dc.onerror=()=>{};
    dc.onmessage=ev=>{ let s=ev.data; if(typeof s!=='string')return;
      if(s.charCodeAt(0)===35){
        const h=s.indexOf('\n'); const parts=s.slice(1,h).split('|'); const id=+parts[0],k=+parts[1],n=+parts[2];
        if(id!==rxId||!rxBuf){ rxId=id; rxBuf=new Array(n); rxLeft=n; }
        if(rxBuf[k]==null){ rxBuf[k]=s.slice(h+1); rxLeft--; }
        if(rxLeft>0)return; s=rxBuf.join(''); rxBuf=null;
      }
      let m; try{ m=JSON.parse(s); }catch(e){ return; }
      if(N.onMsg)try{N.onMsg(m);}catch(e){ console.warn('MP msg',e); } }; }
  N.send=function(obj){
    if(N.mode==='relay'){ if(!N.active||!N._relaySend)return false; N._relaySend(obj); return true; }   // broker packets carry whole frames — no chunking
    if(!dc||dc.readyState!=='open')return false; const s=JSON.stringify(obj);
    if(s.length<=CHUNK){ dc.send(s); return true; }
    const n=Math.ceil(s.length/CHUNK); const id=++txId;
    for(let k=0;k<n;k++) dc.send('#'+id+'|'+k+'|'+n+'\n'+s.slice(k*CHUNK,(k+1)*CHUNK));
    return true; };
  N.hostOffer=async function(pass){ if(!pass)throw new Error('Pick a password first.');
    closing=true; teardown(); closing=false;
    pc=new RTCPeerConnection(RTC); watch(pc); wire(pc.createDataChannel('srd'));
    await pc.setLocalDescription(await pc.createOffer()); await gathered(pc);
    return seal({v:N.PROTO,sdp:pc.localDescription.sdp},pass); };
  N.hostAccept=async function(answer,pass){ if(!pc)throw new Error('Create your code first.');
    const o=await unseal(answer,pass); if(!o||!o.sdp)throw new Error('That is not an answer code.');
    await pc.setRemoteDescription({type:'answer',sdp:o.sdp}); };
  N.joinWithOffer=async function(offer,pass){ if(!pass)throw new Error('Enter the shared password.');
    closing=true; teardown(); closing=false;
    const o=await unseal(offer,pass); if(!o||!o.sdp)throw new Error('That is not a host code.');
    if(o.v!==N.PROTO)throw new Error('Version mismatch — both players need the same build.');
    pc=new RTCPeerConnection(RTC); watch(pc); pc.ondatachannel=ev=>wire(ev.channel);
    await pc.setRemoteDescription({type:'offer',sdp:o.sdp});
    await pc.setLocalDescription(await pc.createAnswer()); await gathered(pc);
    return seal({v:N.PROTO,sdp:pc.localDescription.sdp},pass); };
  // ---- RELAYED PLAY (fallback transport): when no direct P2P link can form (client-isolated
  //      wifi, carrier CGNAT — and no free public TURN exists any more), the whole duel runs over
  //      a public MQTT-over-WebSocket broker instead. Same host-authoritative wire protocol; every
  //      frame is deflate+AES-GCM sealed with a key derived ONCE from the match password, so the
  //      broker only ever carries ciphertext. Turn-based traffic → broker latency is irrelevant.
  const BROKERS=['wss://broker.emqx.io:8084/mqtt','wss://broker.hivemq.com:8884/mqtt'];
  let ws=null,wsPing=null,rdyT=null,relayKey=null,txQ=Promise.resolve(),rxQ=Promise.resolve();
  function vlen(n){ const o=[]; do{ let b=n%128; n=Math.floor(n/128); if(n>0)b|=128; o.push(b); }while(n>0); return o; }
  function mstr(s){ const u=enc.encode(s); return [u.length>>8,u.length&255,...u]; }
  function closeRelay(){ clearInterval(wsPing); wsPing=null; clearTimeout(rdyT); rdyT=null;
    if(ws){ const s=ws; ws=null; try{s.onclose=null;s.close();}catch(e){} } N._relaySend=null; }
  async function relayTopics(pass){ const u=new Uint8Array(await crypto.subtle.digest('SHA-256',enc.encode('srd.mp.v1|'+String(pass))));
    let s=''; for(let i=0;i<15;i++)s+=('0'+u[i].toString(16)).slice(-2);
    return {hg:'srd-'+s+'-hg', gh:'srd-'+s+'-gh'}; }                       // host→guest / guest→host lanes
  async function relayKeyOf(pass){ const km=await crypto.subtle.importKey('raw',enc.encode(String(pass)),'PBKDF2',false,['deriveKey']);
    return crypto.subtle.deriveKey({name:'PBKDF2',salt:enc.encode('srd.relay.v1'),iterations:60000,hash:'SHA-256'},km,{name:'AES-GCM',length:256},false,['encrypt','decrypt']); }
  async function sealFrame(obj){ const {z,u}=await squeeze(JSON.stringify(obj));
    const iv=crypto.getRandomValues(new Uint8Array(12));
    const ct=new Uint8Array(await crypto.subtle.encrypt({name:'AES-GCM',iv},relayKey,u));
    const out=new Uint8Array(13+ct.length); out[0]=z; out.set(iv,1); out.set(ct,13); return out; }
  async function openFrame(u){ const pt=await crypto.subtle.decrypt({name:'AES-GCM',iv:u.slice(1,13)},relayKey,u.slice(13));
    return JSON.parse(await unsqueeze(u[0],new Uint8Array(pt))); }
  N.relayConnect=async function(pass,role){
    if(N.active)return;                                  // the direct link beat us to it
    closeRelay(); N.mode='relay';
    relayKey=await relayKeyOf(pass);
    const t=await relayTopics(pass);
    const sub=role==='host'?t.gh:t.hg, pub=role==='host'?t.hg:t.gh;
    let bi=0;
    (function tryBroker(){
      if(N.active&&N.mode==='p2p')return;                // p2p connected while we were dialing
      if(bi>=BROKERS.length){ if(N.mode==='relay')N.mode=null;
        if(!N.active&&N.onDrop)try{N.onDrop('relay unreachable');}catch(e){} return; }
      const url=BROKERS[bi++]; let stage='ws'; let sock;
      try{ sock=new WebSocket(url,'mqtt'); }catch(e){ tryBroker(); return; }
      ws=sock; sock.binaryType='arraybuffer';
      const guard=setTimeout(()=>{ if(stage!=='ready'){ try{sock.close();}catch(_){} } },9000);
      sock.onclose=()=>{ clearTimeout(guard); if(ws!==sock)return; ws=null;
        if(stage!=='ready'){ tryBroker(); }              // this broker failed — try the next
        else if(N.active&&N.mode==='relay'){ N.active=false; const cb=N.onDrop; teardown(); if(cb&&!closing)try{cb('relay closed');}catch(e){} } };
      sock.onerror=()=>{};
      sock.onopen=()=>{ stage='connect';                 // MQTT 3.1.1 CONNECT (clean session, 60s keepalive)
        const vh=[...mstr('MQTT'),4,2,0,60,...mstr('srd_'+Math.random().toString(36).slice(2,10))];
        sock.send(new Uint8Array([0x10,...vlen(vh.length),...vh])); };
      function relayPub(obj){ txQ=txQ.then(async()=>{ if(ws!==sock||sock.readyState!==1)return;
        const f=await sealFrame(obj); const th=mstr(pub);
        const head=[0x30,...vlen(th.length+f.length)];
        const pkt=new Uint8Array(head.length+th.length+f.length);
        pkt.set(head,0); pkt.set(th,head.length); pkt.set(f,head.length+th.length);
        sock.send(pkt); }).catch(()=>{}); }
      sock.onmessage=ev=>{ const u=new Uint8Array(ev.data); const type=u[0]>>4;
        if(type===2){ stage='sub';                        // CONNACK → SUBSCRIBE our inbound lane
          const vh=[0,1,...mstr(sub),0]; sock.send(new Uint8Array([0x82,...vlen(vh.length),...vh])); }
        else if(type===9){ stage='ready';                 // SUBACK → beacon until the peer answers
          N._relaySend=relayPub;
          wsPing=setInterval(()=>{ try{sock.send(new Uint8Array([0xC0,0]));}catch(e){} },30000);
          (function beacon(){ if(N.active||ws!==sock)return; relayPub({t:'__rdy'}); rdyT=setTimeout(beacon,1000); })(); }
        else if(type===3){                                // PUBLISH → one sealed frame from the peer
          let i=1; while(u[i]&128)i++; i++; const tl=(u[i]<<8)|u[i+1]; const body=u.slice(i+2+tl);
          rxQ=rxQ.then(async()=>{ let m; try{ m=await openFrame(body); }catch(e){ return; }   // wrong password / garbled — ignore
            if(N.mode!=='relay')return;                   // p2p took over meanwhile
            if(!N.active){ N.active=true; closing=false; clearTimeout(rdyT); closeP2P();
              relayPub({t:'__rdy'});                      // answer once more so the slower side opens too
              if(N.onOpen)try{N.onOpen();}catch(e){} }
            if(m&&m.t==='__rdy')return;
            if(N.onMsg)try{N.onMsg(m);}catch(e){ console.warn('MP msg',e); } }); } };
    })();
  };
  N.close=function(){ closing=true; teardown(); };
  return N;
})();

/* ---------- 4.1b MPSIG: password rendezvous — the same SEALED offer/answer codes the manual flow
   uses, traded automatically over a public pub-sub relay (ntfy.sh) on a channel derived from a
   SHA-256 of the password. The relay sees only AES-GCM ciphertext, once, at connect time; the duel
   itself runs peer-to-peer exactly as before. Manual copy/paste stays as the offline fallback. ---------- */
const MPSIG=(function(){
  const BASE='https://ntfy.sh';
  let es=null,waitT=null;
  async function topic(pass,side){                       // un-guessable channel id from the shared password
    const u=new Uint8Array(await crypto.subtle.digest('SHA-256',new TextEncoder().encode('srd.mp.v1|'+String(pass))));
    let s=''; for(let i=0;i<15;i++)s+=('0'+u[i].toString(16)).slice(-2);
    return 'srd-'+s+'-'+side;                            // -h: host offers · -g: guest answers
  }
  function stop(){ clearTimeout(waitT); waitT=null; try{ if(es)es.close(); }catch(_){} es=null; }
  async function publish(t,body){
    const r=await fetch(BASE+'/'+t,{method:'POST',body});
    if(!r.ok)throw new Error('relay refused ('+r.status+')');
  }
  async function recent(t,since){                        // newest cached message on the channel, if any
    try{ const r=await fetch(BASE+'/'+t+'/json?poll=1&since='+(since||'15m'));
      if(!r.ok)return null;
      const rows=(await r.text()).trim().split('\n').filter(Boolean)
        .map(s=>{try{return JSON.parse(s);}catch(_){return null;}})
        .filter(m=>m&&m.event==='message'&&m.message);
      return rows.length?rows[rows.length-1].message:null;
    }catch(_){ return null; }
  }
  function listen(t,onMsg,since){                        // live subscription (server-sent events);
    stop();                                              // `since` replays the recent cache on connect, closing
    es=new EventSource(BASE+'/'+t+'/sse?since='+(since||'10m'));   // the publish→poll cache-lag race
    es.onmessage=ev=>{ try{ const m=JSON.parse(ev.data); if(m&&m.event==='message'&&m.message)onMsg(m.message); }catch(_){} };
  }
  function deadline(fn,ms){ clearTimeout(waitT); waitT=setTimeout(fn,ms); }
  return {topic,publish,recent,listen,deadline,stop};
})();

