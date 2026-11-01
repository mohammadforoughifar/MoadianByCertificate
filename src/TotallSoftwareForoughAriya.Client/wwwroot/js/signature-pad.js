// ============================================================
//  پنل امضای الکترونیکی (canvas) — ماژول صورتجلسه
//  پشتیبانی موس و لمس (pointer events)؛ خروجی PNG شفاف base64
// ============================================================
window.sigPadInit = function (canvas) {
    if (!canvas) return false;
    var ctx = canvas.getContext('2d');
    var drawing = false;
    var color = '#1e3a8a';
    var lineWidth = 2.5;

    function pos(e) {
        var r = canvas.getBoundingClientRect();
        return {
            x: (e.clientX - r.left) * (canvas.width / r.width),
            y: (e.clientY - r.top) * (canvas.height / r.height)
        };
    }

    canvas.style.touchAction = 'none';
    canvas.style.cursor = 'crosshair';

    canvas.addEventListener('pointerdown', function (e) {
        e.preventDefault();
        drawing = true;
        try { canvas.setPointerCapture(e.pointerId); } catch (err) { }
        var p = pos(e);
        ctx.beginPath();
        ctx.moveTo(p.x, p.y);
        ctx.lineTo(p.x + 0.1, p.y + 0.1);
        ctx.strokeStyle = color;
        ctx.lineWidth = lineWidth;
        ctx.lineCap = 'round';
        ctx.lineJoin = 'round';
        ctx.stroke();
    });

    canvas.addEventListener('pointermove', function (e) {
        if (!drawing) return;
        e.preventDefault();
        var p = pos(e);
        ctx.lineTo(p.x, p.y);
        ctx.stroke();
    });

    function end() { drawing = false; }
    canvas.addEventListener('pointerup', end);
    canvas.addEventListener('pointercancel', end);
    canvas.addEventListener('pointerleave', end);

    return true;
};

window.sigPadClear = function (canvas) {
    if (!canvas) return;
    var ctx = canvas.getContext('2d');
    ctx.clearRect(0, 0, canvas.width, canvas.height);
};

window.sigPadEmpty = function (canvas) {
    if (!canvas) return true;
    var d = canvas.getContext('2d').getImageData(0, 0, canvas.width, canvas.height).data;
    for (var i = 3; i < d.length; i += 4) if (d[i] !== 0) return false;
    return true;
};

window.sigPadToBase64 = function (canvas) {
    if (!canvas) return '';
    return canvas.toDataURL('image/png');
};

// دانلود فایل از base64 (برای PDF چاپی و...)
window.downloadBase64 = function (fileName, dataBase64, mime) {
    try {
        var bytes = atob(dataBase64);
        var arr = new Uint8Array(bytes.length);
        for (var i = 0; i < bytes.length; i++) arr[i] = bytes.charCodeAt(i);
        var blob = new Blob([arr], { type: mime || 'application/octet-stream' });
        var url = URL.createObjectURL(blob);
        var a = document.createElement('a');
        a.href = url;
        a.download = fileName || 'download';
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        setTimeout(function () { URL.revokeObjectURL(url); }, 4000);
        return true;
    } catch (e) {
        return false;
    }
};

// ==================== PWA آرشیو — امضای ترسیمی سبک + WebAuthn بیومتریک (B2/PWA) ====================
(function(){
  let padCanvas=null, ctx=null, drawing=false, hasStroke=false;
  function getPos(e, rect){
    const t=e.touches? e.touches[0]: e;
    return {x: t.clientX - rect.left, y: t.clientY - rect.top};
  }
  window.signaturePad={
    init(canvasId){
      padCanvas=document.getElementById(canvasId);
      if(!padCanvas) return false;
      ctx=padCanvas.getContext('2d');
      const dpr=window.devicePixelRatio||1;
      const rect=padCanvas.getBoundingClientRect();
      padCanvas.width=rect.width*dpr;
      padCanvas.height=rect.height*dpr;
      ctx.scale(dpr,dpr);
      ctx.lineWidth=2.2; ctx.lineCap='round'; ctx.lineJoin='round'; ctx.strokeStyle='#0f172a';
      hasStroke=false;
      const getRect=()=>padCanvas.getBoundingClientRect();
      const start=e=>{ e.preventDefault(); drawing=true; hasStroke=true; const p=getPos(e,getRect()); ctx.beginPath(); ctx.moveTo(p.x,p.y); };
      const move=e=>{ if(!drawing) return; e.preventDefault(); const p=getPos(e,getRect()); ctx.lineTo(p.x,p.y); ctx.stroke(); };
      const end=e=>{ if(drawing){ e&&e.preventDefault(); drawing=false; ctx.closePath(); } };
      padCanvas.addEventListener('mousedown', start);
      padCanvas.addEventListener('mousemove', move);
      window.addEventListener('mouseup', end);
      padCanvas.addEventListener('touchstart', start, {passive:false});
      padCanvas.addEventListener('touchmove', move, {passive:false});
      window.addEventListener('touchend', end);
      padCanvas._sigHandlers={start,move,end};
      return true;
    },
    clear(){
      if(!padCanvas||!ctx) return;
      const rect=padCanvas.getBoundingClientRect();
      ctx.clearRect(0,0, rect.width, rect.height);
      hasStroke=false;
    },
    isEmpty(){ return !hasStroke; },
    toDataUrl(){
      if(!padCanvas) return null;
      if(this.isEmpty()) return null;
      try{ return padCanvas.toDataURL('image/png'); }catch{return null}
    },
    destroy(){
      if(!padCanvas) return;
      try{
        const h=padCanvas._sigHandlers;
        if(h){ padCanvas.removeEventListener('mousedown',h.start); padCanvas.removeEventListener('mousemove',h.move); padCanvas.removeEventListener('touchstart',h.start); padCanvas.removeEventListener('touchmove',h.move); }
      }catch{}
      padCanvas=null; ctx=null; hasStroke=false;
    }
  };
  window.biometric={
    isAvailable(){ return !!(window.PublicKeyCredential && navigator.credentials); },
    async verify(){
      if(!this.isAvailable()) return {ok:false, reason:'unsupported'};
      try{
        const challenge=new Uint8Array(32); crypto.getRandomValues(challenge);
        if(navigator.credentials && window.PublicKeyCredential){
          const avail=await PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable().catch(()=>false);
          if(!avail) return {ok:false, reason:'no-platform-authenticator'};
          try{
            await navigator.credentials.get({ publicKey:{ challenge, timeout:60000, userVerification:'required', allowCredentials:[] } });
            return {ok:true};
          }catch(e){
            try{
              const userId=new Uint8Array(16); crypto.getRandomValues(userId);
              await navigator.credentials.create({ publicKey:{ challenge, rp:{name:'فروغ آریا'}, user:{id:userId, name:'user', displayName:'کاربر'}, pubKeyCredParams:[{type:'public-key', alg:-7}], authenticatorSelection:{ authenticatorAttachment:'platform', userVerification:'required', requireResidentKey:false }, timeout:60000, attestation:'none' } });
              return {ok:true};
            }catch(e2){ return {ok:false, reason: (e2&&e2.name)||'cancelled' }; }
          }
        }
        return {ok:false, reason:'unsupported'};
      }catch(e){ return {ok:false, reason:(e&&e.name)||'error'}; }
    }
  };
})();
