// Mundo isolado da extensão: lê a configuração do site em chrome.storage e a
// repassa ao page.js (MAIN world). Responde ao popup quando há mídia neste frame.
(() => {
  'use strict';
  // Evita injeção dupla — mas só enquanto a instância anterior está viva. Após
  // atualizar/recarregar a extensão, o bridge antigo fica órfão (chrome.runtime.id
  // some) e precisa ser substituído; senão a guia nunca mais responde ao painel.
  if (globalThis.__volumeMixerBridgeAlive?.()) return;
  globalThis.__volumeMixerBridgeAlive = () => { try { return !!chrome.runtime?.id; } catch { return false; } };

  const EVT_SET = 'volume-mixer-ext:set';
  const EVT_PROBE = 'volume-mixer-ext:probe';
  const EVT_STATE = 'volume-mixer-ext:state';

  // A chave é o site da ABA (frame de topo), não do iframe: um player do
  // YouTube embutido segue o volume do site que o hospeda.
  const site = (() => {
    try {
      const ancestors = location.ancestorOrigins;
      const origin = ancestors && ancestors.length ? ancestors[ancestors.length - 1] : location.origin;
      return new URL(origin).hostname || origin;
    } catch {
      return location.hostname;
    }
  })();
  if (!site) return;

  const DEFAULT = { volume: 100, muted: false };
  let last = '';

  const push = (setting) => {
    const payload = JSON.stringify({ ...DEFAULT, ...(setting || {}) });
    if (payload === last) return;
    last = payload;
    document.dispatchEvent(new CustomEvent(EVT_SET, { detail: payload }));
  };

  chrome.storage.local.get('sites').then(({ sites }) => push(sites?.[site]));

  chrome.storage.onChanged.addListener((changes, area) => {
    if (area !== 'local' || !changes.sites) return;
    push(changes.sites.newValue?.[site]);
  });

  chrome.runtime.onMessage.addListener((msg, _sender, sendResponse) => {
    if (msg?.type !== 'volume-mixer:probe') return false;
    let state = null;
    const onState = (e) => { try { state = JSON.parse(e.detail); } catch { /* ignora */ } };
    document.addEventListener(EVT_STATE, onState, { once: true });
    document.dispatchEvent(new CustomEvent(EVT_PROBE)); // síncrono: page.js responde na hora
    document.removeEventListener(EVT_STATE, onState);
    // Só responde se este frame tem áudio: com vários frames, a primeira
    // resposta vence — frames sem mídia ficam calados para não "ganhar".
    if (!state || (state.media === 0 && state.audioContexts === 0)) return false;
    sendResponse({ site, ...state });
    return false;
  });
})();
