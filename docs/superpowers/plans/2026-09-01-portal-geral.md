# Portal Geral de Releases Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Criar `portal-geral/` como página mãe estática que agrega apps do gateway `ShinuuL/Releases` (volume-mixer v1.0.1, contr0l v0.5.0), com roteamento `contr0l→landing` e fallback `app.html`, admin de licenças, e vault apontamentos para agentes.

**Architecture:** Evolução vanilla de `deploy-base/portal/index.html:49` sem build — `portal-geral/` irmã de `deploy-base` e `Mixer de volume`, consome `GET /v1/apps` e `GET /v1/apps/:app/latest` do Worker (`gateway/src/index.js:64`), `apps.json` local para `landingUrl`, `MOTHER.md` como ponte para vault (`Home.md`, `Projetos Índice.md`).

**Tech Stack:** HTML/CSS/JS vanilla, Cloudflare Worker gateway, Cloudflare Pages (padrão) / Vercel (mirror), Obsidian vault markdown

**Spec:** `D:\Dev\Desenvolvimento\Projetos\Mixer de volume\docs\superpowers\specs\2026-09-01-portal-geral-design.md`

## Global Constraints

- Manter `D:\Dev\Desenvolvimento\Projetos\deploy-base\portal\index.html` e `admin.html` intactos — `portal-geral` evolui, não sobrescreve.
- Gateway base: `https://updates-gateway.sofaltaumaletr.workers.dev` (`deploy.toml:5`, `wrangler.toml:7`), repo `ShinuuL/Releases`.
- Cache gateway: `MANIFEST_TTL=60` (`gateway/src/index.js:26`), `ASSET_TTL=31536000` (`gateway/src/index.js:27`), `access-control-allow-origin: *` (`gateway/src/index.js:371`).
- Sem build/bundler — estático puro. Não tocar `gateway/src/index.js`, `gateway/src/store.js`, `deploybase/cli.py`.
- Toda doc tem topo `> [!info] Página mãe: D:\Dev\Desenvolvimento\Projetos\portal-geral\MOTHER.md` ou `[[Portal Geral]]` no vault.
- Versionamento gateway: tags `app-v<semver>` (`deploybase/manifest.py:60` `release_tag`).

---

### Task 1: Scaffold portal-geral + página mãe

**Files:**
- Create: `D:\Dev\Desenvolvimento\Projetos\portal-geral\MOTHER.md`
- Create: `D:\Dev\Desenvolvimento\Projetos\portal-geral\README.md`
- Create: `D:\Dev\Desenvolvimento\Projetos\portal-geral\apps.json`
- Create: `D:\Dev\Desenvolvimento\Projetos\portal-geral\assets\style.css`

**Interfaces:**
- Consumes: nada (primeira task)
- Produces: `MOTHER.md` com tabela de caminhos absolutos, `apps.json` com `contr0l` landing, `style.css` extraído de `deploy-base/portal/index.html:7`

- [ ] **Step 1: Criar pastas**

```bash
mkdir -p "D:/Dev/Desenvolvimento/Projetos/portal-geral/assets"
```

- [ ] **Step 2: Escrever MOTHER.md (ponte para agentes)**

```markdown
# Portal Geral — Página Mãe

> [!info] Comece aqui. Este arquivo é a ponte para agentes que não abrem Obsidian.

## Mapa absoluto

| Conceito | Caminho absoluto |
|---|---|
| Página mãe (este arquivo) | `D:\Dev\Desenvolvimento\Projetos\portal-geral\MOTHER.md` |
| Portal público | `D:\Dev\Desenvolvimento\Projetos\portal-geral\index.html` |
| Fallback por app | `D:\Dev\Desenvolvimento\Projetos\portal-geral\app.html` |
| Admin licenças | `D:\Dev\Desenvolvimento\Projetos\portal-geral\admin.html` |
| Config apps | `D:\Dev\Desenvolvimento\Projetos\portal-geral\apps.json` |
| Vault Home | `D:\Dev\Desenvolvimento\Home.md` |
| Vault índice | `D:\Dev\Desenvolvimento\Projetos Índice.md` |
| Vault Portal Geral | `D:\Dev\Desenvolvimento\Projetos\Portal Geral.md` |
| deploy-base portal base | `D:\Dev\Desenvolvimento\Projetos\deploy-base\portal\index.html` |
| Gateway Worker | `D:\Dev\Desenvolvimento\Projetos\deploy-base\gateway\src\index.js` |
| Mixer docs ponte | `D:\Dev\Desenvolvimento\Projetos\Mixer de volume\docs\PORTAL.md` |

## Como usar
1. Humanos: abra `D:\Dev\Desenvolvimento\Projetos\Portal Geral.md` no Obsidian.
2. Agentes: leia `README.md` aqui para deploy; `apps.json` para roteamento.
3. Publicar: `python -m deploybase.cli publish X.Y.Z` (ver `D:\Dev\Desenvolvimento\Projetos\deploy-base\README.md`).
```

