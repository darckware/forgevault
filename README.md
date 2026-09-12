<p align="center">
  <img src="docs/assets/forgevault-icon.svg" width="96" height="96" alt="ForgeVault">
</p>

<h1 align="center">ForgeVault</h1>

<p align="center">
  Cofre central de credenciais e secrets do ecossistema Darckware.<br>
  A <em>Security Plane</em> compartilhada consumida por <strong>ForgeHub</strong>, <strong>ForgeRouter</strong>, Hermes e demais agentes/serviços.
</p>

<p align="center">
  <a href="https://github.com/marcelodarckferreira/forgevault/actions/workflows/ci.yml"><img src="https://github.com/marcelodarckferreira/forgevault/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
  <img src="https://img.shields.io/badge/PostgreSQL-17-336791" alt="PostgreSQL 17">
  <img src="https://img.shields.io/badge/status-Onda%201%20(MVP)%20%2B%20M8-brightgreen" alt="Status">
  <img src="https://img.shields.io/badge/license-proprietary-lightgrey" alt="License">
</p>

---

## O que é o ForgeVault

Nenhum agente, serviço ou humano deveria precisar hardcodar uma API key, uma senha de banco
ou um token de terceiro para operar dentro do ecossistema Darckware. O ForgeVault existe para
resolver exatamente isso: um cofre central de credenciais com **envelope encryption**, **RBAC
hierárquico**, **auditoria à prova de vazamento** e integração nativa — via REST e via
**MCP Server** — para que ForgeHub, ForgeRouter e agentes autônomos obtenham credenciais sob
demanda, sem que elas fiquem espalhadas em `.env`, repositórios ou configs.

## Principais capacidades

| Capacidade | Descrição |
|---|---|
| **Envelope encryption** | AES-256-GCM por versão de secret, DEK protegido por uma Master Key (KEK) via provider plugável (`IKeyManagementProvider`) — trocar para um KMS/HSM real é uma troca de implementação, não de contrato |
| **Versionamento imutável** | toda escrita cria uma nova `SecretVersion`; nada é sobrescrito, versões anteriores continuam legíveis |
| **RBAC hierárquico** | `Organization → Project → Environment`, com herança de papel para baixo na hierarquia e uma matriz papel→permissão explícita |
| **Auditoria à prova de vazamento** | toda leitura/escrita — inclusive negações — gera um `AuditLog`; testado ativamente para garantir que nenhum valor de secret apareça em log de aplicação ou de auditoria |
| **Autenticação dupla** | JWT (humanos, com MFA/TOTP opcional) e tokens de Service Account (`fv_sa_...`, para ForgeHub/ForgeRouter/serviços) — o mesmo header `Authorization: Bearer` resolve os dois automaticamente |
| **MCP Server nativo** | além da API REST, um servidor MCP (`/mcp`) expõe as mesmas operações como *tools* para agentes — mesma autenticação, mesmo RBAC, mesma trilha de auditoria, nenhum caminho paralelo |
| **Backup/restore testado** | scripts que rodam `pg_dump`/`pg_restore` dentro do container Postgres, com a Master Key sempre em backup separado do banco |

## Arquitetura

Clean Architecture — `Domain` não depende de nada externo; cada camada acima só conhece a que
está abaixo dela:

```text
ForgeVault.Domain            entidades e regras de domínio
        ↑
ForgeVault.Application       casos de uso, interfaces (IKeyManagementProvider, IPermissionChecker, ...)
        ↑
ForgeVault.Infrastructure    EF Core + Npgsql, criptografia (AES-256-GCM), JWT, RBAC
        ↑
ForgeVault.Api               ASP.NET Core Web API — REST + MCP Server, composition root
ForgeVault.Worker            jobs em background (expiração, rotação agendada)
ForgeVault.Web               React + TypeScript (ainda não implementado)
```

**Stack:** ASP.NET Core (.NET 10), Entity Framework Core + Npgsql, PostgreSQL 17 como
datastore único e autoritativo, Redis como suporte não-autoritativo, SDK oficial
`ModelContextProtocol` para o servidor MCP.

## Estrutura do repositório

