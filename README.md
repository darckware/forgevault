# ForgeVault

Cofre central de credenciais e secrets do ecossistema Darckware — Security Plane consumido por ForgeHub, ForgeRouter, Hermes e demais agentes/serviços.

## Estado atual

**Onda 1 (MVP) completa** — marcos **M0** a **M7** implementados (scaffold, DbContext,
criptografia, login/JWT/MFA, Secrets CRUD, RBAC/auditoria, rotação/expiração, Service
Accounts/backup-restore). **Onda 2, M8 completo** — MCP Server nativo (onda 1 de 8 tools) +
contrato de contexto ForgeHub fechado (`docs/modules/09_FORGEHUB_FORGEROUTER_MCP_INTEGRATION.md`).
Ver `docs/architecture/IMPLEMENTATION_READINESS.md` para o detalhe de cada marco.

Checklist de aceite do MVP (`docs/ForgeVault.md` §62 / `docs/specs/PRD.md` §8) — todos
verificados com teste automatizado (e, para o restore, também com um drill manual real
nesta sessão):

- [x] usuário autentica com MFA (`MfaFlowTests`)
- [x] cria organization/projeto/ambiente (`SecretsCrudFlowTests`)
- [x] cria secret, valor fica criptografado (`SecretsCrudFlowTests`, `AesGcmEnvelopeEncryptionServiceTests`)
- [x] usuário autorizado recupera o valor / não autorizado recebe 403 (`RbacRevealAuditTests`)
- [x] toda leitura gera auditoria, inclusive negação (`RbacRevealAuditTests`)
- [x] versões são mantidas (`SecretsCrudFlowTests`, `SecretRotationAndExpirationTests`)
- [x] secrets podem expirar (`SecretRotationAndExpirationTests`)
- [x] ForgeHub consegue autenticar (`ServiceAccountIntegrationTests`, via Service Account)
- [x] ForgeRouter consegue recuperar credencial (`ServiceAccountIntegrationTests`)
- [x] backup pode ser restaurado (drill manual: `deploy/scripts/backup.sh` → `restore.sh` → decrypt de um secret conhecido confirmado nesta sessão; não automatizado como teste de CI — ver nota em "Backup e Restore" abaixo)

**Comece pela documentação, não pelo código:** leia `docs/README.md` primeiro — ele define a hierarquia de autoridade entre `docs/specs/`, `docs/architecture/` e `docs/modules/`, e evita que qualquer trabalho assuma como implementado algo que ainda é só especificação.

## Estrutura

```text
forgevault/
├── src/
│   ├── ForgeVault.Domain/          # entidades e regras de domínio, sem dependências externas
│   ├── ForgeVault.Application/     # casos de uso, interfaces (IKeyManagementProvider, etc.)
│   ├── ForgeVault.Infrastructure/  # EF Core, Npgsql, criptografia, JWT
│   ├── ForgeVault.Api/             # ASP.NET Core Web API (composition root)
│   ├── ForgeVault.Worker/          # jobs em background (expiração, rotação agendada)
│   └── ForgeVault.Web/             # React + TypeScript (ainda não criado — ver IMPLEMENTATION_READINESS.md)
├── tests/
│   ├── Unit/, Integration/, Security/, E2E/
├── deploy/
│   ├── docker/, kubernetes/, scripts/
├── docs/                           # comece por docs/README.md
├── docker-compose.yml
└── ForgeVault.slnx
```

## Rodando localmente

```bash
# sobe Postgres (host port 5435 — ver comentário no docker-compose.yml sobre
# conflito com os bancos de outros projetos Darckware neste host) + Redis
docker compose up -d postgres redis

# aplica as migrations (requer a ferramenta dotnet-ef instalada: dotnet tool install --global dotnet-ef)
dotnet ef database update --project src/ForgeVault.Infrastructure --startup-project src/ForgeVault.Api

# build e testes — ForgeVault.Api.IntegrationTests round-tripa dados reais no Postgres
# acima (docs/architecture/IMPLEMENTATION_READINESS.md, M1), então o banco precisa
# estar de pé e com a migration aplicada antes de rodar os testes
dotnet build ForgeVault.slnx
dotnet test ForgeVault.slnx

# rodar a Api localmente (fora do compose, apontando pro Postgres do compose)
dotnet run --project src/ForgeVault.Api
# GET http://localhost:5xxx/health/live  -> 200 sempre
# GET http://localhost:5xxx/health/ready -> 200 se o Postgres estiver acessível, 503 caso contrário
```