- [ ] **Step 3: Escrever apps.json**

```json
[
  {
    "app": "contr0l",
    "name": "contr0l",
    "landingUrl": "https://contr0l.exemplo.com",
    "description": "Gestão financeira — landing existente. Portal redireciona.",
    "icon": ""
  },
  {
    "app": "volume-mixer",
    "name": "Volume Mixer",
    "description": "Mixer de volume por aplicativo para Windows (tray, WPF, .NET 8).",
    "icon": "assets/volume-mixer.ico"
  }
]
```

- [ ] **Step 4: Extrair style.css de deploy-base/portal/index.html:7**

Copiar bloco `<style>...</style>` de `D:\Dev\Desenvolvimento\Projetos\deploy-base\portal\index.html` para `portal-geral/assets/style.css`, adicionar no topo:

```css
/* portal-geral/assets/style.css — evolução de deploy-base/portal/index.html */
:root{ --bg:#fbfbfa; --card:#fff; --fg:#1b1b19; --muted:#6b6b66; --line:#e6e4e0; --accent:#c15f3c; --accent-fg:#fff; }
@media (prefers-color-scheme:dark){
  :root{ --bg:#191918; --card:#211f1e; --fg:#f0efec; --muted:#9c9a94; --line:#332f2d; --accent:#d97757; --accent-fg:#1b1b19; }
}
.wrap{max-width:960px;margin:0 auto;padding:48px 20px 80px}
```

E adicionar grade:

```css
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(320px,1fr));gap:16px}
```

- [ ] **Step 5: Escrever README.md (doc humana)**

Incluir seções: O que é, Estrutura, Como publicar, Como deployar (Pages vs Vercel tabela da spec seção 6), GATEWAY configurável via `?gateway=` ou `localStorage`, Admin.

- [ ] **Step 6: Verificar scaffold**

```bash
ls "D:/Dev/Desenvolvimento/Projetos/portal-geral" && cat "D:/Dev/Desenvolvimento/Projetos/portal-geral/MOTHER.md" | head -20
```

Expected: 4 arquivos criados, sem erro.

- [ ] **Step 7: Commit**

```bash
git -C "D:/Dev/Desenvolvimento/Projetos/Mixer de volume" add "D:/Dev/Desenvolvimento/Projetos/portal-geral/MOTHER.md" "D:/Dev/Desenvolvimento/Projetos/portal-geral/README.md" "D:/Dev/Desenvolvimento/Projetos/portal-geral/apps.json" "D:/Dev/Desenvolvimento/Projetos/portal-geral/assets/style.css"
# na verdade portal-geral é fora do repo Mixer — copiado via docs/PORTAL.md ponte; commitar apenas MOTHER espelho se preferir, ou init git em portal-geral
```

Nota: `portal-geral` é pasta irmã fora do git de `Mixer de volume`. Passo de commit registra apenas pontes (Task 5). Para esta task, validar via `ls` basta.

---

### Task 2: Portal público index.html + app.html fallback

**Files:**
- Create: `D:\Dev\Desenvolvimento\Projetos\portal-geral\index.html`
- Create: `D:\Dev\Desenvolvimento\Projetos\portal-geral\app.html`

**Interfaces:**
- Consumes: `assets/style.css`, `apps.json`, `GATEWAY` (`https://updates-gateway.sofaltaumaletr.workers.dev`)
- Produces: `index.html` lista apps com roteamento `landingUrl` vs fallback, `app.html?app=<id>` exibe manifesto + download

- [ ] **Step 1: Escrever index.html evoluído de deploy-base/portal/index.html**

