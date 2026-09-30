// Roda no "MAIN world" (contexto JS da própria página), antes de qualquer script
// do site. Aplica um fator de volume (0..1) do site a:
//   - todo HTMLMediaElement (<audio>/<video>, inclusive `new Audio()` fora do DOM);
//   - todo AudioContext (Web Audio: sons de notificação, players em canvas, WebRTC).
//
// O site continua enxergando o volume que ELE definiu: o getter de `volume`
// devolve o valor desejado pela página e o elemento toca em `desejado × fator`.
// Assim o slider do YouTube Music, por exemplo, não "pula" nem briga com a extensão.
//
// Comunicação com o bridge.js (mundo isolado) via CustomEvent com payload JSON.
(() => {
  'use strict';
  const INSTALLED = Symbol.for('volume-mixer-ext.installed');
  if (window[INSTALLED]) return; // injeção dupla (manifest + scripting na instalação)
  window[INSTALLED] = true;

  const EVT_SET = 'volume-mixer-ext:set';
  const EVT_PROBE = 'volume-mixer-ext:probe';
  const EVT_STATE = 'volume-mixer-ext:state';

  let factor = 1;

  // ───────── HTMLMediaElement ─────────
  const mediaProto = HTMLMediaElement.prototype;
  const volumeDesc = Object.getOwnPropertyDescriptor(mediaProto, 'volume');
  const realGetVolume = volumeDesc.get;
  const realSetVolume = volumeDesc.set;
  const desired = new WeakMap(); // elemento → volume que a página pediu (0..1)
  const seen = new WeakSet();
  const tracked = new Set(); // WeakRef<HTMLMediaElement>

  const applyTo = (el) => {
    if (!desired.has(el)) desired.set(el, realGetVolume.call(el));
    const target = desired.get(el) * factor;
    if (Math.abs(realGetVolume.call(el) - target) > 1e-4) realSetVolume.call(el, target);
  };

  const track = (el) => {
    if (seen.has(el)) return;
    seen.add(el);
    tracked.add(new WeakRef(el));
    applyTo(el);
  };

  Object.defineProperty(mediaProto, 'volume', {
    configurable: true,
    enumerable: volumeDesc.enumerable,
    get() {
      return desired.has(this) ? desired.get(this) : realGetVolume.call(this);
    },
    set(value) {
      const n = Number(value);
      // Fora de 0..1: delega ao setter nativo para manter o mesmo erro (IndexSizeError).
      if (!(n >= 0 && n <= 1)) return realSetVolume.call(this, value);
      desired.set(this, n);
      if (!seen.has(this)) track(this);
      else realSetVolume.call(this, n * factor);
    },
  });

  const realPlay = mediaProto.play;
  mediaProto.play = function play(...args) {
    track(this);
    return realPlay.apply(this, args);
  };

  // Elementos com autoplay ou tocados sem chamar play() diretamente.
  for (const type of ['play', 'playing', 'loadedmetadata']) {
    document.addEventListener(type, (e) => {
      if (e.target instanceof HTMLMediaElement) track(e.target);
    }, true);
  }

  // ───────── Web Audio ─────────
  // Intercepta `ctx.destination`: devolve um GainNode (criado uma vez por
  // contexto) ligado ao destino real. Tudo que a página conecta ao "destino"
  // passa pelo ganho do site.
  const gains = new WeakMap();
  const contexts = new Set(); // WeakRef<AudioContext>
  const Ctx = window.AudioContext;
  const destDesc = Ctx && Object.getOwnPropertyDescriptor(window.BaseAudioContext.prototype, 'destination');
  if (Ctx && destDesc && destDesc.get) {
    Object.defineProperty(Ctx.prototype, 'destination', {
      configurable: true,
      enumerable: destDesc.enumerable,
      get() {
        let gain = gains.get(this);
        if (!gain) {
          const real = destDesc.get.call(this);
          try {
            gain = new GainNode(this, { gain: factor });
            gain.connect(real);
          } catch {
            return real; // contexto fechado etc.: não interfere
          }
          gains.set(this, gain);
          contexts.add(new WeakRef(this));
        }
        return gain;
      },
    });
  }

  // ───────── Aplicação do fator ─────────
  const applyAll = () => {
    for (const ref of tracked) {
      const el = ref.deref();
      if (!el) { tracked.delete(ref); continue; }
      try { applyTo(el); } catch { /* elemento em estado inválido */ }
    }
    for (const el of document.querySelectorAll('audio, video')) {
      if (!seen.has(el)) track(el);
    }
    for (const ref of contexts) {
      const ctx = ref.deref();
      if (!ctx) { contexts.delete(ref); continue; }
      const gain = gains.get(ctx);
      if (!gain || ctx.state === 'closed') continue;
      try { gain.gain.setTargetAtTime(factor, ctx.currentTime, 0.015); }
      catch { gain.gain.value = factor; }
    }
  };

  document.addEventListener(EVT_SET, (e) => {
    try {
      const { volume, muted } = JSON.parse(e.detail);
      const next = muted ? 0 : Math.min(Math.max(Number(volume) / 100, 0), 1);
      if (Number.isFinite(next) && next !== factor) {
        factor = next;
        applyAll();
      }
    } catch { /* payload inválido: ignora */ }
  });

  document.addEventListener(EVT_PROBE, () => {
    let media = 0;
    let playing = 0;
    for (const ref of tracked) {
      const el = ref.deref();
      if (!el) continue;
      media++;
      if (!el.paused && !el.muted) playing++;
    }
    for (const el of document.querySelectorAll('audio, video')) {
      if (!seen.has(el)) { media++; if (!el.paused && !el.muted) playing++; }
    }
    let audioContexts = 0;
    for (const ref of contexts) {
      const ctx = ref.deref();
      if (ctx && ctx.state !== 'closed') { audioContexts++; if (ctx.state === 'running') playing++; }
    }
    document.dispatchEvent(new CustomEvent(EVT_STATE, {
      detail: JSON.stringify({ media, audioContexts, playing }),
    }));
  });
})();