### Master Key (criptografia de secrets)

O `LocalFileKeyProvider` (docs/modules/03_SECRETS_AND_ENCRYPTION.md) exige uma Master Key
em `/root/.forgevault/master.key` com permissão `600` — ela nunca é gerada silenciosamente
pela aplicação (docs/ForgeVault.md §141). Gere-a uma vez com:

```bash
deploy/scripts/generate-master-key.sh
```

Os testes unitários de criptografia (`tests/Unit/ForgeVault.Infrastructure.Tests`) não
dependem desse arquivo real — cada teste cria e descarta sua própria chave temporária.

### Autenticação (M3) e MFA/TOTP (M6)

`POST /api/v1/auth/login`, `POST /api/v1/auth/refresh` e `GET /api/v1/auth/me` (autenticado)
— JWT curto (15 min por padrão) + refresh token rotativo com detecção de reuso (reapresentar
um refresh token já rotacionado revoga toda a família). A chave de assinatura JWT em
`appsettings.json` é um valor de desenvolvimento — sobrescreva via `Jwt__SigningKey` em
produção.

**MFA (TOTP)**: `POST /api/v1/auth/mfa/enroll` (autenticado, gera um segredo TOTP
envelope-encriptado com o módulo do M2 e retorna o segredo em base32 + uma URI
`otpauth://` — a única vez que o segredo aparece em texto puro, mesma lógica de "revelar
uma vez" usada para tokens de agente) e `POST /api/v1/auth/mfa/verify` (confirma com um
código real do autenticador; só then `MfaEnabled` vira `true`). A partir daí, `POST
/api/v1/auth/login` exige `mfaCode` para essa conta (`mfa_required`/`invalid_mfa_code` se
faltar/errar). Implementado com HMACSHA1 puro do BCL (RFC 6238), sem dependência externa —
mesma filosofia do hash de senha PBKDF2.

`Auth:Mfa:Enabled=true` por padrão agora, mas o `RequireMfa` (aplicado hoje só em `GET
/secrets/{id}/value`) só exige o código de quem **já habilitou** MFA — contas que nunca
chamaram `/mfa/enroll` não são afetadas, então isso não bloqueia retroativamente nenhuma
conta criada antes do M6. O token de acesso carrega `mfa_enabled`/`mfa_verified`, e o
refresh token guarda esse status por família, para não pedir o código de novo a cada 15
minutos.

### Secrets (M4) e RBAC/Auditoria (M5)

```text
POST/GET/PUT/DELETE /api/v1/organizations[/{id}]        (sem RBAC — bootstrap, ver nota abaixo)
POST/GET            /api/v1/organizations/{organizationId}/projects   (RBAC: ProjectWrite)
GET/PUT/DELETE      /api/v1/projects/{id}                             (RBAC: ProjectWrite)
POST/GET            /api/v1/projects/{projectId}/environments         (RBAC: EnvironmentWrite)
GET/DELETE          /api/v1/environments/{id}                         (RBAC: EnvironmentWrite)
POST/GET            /api/v1/secrets                                   (RBAC: SecretWrite)
GET/PUT             /api/v1/secrets/{id}                              (PUT: RBAC SecretWrite)
GET                 /api/v1/secrets/{id}/versions
GET                 /api/v1/secrets/{id}/value?mode=REVEAL            (RBAC: SecretReadValue + RequireMfa)
POST                /api/v1/secrets/{id}/rotate                       (RBAC: SecretWrite)
```

`POST .../rotate` (M6) cria uma nova versão exatamente como o `PUT`, mas é auditado como
`SECRET_ROTATE` (não `SECRET_UPDATE`) — a versão anterior nunca é sobrescrita e continua
legível via `GET .../versions`. Rotação automatizada via API de provider (`PROVIDER_API`)
e cascade/impact-analysis no revoke continuam módulo 07 (Fase 2). Secrets com `expires_at`
no passado são negados em `GET .../value` (409, auditado como `FAILED_ACCESS`) — checagem
em tempo de consulta, sem job de background.