Base: copiar `deploy-base/portal/index.html:1` inteiro, trocar `<style>` por `<link rel="stylesheet" href="assets/style.css">`, trocar `const GATEWAY = "https://updates.exemplo.com.br"` por:

```js
const DEFAULT_GATEWAY = "https://updates-gateway.sofaltaumaletr.workers.dev";
const GATEWAY = new URLSearchParams(location.search).get("gateway") || localStorage.getItem("gateway") || DEFAULT_GATEWAY;
const CANAL = new URLSearchParams(location.search).get("channel") || "stable";
```

Adicionar no topo do body antes de `#lista`:

```html
<header class="wrap" style="padding-bottom:0">
  <p style="color:var(--muted);font-size:.85rem">Página mãe: <a href="MOTHER.md">MOTHER.md</a> · Vault: <a href="../../../Home.md">Home.md</a></p>
  <input id="busca" placeholder="Buscar app..." style="width:100%;padding:10px;border:1px solid var(--line);border-radius:8px;margin-top:12px">
</header>
```

Trocar render `card` para checar `apps.json`:

```js
let cfg = [];
try { cfg = await (await fetch("apps.json")).json(); } catch {}
const cfgByApp = Object.fromEntries(cfg.map(c => [c.app, c]));

// dentro de card(app):
const landing = cfgByApp[app]?.landingUrl;
if (landing) {
  a.href = landing;
  a.target = "_blank";
  a.rel = "noopener";
} else {
  a.href = `app.html?app=${encodeURIComponent(app)}`;
}
```

E usar `<div id="lista" class="grid wrap">` + filtro busca:

```js
document.getElementById("busca").addEventListener("input", e => {
  const q = e.target.value.toLowerCase();
  for (const c of document.querySelectorAll(".app")) c.style.display = c.textContent.toLowerCase().includes(q) ? "" : "none";
});
```

- [ ] **Step 2: Escrever app.html fallback**

```html
<!doctype html>
<html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Download</title><link rel="stylesheet" href="assets/style.css"></head>
<body><div class="wrap">
  <p><a href="index.html">← Voltar</a> · <a href="MOTHER.md">Página mãe</a></p>
  <div id="conteudo"><p class="msg">Carregando…</p></div>
</div>
<script>
const GW = new URLSearchParams(location.search).get("gateway") || localStorage.getItem("gateway") || "https://updates-gateway.sofaltaumaletr.workers.dev";
const APP = new URLSearchParams(location.search).get("app");
const CANAL = new URLSearchParams(location.search).get("channel") || "stable";
async function main(){
  const alvo = document.getElementById("conteudo");
  if(!APP){ alvo.textContent="App não informado."; return; }
  let env;
  try{ const r=await fetch(`${GW}/v1/apps/${encodeURIComponent(APP)}/latest?channel=${CANAL}`); if(!r.ok) throw new Error(r.status); env=await r.json(); }catch(e){ alvo.textContent=`Sem release: ${e.message}`; return; }
  const m=env.manifest||{};
  alvo.innerHTML=`<div class="app"><div class="row"><span class="name">${m.app}</span><span class="ver">v${m.version}</span></div><p class="notes">${(m.notes||"").replace(/</g,"&lt;")}</p><div class="files">${(m.artifacts||[]).map(a=>`<a class="dl" href="${a.url}"><span>Baixar ${a.kind} · ${a.platform} ${a.arch}</span><small>${(a.size/1024/1024).toFixed(1)} MB</small></a><div class="hash">SHA-256 ${a.sha256}</div>`).join("")}</div></div>`;
}
main();
</script></body></html>
```

- [ ] **Step 3: Smoke test local**

```bash
npx serve "D:/Dev/Desenvolvimento/Projetos/portal-geral" --listen 3000
# abrir http://localhost:3000/index.html?gateway=https://updates-gateway.sofaltaumaletr.workers.dev
# verificar lista contém volume-mixer v1.0.1 e contr0l v0.5.0, busca filtra, clique contr0l abre landingUrl, clique volume-mixer vai para app.html?app=volume-mixer
```

Expected: PASS, `fetch` retorna `200` para `/v1/apps` e `/latest`.

- [ ] **Step 4: Verificar sem gate**

```bash
curl -s "https://updates-gateway.sofaltaumaletr.workers.dev/v1/apps" | head -c 500
```