```text
forgevault/
├── src/
│   ├── ForgeVault.Domain/          # entidades e regras de domínio
│   ├── ForgeVault.Application/     # casos de uso e interfaces
│   ├── ForgeVault.Infrastructure/  # EF Core, criptografia, JWT, RBAC
│   ├── ForgeVault.Api/             # Web API — REST + MCP Server
│   ├── ForgeVault.Worker/          # jobs em background
│   └── ForgeVault.Web/             # frontend (não implementado ainda)
├── tests/
│   └── Unit/, Integration/, Security/, E2E/
├── deploy/
│   └── docker/, kubernetes/, scripts/
├── docs/                           # comece por docs/README.md
├── docker-compose.yml
└── ForgeVault.slnx
```

## Começando

### Pré-requisitos

- .NET SDK 10
- Docker (Postgres 17 + Redis via `docker-compose.yml`)
- `dotnet-ef` (`dotnet tool install --global dotnet-ef`)

### Subindo o ambiente

```bash
# Postgres (porta 5435 no host — ver docker-compose.yml para o motivo do não-padrão) + Redis
docker compose up -d postgres redis

# aplica as migrations
dotnet ef database update --project src/ForgeVault.Infrastructure --startup-project src/ForgeVault.Api

# Master Key — obrigatória, nunca gerada silenciosamente pela aplicação
deploy/scripts/generate-master-key.sh

# build e testes
dotnet build ForgeVault.slnx
dotnet test ForgeVault.slnx

# rodar a Api
dotnet run --project src/ForgeVault.Api
# GET /health/live  -> 200 sempre
# GET /health/ready -> 200 se o Postgres estiver acessível, 503 caso contrário
```

A Master Key vive em `/root/.forgevault/master.key` com permissão `600`
(`LocalFileKeyProvider`, ver `docs/modules/03_SECRETS_AND_ENCRYPTION.md`). Testes unitários de
criptografia usam chaves temporárias próprias e não dependem desse arquivo.

## API

### Autenticação e MFA

```text
POST /api/v1/auth/login          JWT (15 min) + refresh token rotativo com detecção de reuso
POST /api/v1/auth/refresh
GET  /api/v1/auth/me
POST /api/v1/auth/mfa/enroll     TOTP (RFC 6238), HMACSHA1 puro do BCL
POST /api/v1/auth/mfa/verify
```

### Organizações, projetos, ambientes e secrets

```text
POST/GET/PUT/DELETE /api/v1/organizations[/{id}]
POST/GET            /api/v1/organizations/{organizationId}/projects        RBAC: ProjectWrite
GET/PUT/DELETE      /api/v1/projects/{id}                                  RBAC: ProjectWrite
POST/GET            /api/v1/projects/{projectId}/environments              RBAC: EnvironmentWrite
GET/DELETE          /api/v1/environments/{id}                              RBAC: EnvironmentWrite
POST/GET            /api/v1/secrets                                       RBAC: SecretWrite
GET/PUT             /api/v1/secrets/{id}                                   PUT: RBAC SecretWrite
GET                 /api/v1/secrets/{id}/versions
GET                 /api/v1/secrets/{id}/value?mode=REVEAL                 RBAC: SecretReadValue + MFA
POST                /api/v1/secrets/{id}/rotate                            RBAC: SecretWrite
POST                /api/v1/secrets/{id}/revoke                            RBAC: SecretWrite
```

`mode` já existe no contrato de leitura mesmo com só `REVEAL` implementado, para que
`BROKER`/`SESSION`/`LEASE`/`INJECT` sejam aditivos quando chegarem.

### Service Accounts e Auditoria

```text
POST /api/v1/service-accounts                        cria a identidade de serviço
POST /api/v1/service-accounts/{id}/tokens             emite um token fv_sa_... (mostrado uma vez)
POST /api/v1/service-accounts/{id}/tokens/{id}/revoke
GET  /api/v1/service-accounts

GET  /api/v1/audit                                    RBAC: AuditRead (checado em qualquer escopo)
```

### MCP Server

Além do REST, ForgeVault expõe um servidor MCP nativo no mesmo processo, com a mesma
autenticação (JWT humano ou token de serviço `fv_sa_...`):