`POST`/`PUT` em `/api/v1/secrets` criptografam o valor com o módulo do M2 (AES-256-GCM via
`LocalFileKeyProvider`) antes de gravar; cada escrita cria uma nova `SecretVersion` imutável.
`GET .../value?mode=REVEAL` é o único endpoint que retorna o valor decifrado — o parâmetro
`mode` já existe no contrato para que `BROKER`/`SESSION`/`LEASE`/`INJECT` sejam aditivos
quando chegarem (Fase 2/3), sem quebrar compatibilidade.

**RBAC**: um `RoleAssignment` (`identity_id`, `role`, `scope_type`, `scope_id`) concede um
papel (`Owner`, `Admin`, `SecurityAdmin`, `ProjectAdmin`, `Developer`, `Operator`, `Auditor`,
`ReadOnly`, `Agent`, `ServiceAccount`) em um escopo (Organization/Project/Environment), com
herança para baixo — um papel na Organization vale para todos os Projects/Environments/Secrets
abaixo dela. A matriz papel→permissão fica em
`ForgeVault.Infrastructure/Authorization/RolePermissions.cs` (documentada ali; é um primeiro
corte, não uma engine de política definitiva). **Não existe endpoint para gerenciar
`RoleAssignment` ainda** — mesma decisão já tomada para criação de usuários no M3: hoje esses
registros são inseridos diretamente via `DbContext` (ver os testes em
`tests/Security/ForgeVault.Security.Tests/RbacRevealAuditTests.cs` para o padrão). Criação de
Organization não tem RBAC (é a raiz da hierarquia — ver comentário em
`OrganizationEndpoints.cs`).

**Auditoria**: toda leitura/escrita relevante grava um `AuditLog` (nunca o valor do secret) —
inclusive negações de acesso (`FAILED_ACCESS`). Testado explicitamente em
`RbacRevealAuditTests` com asserção de que nenhuma linha de auditoria nem nenhuma linha de log
da aplicação (via um `ILoggerProvider` que captura tudo durante o teste) contém o valor em
texto puro, mesmo no caminho de negação.

Como a Api resolve a Master Key na inicialização (fail-fast), ela precisa existir antes de
rodar a Api ou os testes que sobem o host real (`ForgeVault.Api.IntegrationTests`,
`ForgeVault.E2E.Tests`, `ForgeVault.Security.Tests`) — ver seção "Master Key" acima.

### Service Accounts (M7)

ForgeHub/ForgeRouter (ou qualquer outro sistema) autenticam como identidades de serviço
próprias, nunca reutilizando um usuário humano — ver `docs/architecture/INTEGRATION_CONTRACT_MVP.md`
para o contrato completo.

```text
POST /api/v1/service-accounts                       cria a identidade
POST /api/v1/service-accounts/{id}/tokens            emite um token fv_sa_... (mostrado uma única vez)
POST /api/v1/service-accounts/{id}/tokens/{tokenId}/revoke
GET  /api/v1/service-accounts
```

O token `fv_sa_...` é usado exatamente como um JWT humano (`Authorization: Bearer ...`) —
um "policy scheme" no `Program.cs` decide automaticamente qual dos dois validadores usar,
pelo prefixo do token, sem header adicional. RBAC funciona de forma idêntica para
identidades de serviço (`RoleAssignment.IdentityId` aceita o `Guid` de um `ServiceAccount`
do mesmo jeito que aceita o de um `User`) — `ServiceAccountIntegrationTests` prova que uma
service account sem `RoleAssignment` recebe 403 no reveal, exatamente como um usuário sem
papel receberia. MFA nunca se aplica a tokens de serviço. Criação de Service Account não
tem RBAC própria, mesma decisão já tomada para Organization (identidade de plataforma, sem
escopo pai).

### Backup e Restore (M7)

```bash
FORGEVAULT_BACKUP_DIR=./backups deploy/scripts/backup.sh
FORGEVAULT_MASTER_KEY_BACKUP_DIR=/algum/lugar/bem/separado deploy/scripts/backup-master-key.sh
deploy/scripts/restore.sh <arquivo-de-backup.dump> [backup-da-master-key]
```