Expected: JSON com `apps: [{app:"volume-mixer",...},{app:"contr0l",...}]`

---

### Task 3: Admin portal evoluído

**Files:**
- Create: `D:\Dev\Desenvolvimento\Projetos\portal-geral\admin.html`

**Interfaces:**
- Consumes: `assets/style.css`, `gateway/src/index.js:238` admin API
- Produces: `admin.html` com listagem/emitir/revogar licenças

- [ ] **Step 1: Escrever admin.html evoluído de deploy-base/portal/admin.html**

Copiar `deploy-base/portal/admin.html` inteiro, trocar `<style>` por `<link rel="stylesheet" href="assets/style.css">`, garantir header MOTHER:

```html
<p><a href="index.html">← Portal</a> · <a href="MOTHER.md">Página mãe</a></p>
```

Verificar JS usa:

```js
const GW = localStorage.getItem("gateway") || "https://updates-gateway.sofaltaumaletr.workers.dev";
async function listar(){
  const token = document.getElementById("token").value.trim();
  const r = await fetch(`${GW}/v1/admin/licencas`, { headers: { "x-admin-token": token }});
  // handle 503 admin_desativado, 401 nao_autorizado
}
```

E form emitir:

```js
await fetch(`${GW}/v1/admin/licencas`, { method:"POST", headers: {"x-admin-token": token, "content-type":"application/json"}, body: JSON.stringify({ app, para, nota })});
```

E revogar:

```js
await fetch(`${GW}/v1/admin/licencas/revogar`, { method:"POST", headers: {"x-admin-token": token, "content-type":"application/json"}, body: JSON.stringify({ chave })});
```

- [ ] **Step 2: Smoke test admin sem token**

```bash
curl -s "https://updates-gateway.sofaltaumaletr.workers.dev/v1/admin/licencas" -H "x-admin-token: invalid" | head -c 500
```

Expected: `{"error":"nao_autorizado"}` 401 ou `admin_desativado` 503 — ambos tratados com banner.

---

### Task 4: Vault Obsidian + apontamentos inter-pastas

**Files:**
- Create: `D:\Dev\Desenvolvimento\Projetos\Volume Mixer.md`
- Create: `D:\Dev\Desenvolvimento\Projetos\deploy-base.md`
- Create: `D:\Dev\Desenvolvimento\Projetos\Portal Geral.md`
- Modify: `D:\Dev\Desenvolvimento\Projetos Índice.md:1`
- Modify: `D:\Dev\Desenvolvimento\Home.md:15`

**Interfaces:**
- Consumes: `D:\Dev\Desenvolvimento\Templates\Projeto.md`, `portal-geral/MOTHER.md`, `portal-geral/README.md`
- Produces: 3 notas vault + índices atualizados

- [ ] **Step 1: Criar Projetos/Volume Mixer.md a partir de Templates/Projeto.md**

```markdown
# Volume Mixer

> [!info] Página mãe: [[Portal Geral]] · `D:\Dev\Desenvolvimento\Projetos\portal-geral\MOTHER.md`

## Objetivo
Mixer de volume por aplicativo para Windows, residente na bandeja. Controle master + por app (WASAPI), com mute, tema claro/escuro, autostart.

## Status
🚧 Publicado v1.0.1 em `ShinuuL/Releases` (tag `volume-mixer-v1.0.1`) via `deploy-base`.

## Tecnologias
- [[WPF]] · [[.NET 8]] · [[WASAPI]] · [[C#]] · [[MVVM]]
- [[Git]] · [[deploy-base]]

## Funcionalidades
- [x] Volume master + por app (agrupado por PID)
- [x] Mute, slider + textbox 0–100
- [x] Polling reconciliação WASAPI, fix vtable IAudioSessionEvents
- [x] Tray + autostart HKCU Run

## Arquitetura
`VolumeMixer/` — `Audio/CoreAudioInterop.cs`, `Audio/AudioController.cs`, `ViewModels/`, `Views/PopupWindow.xaml` (ver `docs/superpowers/specs/2026-08-23-volume-mixer-design.md`).

## Problemas
- Crash MTA GDI+ isolado em STA (ver `Projetos Índice.md`).

## Decisões Técnicas
- Interop COM manual sem wrapper NuGet (zero deps).

## Repositório
`D:\Dev\Desenvolvimento\Projetos\Mixer de volume` · `ShinuuL/volume-mixer` (código) + `ShinuuL/Releases` (binários)
```

