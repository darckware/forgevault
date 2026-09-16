# Deploy via Cloudflare (Web only, atrás de Cloudflare Access)

## Contexto

Este host já roda um Cloudflare Tunnel compartilhado com os projetos irmãos
(`darckware.net`, `forgehub.darckware.net`, `forgerouter.darckware.net`,
`ssh.darckware.net`, `remoto.darckware.net`), gerenciado como serviço systemd
(`cloudflared.service`) via `cloudflared tunnel run --token-file /etc/cloudflared/token`.

Esse modo de token **não usa um `config.yml` local** — o `ingress` (quais hostnames
apontam para qual `service`) é puxado remotamente do dashboard da Cloudflare
(Zero Trust → Networks → Tunnels → *este túnel* → aba **Public Hostname**). Não há
credencial de API da Cloudflare disponível neste ambiente para automatizar isso, e
alterar esse túnel afeta hostnames de produção de outros projetos — por isso os passos
abaixo são feitos manualmente no dashboard, não por script neste repo.

**Histórico relevante (ver `docker-compose.yml` e
`docs/architecture/IMPLEMENTATION_READINESS.md`)**: este projeto já teve duas exposições
públicas acidentais via Cloudflare Tunnel (API na porta 8080 e Web na porta 4200), ambas
sinalizadas como incidente por ser um secrets manager. Por isso `api` e `web` no
`docker-compose.yml` estão vinculados só a `127.0.0.1`, e a decisão para este deploy é
expor **apenas o Web**, **atrás de Cloudflare Access** (Zero Trust) — nunca a API
diretamente, e nunca o Web sem um gate de autenticação na borda.

> Pendência conhecida e ainda não confirmada como corrigida: a rota pública antiga para a
> porta 8080 (API). Ao mexer no dashboard para os passos abaixo, confira em Public
> Hostname se essa rota ainda existe e remova-a caso sim.

## Porta a configurar

`web` já expõe `127.0.0.1:4200` (mapeado para a porta 80 do container nginx) —
ver `docker-compose.yml`. É esse `127.0.0.1:4200` que deve ser usado como `service` da
regra de ingress abaixo. A API (`127.0.0.1:8080`) não deve ganhar rota pública.

## Passo 1 — Public Hostname (Networks → Tunnels)

No dashboard Cloudflare Zero Trust, no túnel já existente:

| Campo | Valor |
|---|---|
| Subdomain | `forgevault` |
| Domain | `darckware.net` |
| Path | (vazio) |
| Service Type | `HTTP` |
| URL | `127.0.0.1:4200` |

Resultado: `https://forgevault.darckware.net` → nginx do container `web` → proxy interno
`/api/` para o container `api` (sem CORS, sem hostname público direto na API — mesmo
desenho do serviço `web` no `docker-compose.yml`).

## Passo 2 — Cloudflare Access (obrigatório, não pular)

Em Zero Trust → Access → Applications → **Add an application → Self-hosted**:

| Campo | Valor |
|---|---|
| Application domain | `forgevault.darckware.net` |
| Session duration | conforme política interna (ex.: 24h) |
| Policy | *Allow*, restrita — ex.: regra de e-mail/domínio de e-mail da organização, ou o IdP/login method já usado nos outros apps Access deste tenant |

Sem essa política, o hostname público fica só atrás do login da própria aplicação
(ForgeVault), que é exatamente o cenário já sinalizado como exposição indevida de um
secrets manager. O Access acrescenta um gate de autenticação na borda, antes de qualquer
requisição chegar ao nginx/API.

## Passo 3 — reCAPTCHA (se `RECAPTCHA_SITE_KEY`/`RECAPTCHA_SECRET_KEY` estiverem em uso)

Conforme `.env.example`, adicionar `forgevault.darckware.net` na lista de domínios
permitidos em https://www.google.com/recaptcha/admin — ao lado do hostname Tailscale
já configurado.

## Depois de configurado

- `https://forgevault.darckware.net` exige Cloudflare Access antes do login do
  ForgeVault.
- O acesso via Tailscale Service (`127.0.0.1:4200` → `forgevault.<tailnet>.ts.net`)
  continua funcionando sem alteração — são duas portas de entrada para o mesmo serviço.
- A API nunca ganha hostname público; ela só é alcançada via rede interna do
  docker-compose (`api:8080`) ou `127.0.0.1:8080` no host.