Os scripts rodam `pg_dump`/`pg_restore` **dentro do container** do Postgres
(`docker exec`), não no host — evita depender de um cliente `postgresql-client` com a
mesma versão major do servidor (o host deste projeto só tinha a v16 disponível via apt
para um servidor v17; `pg_dump` recusa rodar contra um servidor mais novo). O backup da
Master Key é sempre um script/diretório separado do backup do banco (`docs/ForgeVault.md`
§56, "regra de ouro") — `backup-master-key.sh` exige `FORGEVAULT_MASTER_KEY_BACKUP_DIR`
explicitamente, sem default, para forçar essa separação consciente.

Rodei o drill completo manualmente nesta sessão: criei um secret com valor conhecido,
rodei `backup.sh`, restaurei em um banco `forgevault_drill` isolado (mesma instância
Postgres, banco descartável — não sobrescrevi o banco de dev compartilhado pelos testes),
subi uma segunda instância da Api apontando para ele, logei com o mesmo usuário
(preservado no dump) e o `GET .../value?mode=REVEAL` devolveu o valor original
corretamente — a mesma Master Key (inalterada) decifrou os dados restaurados. Isso não
virou um teste automatizado de CI porque exigiria `pg_dump`/`pg_restore`/`createdb` com
versão compatível disponíveis no runner (o ambiente de CI atual só tem o serviço Postgres
via GitHub Actions, sem client tools instalados) — ficou registrado aqui como evidência do
drill, não como suíte repetível.

### MCP Server nativo (M8)

Além da API REST, ForgeVault expõe um MCP Server nativo, mesmo processo/host, mesma
autenticação (`SmartAuth` — JWT humano ou token `fv_sa_...` de serviço):

```text
POST /mcp   (Streamable HTTP, stateless — RequireAuthorization)
```

Tools implementadas em `src/ForgeVault.Api/Mcp/VaultTools.cs` — cada uma reaproveita
exatamente os mesmos serviços dos endpoints REST equivalentes (`IPermissionChecker`,
`AuditLogFactory`, `IEnvelopeEncryptionService`), nunca uma segunda lógica de
autorização/auditoria:

```text
secret.metadata
credential.request        (só accessMode=REVEAL; aceita taskId/onBehalfOfAgent/runtimeSessionRef
                            como metadado de auditoria opcional — nunca usado para autorização)
capability.check
admin.secret.create / update / rotate / revoke
admin.audit.search
```

`admin.secret.revoke` e `GET /api/v1/audit` (+ `POST /api/v1/secrets/{id}/revoke` equivalente
em REST) são capacidades novas neste marco: `SecretStatus.Revoked` existia no enum desde o M3
mas nada nunca o definia, e o papel `Auditor` não tinha nenhuma permissão desde o M5 — ambos
corrigidos como pré-requisito direto para as tools `admin.secret.revoke`/`admin.audit.search`
funcionarem.

Erro de negócio/autorização numa tool chega como `CallToolResult` com `isError: true` e a
mensagem em `content[0].text` — lançar `ModelContextProtocol.McpException` dentro da tool é o
mecanismo correto (sua `Message` é propagada); qualquer parâmetro opcional de uma tool precisa
de um valor default explícito em C# (`string? foo = null`) para o SDK tratá-lo como opcional
no schema e na invocação — um `string?` sem default é rejeitado como argumento ausente.

Ver `docs/architecture/INTEGRATION_CONTRACT_MVP.md` §7 para um exemplo de chamada completo e
`docs/modules/09_FORGEHUB_FORGEROUTER_MCP_INTEGRATION.md` para o que ainda não existe
(`access.*`/`session.*`/`approval.status`/`admin.policy.*`, modos além de REVEAL).

## Documentação

- `docs/README.md` — índice e hierarquia de autoridade da documentação.
- `docs/specs/PRD.md` / `docs/specs/SPEC.md` — visão e especificação baseline.
- `docs/architecture/TARGET_ARCHITECTURE.md` — arquitetura-alvo.
- `docs/architecture/IMPLEMENTATION_READINESS.md` — ordem de implementação e marcos de engenharia.
- `docs/architecture/INTEGRATION_CONTRACT_MVP.md` — como ForgeHub/ForgeRouter integram hoje.
- `docs/modules/` — spec de cada módulo implementável.