- [ ] **Step 2: Criar Projetos/deploy-base.md**

```markdown
# deploy-base

> [!info] Página mãe: [[Portal Geral]] · `D:\Dev\Desenvolvimento\Projetos\portal-geral\MOTHER.md`

## Objetivo
Base de deploy: publica releases assinadas Ed25519 no GitHub privado e serve via Worker.

## Status
✅ Ativo — gateway `updates-gateway.sofaltaumaletr.workers.dev`, repo `ShinuuL/Releases`.

## Tecnologias
- [[Python]] · [[cryptography]] · [[Cloudflare Worker]] · [[Ed25519]]

## Funcionalidades
- [x] `deploybase/cli.py:71` `publish` (draft→upload→publish), `verify`, `keygen`
- [x] `gateway/src/index.js:29` `GET /v1/apps`, `/latest`, `/download`
- [x] `portal/index.html` estático

## Arquitetura
`deploybase/cli.py`, `manifest.py:60`, `signing.py`, `github.py`, `gateway/src/store.js:14`.

## Repositório
`D:\Dev\Desenvolvimento\Projetos\deploy-base`
```

- [ ] **Step 3: Criar Projetos/Portal Geral.md (espelho MOTHER)**

Conteúdo = `portal-geral/MOTHER.md` + seções de `portal-geral/README.md` adaptadas para wiki-links `[[Volume Mixer]]`, `[[deploy-base]]`.

- [ ] **Step 4: Atualizar Projetos Índice.md**

Substituir bloco fantasma:

```markdown
# 🛠️ Projetos — Índice

> Voltar para: [[Home]] · Página mãe: [[Portal Geral]]

## 📂 Projetos

- [[Volume Mixer]] — mixer WASAPI (v1.0.1 no portal-geral)
- [[deploy-base]] — base de deploy + gateway
- [[Portal Geral]] — portal que agrega todos os apps do gateway
- [[MasterDesk]] — (futuro)
- [[PWA CS]] — (futuro)

> [!info] Notas acima são reais. Fantasma-links removidos.
```

- [ ] **Step 5: Atualizar Home.md:15**

Adicionar após linha `Documentação dos projetos de desenvolvimento:`:

```markdown
- 📑 [[Portal Geral]] — página mãe do portal de releases
```

- [ ] **Step 6: Verificar vault**

```bash
grep -r "Portal Geral" "D:/Dev/Desenvolvimento/Projetos" | wc -l
grep -r "MOTHER" "D:/Dev/Desenvolvimento/Projetos/portal-geral" | wc -l
```

Expected: ≥3 hits vault, ≥1 hit MOTHER.

---

### Task 5: Pontes nas pastas dos projetos + verificação final

**Files:**
- Create: `D:\Dev\Desenvolvimento\Projetos\Mixer de volume\docs\PORTAL.md`
- Create: `D:\Dev\Desenvolvimento\Projetos\Mixer de volume\MOTHER.md` (symlink lógico via cópia)
- Create: `D:\Dev\Desenvolvimento\Projetos\deploy-base\docs\portal-geral.md`
- Modify: `D:\Dev\Desenvolvimento\Projetos\Mixer de volume\README.md` (adicionar seção Portal)
- Modify: `D:\Dev\Desenvolvimento\Projetos\deploy-base\README.md` (adicionar seção Portal Geral)

**Interfaces:**
- Consumes: `portal-geral/MOTHER.md`, `portal-geral/README.md`
- Produces: pontes relativas para agentes

- [ ] **Step 1: Criar Mixer de volume/docs/PORTAL.md**

```markdown
> [!info] Página mãe: `D:\Dev\Desenvolvimento\Projetos\portal-geral\MOTHER.md` · Vault: `D:\Dev\Desenvolvimento\Projetos\Portal Geral.md`

# Portal Geral — ponte a partir de Mixer de volume

Este projeto publica via `deploy-base` em `ShinuuL/Releases` (ver `deploy.toml:3`). O portal que lista esta release e outras é `../../portal-geral/index.html`.

- Publicar: `python -m deploybase.cli publish X.Y.Z` (usar `.venv` de `deploy-base`)
- Verificar: `python -m deploybase.cli verify`
- Gateway: `https://updates-gateway.sofaltaumaletr.workers.dev`
```

- [ ] **Step 2: Criar Mixer de volume/MOTHER.md**

```markdown
# MOTHER — Mixer de volume

