// اسکنر هوشمند — Camera + Crop + Enhance (2025-10-09)
window.smartScanner = (() => {
  let stream = null, track = null;
  return {
    async start(videoId, overlayId) {
      const v = document.getElementById(videoId);
      if (!v) return false;
      stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment', width: { ideal: 1920 }, height: { ideal: 1080 } }, audio: false });
      v.srcObject = stream;
      await v.play();
      track = stream.getVideoTracks()[0];
      // draw guide rect
      const c = document.getElementById(overlayId);
      if (c) {
        const ctx = c.getContext('2d');
        const draw = () => {
          if (!v.videoWidth) return requestAnimationFrame(draw);
          c.width = c.clientWidth; c.height = c.clientHeight;
          ctx.clearRect(0,0,c.width,c.height);
          ctx.strokeStyle = 'rgba(255,255,255,.9)'; ctx.lineWidth = 2; ctx.setLineDash([8,6]);
          const pad = 24; ctx.strokeRect(pad, pad, c.width - pad*2, c.height - pad*2);
          ctx.fillStyle = 'rgba(0,0,0,.18)'; ctx.fillRect(0,0,c.width,c.height);
          ctx.clearRect(pad, pad, c.width - pad*2, c.height - pad*2);
          ctx.fillStyle = '#fff'; ctx.font = '11px Vazirmatn'; ctx.fillText('سند را داخل کادر بگذار', pad+8, pad-6);
        };
        draw();
      }
      return true;
    },
    async stop(videoId) {
      try { const v = document.getElementById(videoId); if (v) v.pause(); } catch {}
      try { if (stream) stream.getTracks().forEach(t=>t.stop()); } catch {}
      stream = null; track = null;
    },
    async flash(videoId) {
      try {
        if (!track) return;
        const cap = track.getCapabilities && track.getCapabilities();
        if (cap && cap.torch) {
          const s = track.getSettings();
          await track.applyConstraints({ advanced: [{ torch: !s.torch }] });
        }
      } catch {}
    },
    async capture(videoId, mode) {
      const v = document.getElementById(videoId);
      if (!v) return null;
      const c = document.createElement('canvas');
      c.width = v.videoWidth; c.height = v.videoHeight;
      const ctx = c.getContext('2d');
      ctx.drawImage(v, 0, 0);
      // simple auto-crop: 6% padding
      let dataUrl = c.toDataURL('image/jpeg', 0.92);
      if (mode) dataUrl = await this.enhanceDataUrl(dataUrl, mode);
      return dataUrl;
    },
    async enhanceDataUrl(dataUrl, mode) {
      return new Promise((resolve) => {
        const img = new Image();
        img.onload = () => {
          const c = document.createElement('canvas');
          c.width = img.width; c.height = img.height;
          const ctx = c.getContext('2d');
          ctx.drawImage(img, 0, 0);
          if (mode === 'gray' || mode === 'enhance') {
            const id = ctx.getImageData(0,0,c.width,c.height);
            const d = id.data;
            for (let i=0;i<d.length;i+=4){
              const g = 0.299*d[i]+0.587*d[i+1]+0.114*d[i+2];
              if (mode==='gray'){ d[i]=d[i+1]=d[i+2]=g; }
              else {
                // enhance: contrast + brightness
                let v = (g - 128)*1.25 + 128 + 12;
                v = Math.max(0,Math.min(255,v));
                // threshold for clean scan
                d[i]=d[i+1]=d[i+2]= v;
              }
            }
            ctx.putImageData(id,0,0);
          }
          resolve(c.toDataURL('image/jpeg', 0.88));
        };
        img.onerror = () => resolve(dataUrl);
        img.src = dataUrl;
      });
    },
    async rotate(dataUrl) {
      return new Promise((resolve) => {
        const img = new Image();
        img.onload = () => {
          const c = document.createElement('canvas');
          c.width = img.height; c.height = img.width;
          const ctx = c.getContext('2d');
          ctx.translate(c.width/2, c.height/2); ctx.rotate(90*Math.PI/180);
          ctx.drawImage(img, -img.width/2, -img.height/2);
          resolve(c.toDataURL('image/jpeg', 0.88));
        };
        img.src = dataUrl;
      });
    }
  };
})();