```text
POST /mcp   (Streamable HTTP, stateless)
```

| Tool | Descrição |
|---|---|
| `secret.metadata` | metadados de um secret, nunca o valor |
| `credential.request` | recupera o valor sob RBAC (hoje só `accessMode=REVEAL`); aceita `taskId`/`onBehalfOfAgent`/`runtimeSessionRef` como metadado de auditoria opcional — nunca como entrada de autorização |
| `capability.check` | simula uma permissão sem executar nem revelar nada |
| `admin.secret.create` / `update` / `rotate` / `revoke` | ciclo de vida completo de um secret |
| `admin.audit.search` | busca na trilha de auditoria |

Toda tool reaproveita exatamente os mesmos serviços dos endpoints REST equivalentes — nenhuma
lógica de autorização ou auditoria duplicada. Ver `docs/architecture/INTEGRATION_CONTRACT_MVP.md`
para um exemplo de chamada completo.

## Segurança

- **Criptografia:** envelope encryption AES-256-GCM; a Master Key nunca vive no banco, no
  repositório ou em variável de ambiente sem proteção — só em arquivo com permissão restrita.
- **RBAC:** `RoleAssignment(identity_id, role, scope_type, scope_id)` com herança
  Organization → Project → Environment; matriz papel→permissão em
  `ForgeVault.Infrastructure/Authorization/RolePermissions.cs`.
- **Auditoria:** `AuditLog` append-only, nunca contém valor de secret — verificado por testes
  que capturam toda saída de log da aplicação durante os cenários de RBAC/reveal.
- **MFA:** TOTP obrigatório apenas para contas que optaram por habilitá-lo, aplicado hoje na
  leitura de valor de secret.

## Testes

```bash
dotnet test ForgeVault.slnx
```

| Suite | Foco |
|---|---|
| `Unit` | criptografia, permissões — isolado, sem I/O |
| `Integration` | round-trip real contra Postgres |
| `Security` | RBAC, IDOR, escalonamento de privilégio, ausência de vazamento em log/auditoria |
| `E2E` | fluxos completos via `WebApplicationFactory` — autenticação, MFA, Service Accounts, MCP |

CI (`.github/workflows/ci.yml`) builda e roda a suíte completa contra um Postgres 17 real a
cada push/PR.

## Estado do projeto

**Onda 1 (MVP)** completa — scaffold, criptografia, autenticação/MFA, Secrets CRUD,
RBAC/auditoria, rotação/expiração, Service Accounts e backup/restore. **Onda 2, M8** completa
— MCP Server nativo e o contrato de contexto ForgeHub/ForgeRouter fechado. Detalhe marco a
marco em `docs/architecture/IMPLEMENTATION_READINESS.md`.

Fora do escopo atual, por decisão explícita (não esquecimento) — ver
`docs/architecture/IMPLEMENTATION_READINESS.md` §6: dynamic secrets/leases, KMS/HSM real,
SSO/OIDC/LDAP, break-glass e quorum de aprovação, multi-tenant avançado, HA/Kubernetes.

## Documentação

| Documento | Conteúdo |
|---|---|
| [`docs/README.md`](docs/README.md) | índice e hierarquia de autoridade da documentação |
| [`docs/specs/PRD.md`](docs/specs/PRD.md) / [`SPEC.md`](docs/specs/SPEC.md) | visão e especificação baseline |
| [`docs/architecture/TARGET_ARCHITECTURE.md`](docs/architecture/TARGET_ARCHITECTURE.md) | arquitetura-alvo |
| [`docs/architecture/IMPLEMENTATION_READINESS.md`](docs/architecture/IMPLEMENTATION_READINESS.md) | ordem de implementação e marcos de engenharia |
| [`docs/architecture/INTEGRATION_CONTRACT_MVP.md`](docs/architecture/INTEGRATION_CONTRACT_MVP.md) | contrato de integração REST + MCP para ForgeHub/ForgeRouter |
| [`docs/modules/`](docs/modules/) | spec de cada módulo implementável |

---

<p align="center"><sub>Parte do ecossistema Darckware — sibling de <strong>ForgeHub</strong> e <strong>ForgeRouter</strong>.</sub></p>
