# Volume Mixer — extensão de navegador

Controle de volume **por site/app dentro do navegador** (Chrome e Edge), incluindo
apps instalados (PWA) como YouTube Music e Mastersys Suporte.

## Por que existe

No Windows, todo o áudio do Chrome/Edge sai por **um único processo** (`chrome` /
`msedge`). O mixer do Windows — e o `VolumeMixer.exe` — só conseguem ajustar esse
processo inteiro: baixar o YouTube Music baixa também o som de notificação do app
de suporte. A extensão resolve isso dentro do navegador, site a site.

## Instalação (Chrome ou Edge)

1. Abra `chrome://extensions` (ou `edge://extensions`).
2. Ative o **Modo do desenvolvedor**.
3. Clique em **Carregar sem compactação** e selecione esta pasta `browser-extension`.
4. Fixe o ícone na barra (ícone de quebra-cabeça → alfinete).

As guias já abertas passam a ser controladas na hora. **Exceção:** sites que já
tinham criado um `AudioContext` antes da instalação (alguns sons de notificação)
só passam a obedecer depois de recarregar a página (F5), uma única vez.

## Uso

- Clique no ícone: aparecem os sites com áudio (guias e janelas de app). O ♪ indica
  que o site está tocando agora.
- Slider/caixa numérica: 0–100 %. Botão de alto-falante: mudo.
- ↗ leva até a guia/janela.
- O ajuste é **salvo por site** (ex.: `music.youtube.com` a 30 %) e reaplicado
  automaticamente sempre que o site/app for aberto. Em "Sites salvos" dá para
  restaurar para 100 %.
- O volume do site é **multiplicado** pelo volume do próprio player: o slider do
  YouTube Music continua funcionando normalmente, a extensão só limita o teto.

## Como funciona

| Arquivo | Papel |
|---|---|
| `content/page.js` | Roda no contexto da página. Aplica o fator do site a todo `<audio>`/`<video>` (inclusive `new Audio()`) e a todo `AudioContext` (via `GainNode` no destino). |
| `content/bridge.js` | Lê `chrome.storage.local` e repassa o fator ao `page.js`. Iframes seguem o site da aba. |
| `popup/` | Interface. |
| `background.js` | Na instalação/atualização, injeta os scripts nas guias já abertas. |

## Limitações conhecidas

- Não amplifica acima de 100 %.
- Páginas internas (`chrome://`, loja de extensões, PDF) não permitem extensões.
- Áudio de plugins/processos externos (ex.: app nativo aberto pelo site) continua
  sendo controlado pelo `VolumeMixer.exe`.

## Privacidade (LGPD)

A extensão não coleta, não envia e não registra nada fora do navegador. O único dado
salvo é `{ site: { volume, muted } }` em `chrome.storage.local`, na máquina do usuário.
As permissões `<all_urls>`/`tabs` são necessárias apenas para aplicar o volume em
qualquer site e mostrar título/ícone das guias no painel.
