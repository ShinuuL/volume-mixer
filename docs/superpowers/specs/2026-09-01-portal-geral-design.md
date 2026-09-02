# Especificação — Portal Geral de Releases (gateway + vault)

**Data:** 2026-09-01
**Status:** Aprovada — abordagem A (estático vanilla evoluindo deploy-base/portal)
**Página mãe para agentes:** `D:\Dev\Desenvolvimento\Projetos\portal-geral\MOTHER.md` (espelho vault: `Projetos/Portal Geral.md`)

## 1. Visão geral

Portal geral que agrega todos os apps publicados via `deploy-base` no repo privado `ShinuuL/Releases` (`deploy.toml:4`, `gateway/wrangler.toml:7`). Evolui o `deploy-base/portal/index.html:49` (100% estático) para listar `volume-mixer` (v1.0.1), `contr0l` (v0.5.0) e futuros apps, com regra de roteamento: `contr0l` → landing page existente, demais sem landing → `app.html` fallback de download simples.

- **Stack portal:** HTML + CSS + JS vanilla (sem build), reutilizando `fetch` de `gateway/src/index.js:64`.
- **Gateway:** Worker Cloudflare `updates-gateway.sofaltaumaletr.workers.dev` (`deploy.toml:5`) — `GET /v1/apps`, `GET /v1/apps/:app/latest?channel=`, `GET /v1/apps/:app/download/:version/:file` (`gateway/src/index.js:29`).
- **Vault:** Obsidian em `D:\Dev\Desenvolvimento\` (`Home.md`, `Projetos Índice.md`, `Templates/Projeto.md`) como fonte humana; `portal-geral/MOTHER.md` como ponte para agentes que não abrem Obsidian.

## 2. Objetivos e não-objetivos

### Objetivos
1. Criar `D:\Dev\Desenvolvimento\Projetos\portal-geral\` como **página mãe** (evolução do portal, não substituição de `deploy-base/portal/`).
2. Listagem pública: grade de apps publicados (nome, versão, `released_at`, `notes`, artefatos com `size`, `sha256`, `url` assinada).
3. Roteamento: `apps.json` mapeia `app → landingUrl`; `contr0l` redireciona, fallback vai para `app.html?app=<id>`.
4. Admin: evoluir `deploy-base/portal/admin.html:1` para `portal-geral/admin.html` (listar/emitir/revogar licenças via `POST /v1/admin/licencas` com `x-admin-token`, `gateway/src/index.js:238`).
5. Vault: criar/atualizar `Projetos/Volume Mixer.md`, `Projetos/deploy-base.md`, `Projetos/Portal Geral.md` a partir de `Templates/Projeto.md`, atualizar `Projetos Índice.md:8` (remover fantasma-links) e `Home.md:15`, criar apontamentos em `Mixer de volume/docs/PORTAL.md` e `deploy-base/docs/portal-geral.md` apontando para a mãe.
6. Documentar hospedagem: Pages vs Vercel com limites e comandos (`wrangler pages deploy` / `vercel --prod`).

### Não-objetivos
- Reescrever gateway (`gateway/src/index.js`, `store.js`) ou CLI (`deploybase/cli.py:71`).
- SPA com build (Vite/Next) nesta fase — fica como upgrade se catálogo >10 apps.
- Autenticação de usuário final no portal (só `x-license-key` opcional para `PAID_APPS` em `wrangler.toml:14`).
- Migrar `deploy-base/portal/` — mantido como base mínima.

## 3. Arquitetura

```
D:\Dev\Desenvolvimento\
├── .obsidian/
├── Home.md
├── Projetos Índice.md          # atualizado
├── Projetos/
│   ├── Volume Mixer.md         # novo/atualizado
│   ├── deploy-base.md          # novo/atualizado
│   └── Portal Geral.md         # espelho da MOTHER
├── Projetos/
│   ├── deploy-base/
│   │   ├── portal/index.html + admin.html  # mantidos
│   │   └── docs/portal-geral.md            # ponte → ../../portal-geral/README.md
│   ├── Mixer de volume/
│   │   ├── deploy.toml
│   │   ├── docs/PORTAL.md + MOTHER.md      # ponte → ../../portal-geral/MOTHER.md
│   │   └── docs/superpowers/specs/2026-09-01-portal-geral-design.md (este arquivo)
│   └── portal-geral/           # NOVO — página mãe
│       ├── MOTHER.md
│       ├── README.md
│       ├── index.html          # público (evolução de deploy-base/portal/index.html)
│       ├── app.html            # fallback por app
│       ├── admin.html          # evolução de deploy-base/portal/admin.html
│       ├── apps.json           # [{app, landingUrl, name, icon, description}]
│       └── assets/style.css    # extraído de <style> de index.html:7
```

Regra: toda doc tem no topo `> [!info] Página mãe: D:\Dev\Desenvolvimento\Projetos\portal-geral\MOTHER.md` + `[[Portal Geral]]` quando em vault.

## 4. Fluxo de dados & roteamento

### Leitura pública (sem auth)
- `index.html` → `GET {GATEWAY}/v1/apps` → `listApps` (`gateway/src/index.js:131` filtra `tag_name` `app-v<semver>`, pega maior versão por app, `cache max-age 60`).
- Para cada app (união de `apps.json` + resposta), `GET /v1/apps/:app/latest?channel=stable` (`gateway/src/index.js:72`) retorna `envelope {manifest, signature}`. Portal exibe `manifest.version`, `released_at`, `notes`, `artifacts[] {filename, size, sha256, url}` (`deploybase/cli.py:30` gera `.../download/:version/:file`). Portal **não verifica** assinatura (cliente verifica via `deploybase/updater.py:38`).
- Helpers: `bytes(n)` e `el(tag,cls,txt)` já existem em `deploy-base/portal/index.html:55`.

### Roteamento
- `apps.json` exemplo:
  ```json
  [
    { "app": "contr0l", "landingUrl": "https://contr0l.exemplo.com", "name": "contr0l" },
    { "app": "volume-mixer", "name": "Volume Mixer", "icon": "assets/volume-mixer.ico" }
  ]
  ```
- Clique: se `landingUrl` existe → `location.href = landingUrl`; senão → `location.href = app.html?app=volume-mixer` que refaz `latest` e mostra botão `Baixar` + `SHA-256` + link `Como instalar` (`Mixer de volume/README.md:30`).
- `404 no_release` (`gateway/src/index.js:170`) → card exibe "Sem release neste canal".

### Admin (privado)
- `admin.html` → `GET /v1/admin/licencas` + `POST /v1/admin/licencas` e `POST /v1/admin/licencas/revogar` com `x-admin-token` (`gateway/src/index.js:238`). Sem `ADMIN_TOKEN` → `503 admin_desativado` → banner "admin desativado".

### Erros & cache
- `502 upstream_error` ou `403` (token GitHub expirado) → banner retry como em `portal/index.html:76`.
- Binários `immutable` (`ASSET_TTL 31536000`, `gateway/src/index.js:27`) → `app.html` usa `cache-control immutable`.

## 5. Interface

### Público (`index.html` → `assets/style.css`)
- Extrair `<style>` de `deploy-base/portal/index.html:7` para `assets/style.css`, manter variáveis `--bg/--card/--accent` e dark mode.
- Grade responsiva (grid 1→3), header com busca `?q=` e filtro `channel`, footer com link MOTHER.
- Acessibilidade: `a.dl` com `aria-label`, botão copiar `sha256`, `notes` com `pre-wrap` e `max-height`.
- Router leve: `history.pushState` para `app.html` sem recarregar lista; sem framework.

### Fallback (`app.html`)
- Título `m.app vX.Y.Z`, `released_at` local, `notes`, lista `artifacts` com `href=art.url`, `bytes(size)`, `SHA-256`.

### Admin (`admin.html`)
- Lista KV `LICENSES`, form emitir (`app`, `para`, `nota`), botão revogar. Reusa `segredoConfere` constant-time (`gateway/src/index.js:231`).

## 6. Hospedagem — Pages vs Vercel

| Critério | Cloudflare Pages | Vercel Hobby |
|---|---|---|
| Build | estático, `npx wrangler pages deploy portal-geral --project-name portal-geral` | estático, `npx vercel --prod --cwd portal-geral` |
| Banda | ilimitada (fair use) | 100 GB/mês |
| Builds/mês | 500 | 6000 min |
| Domínio | grátis, co-localizado com Worker | grátis |
| CDN | mesmo CDN do Worker (`caches.default` em `gateway/src/index.js:109`) | CDN separado |
| CORS | já `access-control-allow-origin: *` (`gateway/src/index.js:371`) | idem |

**Recomendação:** Pages como padrão (mesmo `wrangler.toml:1`, 1 comando). Vercel como mirror — portal é estático, roda em ambos. `GATEWAY` configurável via `?gateway=` ou `localStorage`. Documentado em `portal-geral/README.md`.

## 7. Vault & apontamentos inter-pastas

- `Projetos/Volume Mixer.md`, `Projetos/deploy-base.md`, `Projetos/Portal Geral.md` criados de `Templates/Projeto.md` com links `[[Portal Geral]]`, `[[deploy-base]]`, `[[Volume Mixer]]`, `[[Git]]`, `[[WASAPI]]`.
- `Projetos Índice.md:8` atualizado: remover fantasma-info, listar reais com descrição + `> [!info] Página mãe: [[Portal Geral]]`.
- `Home.md:15` adiciona `[[Portal Geral]]`.
- `portal-geral/MOTHER.md` — mapa absoluto para agentes: tabela `Conceito → Caminho absoluto`.
- `Mixer de volume/docs/PORTAL.md` + `MOTHER.md` → `> Veja ../../portal-geral/MOTHER.md`.
- `deploy-base/docs/portal-geral.md` → aponta para `../../portal-geral/README.md`, explica `portal/` vs `portal-geral`.
- `portal-geral/README.md` — doc humana completa (o que é, como publicar `python -m deploybase.cli publish`, como deployar).

Garantia: `grep -r "MOTHER"` acha a mãe em <2 níveis.

## 8. Tratamento de erros

- Toda chamada `fetch` com try/catch por app; falha isolada não derruba grade.
- `GATEWAY` ausente/inválido → mensagem "Configure GATEWAY em apps.json ou ?gateway=".
- `admin.html` sem token → 401 `nao_autorizado` → prompt para inserir `x-admin-token`.

## 9. Testes

- Smoke manual: `npx serve portal-geral` + fixture `GET /v1/apps` com `volume-mixer`/`contr0l`, checar roteamento `contr0l`→landing e fallback.
- Gateway: `gateway/src/*.test.js` (vitest) não quebrado — portal só consome `listApps/latest`.
- Vault: `grep -r "\[\[Portal Geral\]\]" Projetos/` ≥3 hits; `Projetos Índice.md` sem "fantasma".
- Verify: `python -m deploybase.cli verify --config portal-geral/apps.json` valida assinatura `ed25519` do manifesto v1.0.1; `gh release list --repo ShinuuL/Releases` confirma tags.

## 10. Entrega

- Commit único: `feat(portal-geral): portal geral evolutivo + página mãe + vault` cobrindo `portal-geral/` + notas vault + pontes em `Mixer de volume` e `deploy-base`.
- Sem `dotnet publish`; portal é estático.
- Deploy opcional documentado, não obrigatório no spec.

## 11. Decisões registradas

| Decisão | Alternativa descartada | Motivo |
|---|---|---|
| Estático vanilla evoluindo `portal/index.html` | Vite/Next | YAGNI, zero build, agentes entendem 1 arquivo, roda Pages e Vercel |
| `apps.json` local para `landingUrl` | `wrangler.toml:7` | desacopla portal do Worker, edita sem deploy do gateway |
| `portal-geral` como pasta irmã em `Projetos/` | dentro de `deploy-base` | página mãe neutra, visível para todos os projetos |
| Pages como padrão | Vercel-only | co-localização com Worker, banda ilimitada |
| Manter `deploy-base/portal/` | Sobrescrever | base mínima permanece como fallback |
