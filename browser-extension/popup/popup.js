// Popup: lista os sites com áudio (guias e janelas de app/PWA) e grava o volume
// por site em chrome.storage.local { sites: { [hostname]: { volume, muted } } }.
// O bridge.js de cada guia reage à mudança de storage e aplica na hora.

const DEFAULT = { volume: 100, muted: false };
const $ = (sel) => document.querySelector(sel);

const hostOf = (url) => {
  try { return new URL(url).hostname; } catch { return ''; }
};

const loadSites = async () => (await chrome.storage.local.get('sites')).sites ?? {};

// Escrita serializada: cada slider grava o objeto inteiro de sites; sem fila,
// duas gravações rápidas de sites diferentes poderiam se sobrescrever.
let writeChain = Promise.resolve();
const updateSite = (site, patch) => {
  writeChain = writeChain.then(async () => {
    const sites = await loadSites();
    const next = { ...DEFAULT, ...sites[site], ...patch };
    if (next.volume === DEFAULT.volume && !next.muted) delete sites[site]; // padrão: não guarda
    else sites[site] = next;
    await chrome.storage.local.set({ sites });
  });
  return writeChain;
};

// Guias congeladas/descartadas pelo economizador de memória nunca respondem:
// sem timeout, o painel ficava esperando para sempre e "não abria".
const PROBE_TIMEOUT_MS = 350;
const probe = (tab) => {
  if (tab.discarded || tab.frozen || tab.status === 'unloaded') return Promise.resolve(null);
  return Promise.race([
    chrome.tabs.sendMessage(tab.id, { type: 'volume-mixer:probe' }).catch(() => null),
    new Promise((resolve) => setTimeout(() => resolve(null), PROBE_TIMEOUT_MS)),
  ]);
};

async function collect() {
  const [tabs, sites] = await Promise.all([
    chrome.tabs.query({ url: ['http://*/*', 'https://*/*'] }),
    loadSites(),
  ]);
  const states = await Promise.all(tabs.map(probe));

  // Agrupa por site: o volume é por site, então duas guias do mesmo site
  // viram uma linha só.
  const bySite = new Map();
  tabs.forEach((tab, i) => {
    const state = states[i];
    const site = state?.site ?? hostOf(tab.url);
    if (!site) return;
    const hasAudio = tab.audible || !!state;
    if (!hasAudio && !sites[site]) return;
    const entry = bySite.get(site) ?? { site, tabs: [], playing: false, hasAudio: false };
    entry.tabs.push(tab);
    entry.playing ||= tab.audible || (state?.playing ?? 0) > 0;
    entry.hasAudio ||= hasAudio;
    bySite.set(site, entry);
  });

  const rows = [...bySite.values()].sort((a, b) =>
    Number(b.playing) - Number(a.playing) || a.site.localeCompare(b.site));
  return { rows, sites };
}

function renderRow({ site, tabs, playing }, setting) {
  const node = $('#row-tpl').content.firstElementChild.cloneNode(true);
  const tab = tabs.find((t) => t.audible) ?? tabs[0];
  const favicon = node.querySelector('.favicon');
  if (tab.favIconUrl && !tab.favIconUrl.startsWith('chrome://')) favicon.src = tab.favIconUrl;
  else favicon.style.visibility = 'hidden';
  node.querySelector('.title').textContent = tab.title || site;
  node.querySelector('.site').textContent = tabs.length > 1 ? `${site} · ${tabs.length} guias` : site;
  node.querySelector('.playing').hidden = !playing;

  const slider = node.querySelector('.slider');
  const value = node.querySelector('.value');
  const mute = node.querySelector('.mute');

  const paint = ({ volume, muted }) => {
    slider.value = volume;
    value.value = volume;
    node.classList.toggle('is-muted', muted);
    mute.textContent = muted ? '🔇' : volume === 0 ? '🔈' : volume < 50 ? '🔉' : '🔊';
    mute.title = muted ? 'Ativar som' : 'Mudo';
  };
  let current = { ...DEFAULT, ...setting };
  paint(current);

  const setVolume = (v) => {
    const volume = Math.round(Math.min(Math.max(Number(v) || 0, 0), 100));
    current = { ...current, volume, muted: false };
    paint(current);
    updateSite(site, { volume, muted: false });
  };

  slider.addEventListener('input', () => setVolume(slider.value));
  value.addEventListener('change', () => setVolume(value.value));
  value.addEventListener('keydown', (e) => { if (e.key === 'Enter') value.blur(); });
  mute.addEventListener('click', () => {
    current = { ...current, muted: !current.muted };
    paint(current);
    updateSite(site, { muted: current.muted });
  });
  node.querySelector('.focus').addEventListener('click', async () => {
    await chrome.windows.update(tab.windowId, { focused: true });
    await chrome.tabs.update(tab.id, { active: true });
  });
  return node;
}

function renderSaved(sites, openSites) {
  const saved = Object.entries(sites).filter(([site]) => !openSites.has(site));
  $('#saved-wrap').hidden = saved.length === 0;
  $('#saved-count').textContent = String(saved.length);
  const box = $('#saved');
  box.replaceChildren(...saved.map(([site, s]) => {
    const item = document.createElement('div');
    item.className = 'saved-item';
    const label = document.createElement('span');
    label.textContent = `${site} — ${s.muted ? 'mudo' : `${s.volume}%`}`;
    const reset = document.createElement('button');
    reset.type = 'button';
    reset.textContent = 'Restaurar 100%';
    reset.addEventListener('click', async () => {
      await updateSite(site, { ...DEFAULT });
      item.remove();
      const left = box.children.length;
      $('#saved-count').textContent = String(left);
      $('#saved-wrap').hidden = left === 0;
    });
    item.append(label, reset);
    return item;
  }));
}

async function render() {
  const { rows, sites } = await collect();
  $('#list').replaceChildren(...rows.map((r) => renderRow(r, sites[r.site])));
  $('#empty').hidden = rows.length > 0;
  renderSaved(sites, new Set(rows.map((r) => r.site)));
}

render()
  .catch((err) => {
    console.error('Volume Mixer: falha ao montar o painel', err);
    const empty = $('#empty');
    empty.textContent = 'Não foi possível listar as guias. Feche e abra o painel novamente.';
    empty.hidden = false;
  })
  .finally(() => $('#loading').remove());