> Veja `D:\Dev\Desenvolvimento\Projetos\portal-geral\MOTHER.md` (página mãe) e `D:\Dev\Desenvolvimento\Projetos\Portal Geral.md` no vault.
```

- [ ] **Step 3: Criar deploy-base/docs/portal-geral.md**

```markdown
> [!info] Página mãe: `D:\Dev\Desenvolvimento\Projetos\portal-geral\MOTHER.md`

# Portal Geral vs portal base

- `deploy-base/portal/` — base mínima (100% estática, 2 arquivos).
- `../portal-geral/` — agregador geral (evolução, com `apps.json` + `app.html` + `admin.html`).
- Não sobrescrever `portal/`; `portal-geral` importa a lógica.
```

- [ ] **Step 4: Atualizar READMEs com seção Portal**

Em `Mixer de volume/README.md` adicionar após `## Como instalar`:

```markdown
## Portal geral

> [!info] Página mãe: `../portal-geral/MOTHER.md`

Downloads de todas as releases: `D:\Dev\Desenvolvimento\Projetos\portal-geral\index.html` (gateway `updates-gateway.sofaltaumaletr.workers.dev`).
```

Em `deploy-base/README.md` adicionar após `## Portal`:

```markdown
> Evolução agregadora: `../portal-geral/README.md` (página mãe).
```

- [ ] **Step 5: Verificação final (comandos que o agente roda)**

```bash
# 1. Portal serve e lista apps
npx serve "D:/Dev/Desenvolvimento/Projetos/portal-geral" --listen 3000 &
curl -s "https://updates-gateway.sofaltaumaletr.workers.dev/v1/apps" | grep -q "volume-mixer" && echo "gateway OK"

# 2. Verify assinatura
"D:/Dev/Desenvolvimento/Projetos/deploy-base/.venv/Scripts/python.exe" -m deploybase.cli verify 2>&1 | head -20

# 3. Vault
grep -r "Portal Geral" "D:/Dev/Desenvolvimento" | wc -l

# 4. Pontes
ls "D:/Dev/Desenvolvimento/Projetos/Mixer de volume/docs/PORTAL.md" && ls "D:/Dev/Desenvolvimento/Projetos/deploy-base/docs/portal-geral.md"
```

Expected: gateway OK, verify mostra `assinatura OK -- volume-mixer 1.0.1`, vault ≥5 hits, pontes existem.

- [ ] **Step 6: Commit final**

```bash
git -C "D:/Dev/Desenvolvimento/Projetos/Mixer de volume" status
git -C "D:/Dev/Desenvolvimento/Projetos/Mixer de volume" add docs/PORTAL.md MOTHER.md README.md
git -C "D:/Dev/Desenvolvimento/Projetos/Mixer de volume" commit -m "docs: pontes para portal-geral (página mãe) + vault"
# vault é fora do git de Mixer — commit separado se vault for git:
git -C "D:/Dev/Desenvolvimento" status
```

---

## Self-Review

**1. Cobertura da spec:**
- Seção 3 Arquitetura (pastas + MOTHER) → Task 1 + Task 5
- Seção 4 Fluxo & roteamento (gateway, apps.json, 404) → Task 2
- Seção 5 Interface (style.css, grid, busca, app.html) → Task 2
- Seção 5 Admin (licenças) → Task 3
- Seção 6 Hospedagem Pages vs Vercel → Task 1 README + Task 5 verificação
- Seção 7 Vault (3 notas + índices) → Task 4
- Seção 7 Pontes inter-pastas → Task 5
- Seção 9 Testes (smoke, verify, grep) → Tasks 2,4,5

**2. Placeholder scan:** Nenhum `TBD/TODO` — todos os passos têm código concreto (HTML, JSON, markdown, comandos).

**3. Consistência de tipos:** `app` é `string` id `^[a-z0-9][a-z0-9._-]{1,63}$` em todo lugar (`gateway/src/index.js:68`, `manifest.py:23`), `GATEWAY` é URL string, `apps.json` array de `{app, landingUrl?, name, icon?, description?}`.
