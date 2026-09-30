// Content scripts declarados no manifest só entram em páginas carregadas DEPOIS
// da instalação. Para não obrigar a recarregar o YouTube Music / apps já abertos,
// injeta nas abas existentes na instalação/atualização.
chrome.runtime.onInstalled.addListener(async () => {
  const tabs = await chrome.tabs.query({ url: ['http://*/*', 'https://*/*'] });
  await Promise.allSettled(tabs.map(async (tab) => {
    const target = { tabId: tab.id, allFrames: true };
    await chrome.scripting.executeScript({ target, files: ['content/page.js'], world: 'MAIN', injectImmediately: true });
    await chrome.scripting.executeScript({ target, files: ['content/bridge.js'], injectImmediately: true });
  }));
});
