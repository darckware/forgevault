# Module Specification — MCP Server Registry

> Documentação retroativa (ver decisão M10 abaixo): o código deste módulo (`McpServerDefinition`/`McpServerAssignment`, `McpRegistryEndpoints.cs`, `McpAssignmentRenderer.cs`, e as tools `admin.mcp.*`/`mcp.render_config` em `VaultTools.cs`) foi implementado antes desta spec existir, violando a ordem normal de `docs/README.md` ("Definition Gate completo antes do código"). Esta spec descreve o que **já está em código e testado**, não uma proposta — segue o mesmo padrão de honestidade do módulo `09` (que também documenta código já existente), mas o gap de sequenciamento em si é registrado como decisão para não se repetir.

## 1. Controle

```yaml
spec_id: MOD-11-MCP-REGISTRY
revision: 4
status: draft
owner: unassigned
approvers: []
target_release: Fase 2
architecture_refs:
  - docs/architecture/TARGET_ARCHITECTURE.md §6 (MCP Server Nativo)
  - docs/architecture/IMPLEMENTATION_READINESS.md §3.3 (M10)
  - docs/modules/09_FORGEHUB_FORGEROUTER_MCP_INTEGRATION.md (fundação de MCP Server/tools que este módulo estende)
decisions:
  - "M10: código (entidades, endpoints REST, tools MCP, migration) foi escrito e mesclado antes de existir spec de módulo, contrariando a regra de `docs/README.md` de que um módulo só recebe código após seu Definition Gate fechar. Esta spec é a documentação retroativa desse trabalho, não uma autorização prévia. Registrado aqui para que revisões futuras não repitam a sequência."
  - "M10: o catálogo (`McpServerDefinition`) nunca guarda um valor de secret — apenas a forma da conexão (transporte, comando/URL, nomes de parâmetros que devem vir de um Secret). O valor sensível em si só existe como referência (`{\"secretId\": \"<guid>\"}`) dentro de `McpServerAssignment.ParamValuesJson`, resolvido (descriptografado) sob demanda em `/render` ou `mcp.render_config` — nunca persistido resolvido."
  - "M10: uma `McpServerAssignment` só pode ser criada para uma identidade que já possua ao menos um `RoleAssignment` ativo em algum escopo (checado em `McpRegistryEndpoints`/`VaultTools.AdminMcpAssignAsync`). Justificativa: uma atribuição de MCP sem nenhuma autorização RBAC por trás renderizaria uma config funcional para uma identidade que não deveria ter acesso a nada — a checagem rejeita esse estado inconsistente na origem, em vez de deixar para o render-time descobrir."
  - "M10: a ação de auditoria emitida ao criar OU atualizar uma assignment é `MCP_SERVER_ACCESS_RELEASED` (não `MCP_SERVER_ASSIGNMENT_CREATED`/`_GRANTED`). O nome é o único usado no código (`McpRegistryEndpoints.cs`, `VaultTools.cs`) e significa \"acesso liberado a esta identidade\", não uma revogação — registrado aqui explicitamente porque o nome, isolado, sugere o oposto do que faz."
  - "M10: este módulo nunca escreve no `config.yaml`/equivalente de um host de agente. `/render` e `mcp.render_config` apenas devolvem o bloco de config resolvido; um passo explícito e revisável fora deste repositório aplicaria esse bloco a um host real. Os comentários de código citam `deploy/scripts/sync_mcp_config.py` como esse passo, mas **esse script não existe neste repositório** — é uma referência a um processo pretendido, não a algo implementado (ver `open_blocking_questions`)."
  - "M10 rev2: UI construída (`McpServersPage`/`McpServerDetailPage`/`McpAssignmentsPage` em `src/ForgeVault.Web`) — catálogo por Organization com criar/remover, e gestão de assignment por identidade (grant/lookup/revoke/render) seguindo o mesmo padrão de `AccessRolesPage` (lookup por id colado, sem endpoint de busca de identidade). `RenderMcpConfigButton` reusa a interação \"vault door\" de `RevealSecretButton` (mascarado por padrão, confirmação explícita, auto-reseal em 20s) porque o config renderizado pode conter múltiplos valores de secret decriptados."
  - "M10 rev2: o gap de segurança da revisão 1 (render não checa `RevokedAt`) foi confirmado manualmente, não é só teórico — reproduzido via chamada direta à API: `POST .../revoke` seguido de `GET .../render` retorna `200` com o secret ainda decriptado. Ver seção 12."
  - "M10 rev3: gap de `RevokedAt` corrigido — `McpAssignmentRenderer.RenderAsync` agora checa `assignment.RevokedAt is not null` logo após carregar a assignment, antes de resolver qualquer secret, e retorna um outcome dedicado (`AssignmentRevoked`) mapeado para `409 Conflict` (`assignment_revoked`) tanto em `/render` (REST) quanto em `mcp.render_config` (MCP), com `AuditLog FAILED_ACCESS` (`reason: assignment_revoked`) — mesmo padrão já usado por `SecretEndpoints` para `secret_not_active`. Regressão coberta por `McpAssignmentRenderTests.Render_AfterRevoke_Is409_DoesNotLeakSecret_AndAudits` (`tests/Security`), que falha (200 em vez de 409) contra o código pré-fix — verificado manualmente revertendo a correção antes de reaplicá-la. Cobre apenas este gap específico, não o restante de AC-01 a AC-05 (seção 13), que continua sem teste dedicado."
  - "M10 rev4: invariante 3 (seção 4) corrigida — `McpAssignmentValidation.FindMissingOrInvalidSecretParam` (novo, compartilhado por `McpRegistryEndpoints.cs` e `VaultTools.AdminMcpAssignAsync`, mesmo princípio de não bifurcar lógica sensível entre REST e MCP já usado para o render) rejeita com `409 missing_required_secret_param:<name>` uma assignment (criação OU atualização) que não traga `{\"secretId\": ...}` para todo nome em `SecretParamNames` — cobrindo tanto o parâmetro ausente quanto o valor fornecido como string literal. Coberto por `McpAssignmentValidationTests` (`tests/Security`), verificado (como o de rev3) revertendo a correção e confirmando que os dois testes falham contra o código anterior."
open_blocking_questions:
  - "Onde/como o bloco renderizado por `/render` ou `mcp.render_config` chega de fato ao `mcp_servers:` de um host de agente (Hermes ou outro)? O script `deploy/scripts/sync_mcp_config.py` citado nos comentários de código não existe. Sem ele, o valor prático do módulo pára em \"ForgeVault sabe a config certa\", sem um caminho documentado até \"o host está usando essa config\"."
  - "Nenhuma métrica de observabilidade específica (ex.: `mcp_server_renders_total`) foi adicionada — ver seção 11."
```

Uma spec com `open_blocking_questions` não autoriza implementação autônoma adicional sem fechar essas questões primeiro — o código já existente descrito aqui é uma exceção documentada (ver decisão acima), não um precedente para o próximo incremento deste módulo.

## 2. Objetivo e limite

- **Problema resolvido:** antes deste módulo, o `mcp_servers:` de cada agente (Hermes ou outro) era editado à mão em `config.yaml`, incluindo tokens em texto puro como `FORGEHUB_AGENT_TOKEN` — exatamente o tipo de credencial hardcoded que o ForgeVault existe para eliminar (ver `CLAUDE.md`, "identidades ... obtêm credenciais sem que elas fiquem persistidas ou hardcoded em outro lugar").
- **Resultado esperado:** o ForgeVault é a fonte única de verdade de (a) quais servidores MCP existem e como se conecta a cada um, e (b) qual identidade está autorizada a usar qual servidor, com quais parâmetros — incluindo quais desses parâmetros vêm de um Secret já cadastrado, nunca duplicados em texto puro.
- **Atores:** Owner/Admin (cadastra o catálogo e concede assignments), a própria identidade atribuída (renderiza sua própria config), qualquer identidade com `SecretReadValue` nos secrets referenciados (pode renderizar em nome de outra identidade).
- **Responsabilidade deste módulo:** manter o catálogo (`McpServerDefinition`), a atribuição por identidade (`McpServerAssignment`) e resolver/descriptografar essa atribuição em um bloco de config pronto para uso.
- **Responsabilidades de outros módulos:** autenticação de quem chama (módulo `02`), RBAC/`Permission.McpRegistryWrite` (módulo `04`), criptografia/armazenamento do secret referenciado (módulo `03`), auditoria (módulo `06`), o MCP Server nativo em si — host, transporte, `SmartAuth` (módulo `09`).
- **Dentro do escopo:** CRUD do catálogo por Organization; grant/revoke/listagem de assignment por identidade; render (REST e MCP) resolvendo referências a Secret.
- **Fora do escopo:** aplicar a config renderizada a um host de agente real (ver `open_blocking_questions`); UI; descoberta automática de servidores MCP; qualquer coisa além de `Stdio`/`Http` como transporte.

## 3. Casos de uso e processos

| ID | Ator | Gatilho | Fluxo principal | Alternativas | Resultado | Scope elements |
|---|---|---|---|---|---|---|
| UC-01 | Owner/Admin | Precisa cadastrar um novo servidor MCP no catálogo da Organization | `POST /api/v1/organizations/{orgId}/mcp-servers` ou tool `admin.mcp.register` | nome já usado na Organization → `409 mcp_server_name_taken` | `McpServerDefinition` criado, `AuditLog` `MCP_SERVER_REGISTERED` | Organization |
| UC-02 | Owner/Admin | Precisa dar a um agente/usuário acesso a um servidor já catalogado | `POST /api/v1/identities/{identityId}/mcp-assignments` ou tool `admin.mcp.assign`, com `paramValuesJson` | identidade sem nenhum `RoleAssignment` ativo → `409 identity_has_no_role_assignment`; assignment já existe e ativa → atualiza `ParamValuesJson` em vez de duplicar | `McpServerAssignment` criado/atualizado, `AuditLog` `MCP_SERVER_ACCESS_RELEASED` com `grantedViaRoleAssignments` | Identity |
| UC-03 | Identidade atribuída (self) | Precisa da config resolvida para conectar ao servidor MCP | `GET /api/v1/mcp-assignments/{id}/render` ou tool `mcp.render_config`, chamando com o próprio token | um `secretId` referenciado não existe mais → `404 referenced_secret_not_found:<id>` | bloco de config com env/headers resolvidos (secrets descriptografados), `AuditLog` `SECRET_REVEAL` por secret resolvido | Identity (self) |
| UC-04 | Terceiro com `SecretReadValue` | Precisa renderizar a config de uma assignment que não é sua (ex.: automação de deploy) | mesmo endpoint/tool do UC-03, mas chamador ≠ identidade da assignment | falta `SecretReadValue` em algum secret referenciado → `403 forbidden`, `AuditLog` `FAILED_ACCESS` | mesma resposta do UC-03, se autorizado em todos os secrets | Identity (cross) |
| UC-05 | Owner/Admin | Precisa revogar o acesso de uma identidade a um servidor | `POST /api/v1/mcp-assignments/{id}/revoke` ou tool `admin.mcp.revoke_assignment` | já revogado → idempotente, sem novo `AuditLog` | `RevokedAt` setado, `AuditLog` `MCP_SERVER_ASSIGNMENT_REVOKED` | Identity |
| UC-06 | Owner/Admin | Precisa remover um servidor do catálogo | `DELETE /api/v1/mcp-servers/{id}` | — | `McpServerDefinition` removido (cascade sobre `McpServerAssignment` via FK), `AuditLog` `MCP_SERVER_REMOVED` | Organization |

## 4. Modelo de domínio e dados

### Entidades

| Entidade | Atributo | Tipo | Obrigatório | Default | Constraint | Sensibilidade |
|---|---|---|---|---|---|---|
| `McpServerDefinition` | `Id` | `Guid` | sim | — | PK | — |
| | `OrganizationId` | `Guid` | sim | — | FK `organizations.id`, cascade delete | — |
| | `Name` | `string` | sim | — | `max 100`, único por `OrganizationId` | — |
| | `Transport` | `enum` (`Stdio`\|`Http`) | sim | — | armazenado como `varchar(16)` | — |
| | `Command` | `string?` | não (obrigatório na prática se `Transport=Stdio`, não imposto no schema) | null | — | — |
| | `ArgsJson` | `string?` | não | null | JSON de `List<string>` | — |
| | `Url` | `string?` | não (obrigatório na prática se `Transport=Http`, não imposto no schema) | null | — | — |
| | `Timeout` / `ConnectTimeout` | `int?` | não | null | — | — |
| | `StaticEnvJson` | `string?` | não | null | JSON de `Dictionary<string,string>` — **nunca** deve conter um valor sensível (é compartilhado por toda identidade atribuída, sem indireção de Secret) | não-sensível por definição |
| | `SecretParamNamesJson` | `string?` | não | null | JSON de `List<string>` — nomes que toda `McpServerAssignment` deste definition **deve** suprir como referência a Secret (declarativo; não validado no `POST /mcp-assignments` hoje, ver invariante 3) | — |
| | `CreatedAt` | `DateTimeOffset` | sim | `UtcNow` | — | — |
| `McpServerAssignment` | `Id` | `Guid` | sim | — | PK | — |
| | `IdentityId` | `Guid` | sim | — | referência soft a `User`/`ServiceAccount`, mesma convenção de `RoleAssignment.IdentityId` (módulo 04) — sem FK no schema | — |
| | `McpServerDefinitionId` | `Guid` | sim | — | FK `mcp_server_definitions.id`, cascade delete | — |
| | `ParamValuesJson` | `string` | sim | — | JSON dict: cada valor é uma `string` literal (não-sensível) ou `{"secretId": "<guid>"}` | **contém referências a secret, nunca o valor** |
| | `CreatedAt` | `DateTimeOffset` | sim | `UtcNow` | — | — |
| | `RevokedAt` | `DateTimeOffset?` | não | null | presença = revogado | — |

### Relações

| Origem | Relação | Destino | Cardinalidade | On delete | Regra cross-domain |
|---|---|---|---|---|---|
| `McpServerDefinition` | pertence a | `Organization` | N:1 | Cascade | remover a Organization remove seu catálogo inteiro |
| `McpServerAssignment` | referencia | `McpServerDefinition` | N:1 | Cascade | remover a definition remove toda assignment que a usa (UC-06) |
| `McpServerAssignment` | atribuída a | Identity (`User`/`ServiceAccount`) | N:1 (soft, sem FK) | — | mesma convenção de `RoleAssignment` — não há integridade referencial de banco entre `IdentityId` e as tabelas de identidade |
| `McpServerAssignment.ParamValuesJson` (por chave) | pode referenciar | `Secret` (por `secretId`) | N:N lógico (dentro do JSON) | — | referência **não é FK de banco** — resolvida em runtime por `McpAssignmentRenderer`; um Secret apagado deriva em `404 referenced_secret_not_found` no render, não em erro de integridade |

### Invariantes

1. `McpServerDefinition.Name` é único por `OrganizationId`, nunca globalmente (duas Organizations podem ambas ter um servidor chamado `forgehub`).
2. Uma `McpServerAssignment` ativa (`RevokedAt IS NULL`) por par (`IdentityId`, `McpServerDefinitionId`) — o `POST` de assignment é idempotente nesse par: se já existe uma ativa, atualiza `ParamValuesJson` em vez de criar uma segunda.
3. `McpServerDefinition.SecretParamNamesJson` é **imposto desde a rev4** — `McpAssignmentValidation.FindMissingOrInvalidSecretParam` (usado por `McpRegistryEndpoints.cs` e `VaultTools.AdminMcpAssignAsync`, a mesma checagem nas duas superfícies) rejeita com `409 missing_required_secret_param:<name>` ao criar OU atualizar uma `McpServerAssignment` que não traga, para todo nome declarado em `SecretParamNames`, uma entrada `{"secretId": ...}` em `ParamValuesJson` — falta o parâmetro inteiramente, ou ele aparece como string literal, ambos rejeitados. Coberto por `McpAssignmentValidationTests` (`tests/Security`). Antes da rev4 este era um gap conhecido, não corrigido.
4. Nenhuma tabela deste módulo guarda o valor de um secret — apenas a referência (`secretId`). O valor resolvido (`Dictionary<string,string> resolvedValues` em `McpAssignmentRenderer`) só existe em memória durante a chamada de render, nunca é persistido.
5. Criar uma `McpServerAssignment` exige que a identidade alvo já tenha ao menos um `RoleAssignment` com `RevokedAt IS NULL` em qualquer escopo — não necessariamente relacionado ao servidor MCP sendo atribuído (checagem de existência de autorização, não de relevância dela).

### Migração e backfill

- **Estado anterior:** tabelas não existiam.
- **Transformação:** migration `20260912233943_AddMcpRegistry` cria `mcp_server_definitions` e `mcp_server_assignments` (ver DDL completo na migration), com índice único `(organization_id, name)` em definitions e índice não-único `(identity_id, mcp_server_definition_id)` em assignments (o índice não impõe a invariante 2 no banco — é a camada de aplicação, via `SingleOrDefaultAsync` filtrando `RevokedAt == null`, que garante no máximo uma ativa).
- **Backfill:** nenhum — funcionalidade nova, sem dado legado a migrar.
- **Compatibilidade:** aditiva, sem alteração em tabela existente.
- **Rollback/roll-forward:** `Down()` da migration derruba as duas tabelas; seguro porque nenhuma outra tabela depende delas (FK só sai de `McpServerDefinition`/`McpServerAssignment`, nunca chega nelas de fora).

## 5. Máquinas de estados

### Estados (`McpServerAssignment`)

| Estado | Significado | Entradas permitidas | Saídas permitidas | Terminal? |
|---|---|---|---|---|
| `active` (`RevokedAt IS NULL`) | identidade autorizada a renderizar esta assignment | criação, ou update de `ParamValuesJson` (permanece `active`) | `revoked` | não |
| `revoked` (`RevokedAt IS NOT NULL`) | acesso encerrado; `/render` e `mcp.render_config` rejeitam com `409 assignment_revoked` (corrigido na rev3 — ver seção 12) | nenhuma (revogar de novo é idempotente, sem transição real) | — | sim, na prática (nenhum comando reativa) |

`McpServerDefinition` não tem máquina de estados — existe ou foi removida (hard delete, sem soft-delete/estado intermediário).

### Comandos

| Comando | Ator | Precondições | Transição | Side effects | Idempotência | Erros |
|---|---|---|---|---|---|---|
| Registrar definition | `McpRegistryWrite` no escopo da Organization | nome ainda não usado na Organization | — (criação) | `AuditLog MCP_SERVER_REGISTERED` | não (nome duplicado é erro, não no-op) | `404 organization_not_found`, `403`, `409 mcp_server_name_taken` |
| Remover definition | `McpRegistryWrite` no escopo da Organization | definition existe | — (deleção) | cascade sobre assignments; `AuditLog MCP_SERVER_REMOVED` | não (segunda chamada é `404`) | `404`, `403` |
| Criar/atualizar assignment | `McpRegistryWrite` no escopo da Organization da definition | definition existe; identidade tem ≥1 `RoleAssignment` ativo | `active` (nova) ou permanece `active` (update) | `AuditLog MCP_SERVER_ACCESS_RELEASED` com `grantedViaRoleAssignments` | sim, por par (identity, definition) | `404 mcp_server_not_found`, `403`, `409 identity_has_no_role_assignment` |
| Revogar assignment | `McpRegistryWrite` no escopo da Organization da definition | assignment existe | `active` → `revoked` | `AuditLog MCP_SERVER_ASSIGNMENT_REVOKED` (só na primeira vez) | sim | `404` |
| Render | self, ou qualquer ator com `SecretReadValue` em todo secret referenciado | assignment e definition existem; todo `secretId` referenciado ainda existe | — (leitura) | `AuditLog SECRET_REVEAL` por secret resolvido; `AuditLog FAILED_ACCESS` se negado | sim (leitura pura) | `404` (assignment/definition/secret), `403 forbidden` |

PATCH livre de status não existe neste módulo — toda transição passa por um comando explícito acima.

## 6. Policies e permissões

| Ação | Papel/authority | Policy | Aprovação humana | Separation of duties | AuditEvent |
|---|---|---|---|---|---|
| Registrar/remover definition | `Permission.McpRegistryWrite` no escopo `Organization` | apenas `Owner`/`Admin` (`RolePermissions.cs`) | não | nenhuma — mesmo padrão "primeiro corte" de `RoleAssignmentWrite` (módulo 04): não há checagem adicional de hierarquia | `MCP_SERVER_REGISTERED`/`MCP_SERVER_REMOVED` |
| Criar/atualizar/revogar assignment | `Permission.McpRegistryWrite` no escopo `Organization` da definition | idem, mais a precondição de RoleAssignment ativo (seção 4, invariante 5) | não | idem | `MCP_SERVER_ACCESS_RELEASED`/`MCP_SERVER_ASSIGNMENT_REVOKED` |
| Listar assignments de uma identidade | a própria identidade, ou qualquer ator com `McpRegistryWrite` em **algum** escopo (`HasPermissionAnywhereAsync`) | — | não | nenhuma | — (leitura, sem audit) |
| Render (self) | a própria identidade da assignment | confia na decisão já tomada por quem criou a assignment — nenhuma checagem adicional de `SecretReadValue` | não | nenhuma | `SECRET_REVEAL` por secret |
| Render (cross-identity) | qualquer ator | `SecretReadValue` no escopo de **cada** secret referenciado | não | nenhuma | `SECRET_REVEAL` (sucesso) ou `FAILED_ACCESS` (negado) |

`Permission.McpRegistryWrite` foi adicionado à matriz existente do módulo `04` (`RolePermissions.cs`), concedido apenas a `Owner`/`Admin` — mesmo padrão de `RoleAssignmentWrite` (M9).

## 7. Contratos de API

```yaml
operation_id: registerMcpServerDefinition
method: POST
path: /api/v1/organizations/{organizationId}/mcp-servers
authorization: McpRegistryWrite no escopo Organization
idempotency: null (nome duplicado é erro)
request_schema: CreateMcpServerDefinitionRequest
response_schema: McpServerDefinitionResponse (201)
errors:
  - {status: 404, code: organization_not_found, condition: organização não existe}
  - {status: 403, code: forbidden, condition: sem McpRegistryWrite}
  - {status: 409, code: mcp_server_name_taken, condition: nome já usado na organização}
events: [MCP_SERVER_REGISTERED]
```

```yaml
operation_id: listMcpServerDefinitions
method: GET
path: /api/v1/organizations/{organizationId}/mcp-servers
authorization: RequireAuthorization() apenas — nenhuma checagem de McpRegistryWrite ou de pertencimento à Organization (qualquer identidade autenticada pode listar o catálogo de qualquer Organization)
idempotency: null
request_schema: null
response_schema: "McpServerDefinitionResponse[]"
errors: []
events: []
```

```yaml
operation_id: getMcpServerDefinition
method: GET
path: /api/v1/mcp-servers/{id}
authorization: RequireAuthorization() apenas — mesma observação acima
idempotency: null
request_schema: null
response_schema: McpServerDefinitionResponse
errors:
  - {status: 404, code: null, condition: definition não existe}
events: []
```

```yaml
operation_id: deleteMcpServerDefinition
method: DELETE
path: /api/v1/mcp-servers/{id}
authorization: McpRegistryWrite no escopo Organization da definition
idempotency: null (segunda chamada é 404)
request_schema: null
response_schema: null (204)
errors:
  - {status: 404, code: null, condition: definition não existe}
  - {status: 403, code: forbidden, condition: sem McpRegistryWrite}
events: [MCP_SERVER_REMOVED]
```

```yaml
operation_id: createOrUpdateMcpServerAssignment
method: POST
path: /api/v1/identities/{identityId}/mcp-assignments
authorization: McpRegistryWrite no escopo Organization da definition
idempotency: por par (identityId, mcpServerDefinitionId) — update em vez de duplicar
request_schema: CreateMcpServerAssignmentRequest
response_schema: McpServerAssignmentResponse (201 na criação, 200 no update)
errors:
  - {status: 404, code: mcp_server_not_found, condition: definition não existe}
  - {status: 403, code: forbidden, condition: sem McpRegistryWrite}
  - {status: 409, code: identity_has_no_role_assignment, condition: identidade sem nenhum RoleAssignment ativo}
events: [MCP_SERVER_ACCESS_RELEASED]
```

```yaml
operation_id: listMcpServerAssignments
method: GET
path: /api/v1/identities/{identityId}/mcp-assignments
authorization: a própria identidade, ou McpRegistryWrite em qualquer escopo
idempotency: null
request_schema: null
response_schema: "McpServerAssignmentResponse[]"
errors:
  - {status: 403, code: null, condition: chamador não é a identidade e não tem McpRegistryWrite em nenhum escopo}
events: []
```

```yaml
operation_id: revokeMcpServerAssignment
method: POST
path: /api/v1/mcp-assignments/{id}/revoke
authorization: McpRegistryWrite no escopo Organization da definition referenciada
idempotency: sim (revogar já-revogado não gera novo AuditLog)
request_schema: null
response_schema: McpServerAssignmentResponse (200)
errors:
  - {status: 404, code: null, condition: assignment não existe}
  - {status: 403, code: forbidden, condition: sem McpRegistryWrite}
events: [MCP_SERVER_ASSIGNMENT_REVOKED]
```

```yaml
operation_id: renderMcpServerAssignment
method: GET
path: /api/v1/mcp-assignments/{id}/render
authorization: self, ou SecretReadValue em todo secret referenciado
idempotency: null (leitura pura, mas cada chamada gera novo AuditLog SECRET_REVEAL)
request_schema: null
response_schema: McpServerRenderResponse
errors:
  - {status: 404, code: null, condition: assignment ou definition não existe}
  - {status: 404, code: "referenced_secret_not_found:<secretId>", condition: um secretId referenciado não existe mais}
  - {status: 403, code: null, condition: chamador não é self e falta SecretReadValue em algum secret}
events: [SECRET_REVEAL, FAILED_ACCESS]
```

Tools MCP equivalentes (mesmo host `/mcp` do módulo `09`, mesma autenticação `SmartAuth`, lógica compartilhada — `McpAssignmentRenderer` é reusado literalmente pelo endpoint REST de render e pela tool `mcp.render_config`):

```text
admin.mcp.register           → registerMcpServerDefinition
admin.mcp.assign             → createOrUpdateMcpServerAssignment
admin.mcp.revoke_assignment  → revokeMcpServerAssignment
mcp.render_config            → renderMcpServerAssignment
```

Sem equivalente MCP para `list`/`get`/`delete` de definition nem para `list` de assignment — essas operações só existem hoje via REST.

## 8. Eventos e auditoria

| Evento | Producer | Payload version | Consumer | Idempotency key | Retention |
|---|---|---|---|---|---|
| `MCP_SERVER_REGISTERED` | `McpRegistryEndpoints` / `VaultTools.AdminMcpRegisterAsync` | `{}` (sem metadata adicional) | Audit (módulo 06) | — | conforme módulo 06 |
| `MCP_SERVER_REMOVED` | `McpRegistryEndpoints.deleteMcpServerDefinition` | `{}` | Audit | — | conforme módulo 06 |
| `MCP_SERVER_ACCESS_RELEASED` | `McpRegistryEndpoints` / `VaultTools.AdminMcpAssignAsync` | `{grantedViaRoleAssignments: [{id, role, scopeType, scopeId}, ...]}` | Audit | — | conforme módulo 06 |
| `MCP_SERVER_ASSIGNMENT_REVOKED` | `McpRegistryEndpoints` / `VaultTools.AdminMcpRevokeAssignmentAsync` | `{}` | Audit | — | conforme módulo 06 |
| `SECRET_REVEAL` (reusado do módulo 05/09) | `McpAssignmentRenderer` | `{"context":"mcp_render"}` | Audit | — | conforme módulo 06 |
| `FAILED_ACCESS` (reusado do módulo 05/09) | `McpAssignmentRenderer` | `{"reason":"forbidden","context":"mcp_render"}` | Audit | — | conforme módulo 06 |

Nenhum evento ou registro de auditoria deste módulo contém o valor de um secret — apenas metadados (ator, recurso, ação, timestamp, resultado, e no caso de `MCP_SERVER_ACCESS_RELEASED`, os `RoleAssignment`s que justificaram a concessão). Ver `docs/ForgeVault.md` §21.

## 9. Interface

| Tela/Componente | Rota | Atores | Dados | Ações | Policies |
|---|---|---|---|---|---|
| `McpServersPage` | `/mcp-servers` | Owner/Admin | catálogo de `McpServerDefinition` da Organization selecionada | selecionar Organization, registrar, remover, abrir detalhe | `McpRegistryWrite` (o backend rejeita; a UI não esconde a ação para quem não tem permissão) |
| `McpServerDetailPage` | `/mcp-servers/:id` | Owner/Admin | uma `McpServerDefinition` (transporte, command/url, static env, secret param names) | remover | idem |
| `McpAssignmentsPage` | `/mcp-assignments` | Owner/Admin (grant/revoke/render cross-identity), identidade atribuída (render self) | `McpServerAssignment[]` de uma identidade colada por id | conceder (form com Organization → server → param values), revogar, renderizar config resolvida | `McpRegistryWrite` para grant/revoke; render segue a regra self-or-`SecretReadValue` da seção 6 |

Loading/empty/error: `Spinner`/`EmptyState`/mensagem de erro do servidor em todos os três, mesmo padrão do resto do app. Mascaramento por padrão (§19): `RenderMcpConfigButton` reusa a interação "vault door" de `RevealSecretButton` — confirmação explícita antes de decriptar, auto-reseal em 20s, nunca cacheado (mutation, não query). Sem endpoint de busca de identidade (mesma limitação de `AccessRolesPage`), então o lookup de assignments é por id colado — não uma limitação nova deste módulo.

**Ainda fora do escopo desta UI:** nenhuma tela lista "quais identidades estão em um servidor" a partir da definition (a API não expõe essa consulta — só por identidade); a criação de assignment não valida no cliente que `SecretParamNames` da definition foram todos supridos (mesma lacuna do backend, invariante 3 da seção 4).

## 10. Integrações e falhas

| Integração | Ownership | Timeout | Retry | Circuit/reconciliation | Falha visível | Fonte de verdade |
|---|---|---|---|---|---|---|
| Host de agente (Hermes ou outro) consumindo o bloco renderizado | fora deste repositório | n/a | n/a | nenhuma — este módulo não empurra config, o consumidor puxa via render | consumidor decide | ForgeVault (catálogo + assignment) é a fonte de verdade da *intenção*; se o host de fato usa a config renderizada é responsabilidade externa não coberta aqui |
| `IEnvelopeEncryptionService` (módulo 03) | interno | — | — | — | erro de decrypt propaga como exceção não tratada por este módulo (sem catch específico em `McpAssignmentRenderer`) | módulo 03 |

## 11. Observabilidade

- logs estruturados: nenhum específico deste módulo além do `AuditLog` (seção 8).
- métricas: nenhuma implementada (`mcp_calls_total` do módulo 09 cobriria as tools deste módulo também, mas não foi implementada em nenhum marco ainda).
- traces: não implementado.
- health/readiness: nenhum específico — segue o `/health/ready` geral (módulo 01).
- alertas: nenhum.
- SLO/SLA: nenhum definido.
- dashboards: nenhum.

## 12. Segurança e privacidade

- **Threat model:** o risco principal era uma `McpServerAssignment` sobreviver à revogação prática — `RevokedAt` sempre foi setado corretamente, mas **nem `/render` nem `mcp.render_config` checavam `RevokedAt` antes de resolver** (`McpAssignmentRenderer.RenderAsync` não filtrava por `RevokedAt == null`), confirmado manualmente na rev2 (`POST .../revoke` seguido de `GET .../render` retornava `200` com o secret ainda decriptado). **Corrigido na rev3:** `RenderAsync` agora checa `assignment.RevokedAt` logo após carregar a assignment, antes de resolver qualquer parâmetro, retornando `409 Conflict` (`assignment_revoked`) em ambas as superfícies e emitindo `AuditLog FAILED_ACCESS` (`reason: assignment_revoked`) em vez de decriptar. Coberto por `McpAssignmentRenderTests.Render_AfterRevoke_Is409_DoesNotLeakSecret_AndAudits` (`tests/Security`), que reproduz exatamente a sequência revoke→render e assert tanto o status code quanto a ausência do valor no corpo/audit.
- **Autenticação/autorização:** herda `SmartAuth` (módulo 02) e `IPermissionChecker` (módulo 04); nenhum mecanismo de autorização paralelo.
- **Secrets:** nunca persistidos por este módulo, só referenciados; resolvidos em memória, só na resposta de render.
- **Dados pessoais/sensíveis:** nenhum além do que já existe em `Secret`.
- **Retenção e exclusão:** segue módulo 06 para `AuditLog`; `McpServerDefinition`/`McpServerAssignment` são hard-deleted (sem soft-delete, sem retenção adicional).
- **Abuso/rate limit:** nenhum específico — `/render` pode ser chamado repetidamente sem limite, cada chamada gera um novo `SECRET_REVEAL` de auditoria (ruído potencial, sem controle de taxa).
- **Supply chain:** n/a.

## 13. Critérios de aceite

| ID | Given | When | Then | Nível do teste | Evidência |
|---|---|---|---|---|---|
| AC-01 | Owner/Admin autenticado | registra uma definition nova via REST ou `admin.mcp.register` | `201`, `AuditLog MCP_SERVER_REGISTERED` | Integration/E2E | **Satisfeito (REST)** — `McpRegistryEndpointTests.RegisterDefinition_RequiresMcpRegistryWrite_AndAuditsOnSuccess`; a variante via tool `admin.mcp.register` continua sem teste dedicado |
| AC-02 | identidade sem `McpRegistryWrite` | tenta registrar/remover/assign/revoke | `403`/`forbidden` | Security | **Satisfeito (REST)** — `RegisterDefinition_RequiresMcpRegistryWrite_AndAuditsOnSuccess` (register) + `NonPrivilegedIdentity_CannotRemoveDefinition_AssignOrRevoke` (remove/assign/revoke); as tools MCP equivalentes continuam sem teste dedicado |
| AC-03 | identidade sem nenhum `RoleAssignment` ativo | Owner tenta criar assignment para ela | `409 identity_has_no_role_assignment` | Integration | **Satisfeito (REST)** — `McpRegistryEndpointTests.CreateAssignment_ForIdentityWithNoActiveRoleAssignment_Is409` |
| AC-04 | assignment com um parâmetro `{"secretId": ...}` | self chama render | valor decriptado retornado, `AuditLog SECRET_REVEAL`, nenhum log contém o valor | Security | **Parcialmente coberto** — `McpAssignmentRenderTests.Render_AfterRevoke_Is409_DoesNotLeakSecret_AndAudits` exercita o render-antes-do-revoke como setup e assert o valor decriptado, mas não assert diretamente o `AuditLog SECRET_REVEAL`; um teste dedicado a AC-04 isolado ainda falta |
| AC-05 | assignment de outra identidade, chamador sem `SecretReadValue` no secret referenciado | chamador tenta renderizar | `403 forbidden`, `AuditLog FAILED_ACCESS`, nenhum vazamento | Security | **Satisfeito** — `McpRegistryEndpointTests.Render_ByStrangerWithoutSecretReadValue_Is403_AndAudits_NoLeak` |
| AC-06 | as 4 tools MCP (`admin.mcp.register/assign/revoke_assignment`, `mcp.render_config`) | `tools/list` | aparecem no catálogo | E2E | **Satisfeito** — `McpToolsTests.ToolsList_ReturnsAllPlannedTools` (corrigido nesta revisão para incluir as 4 tools novas) |

**Gap real (histórico, rev1/rev2):** ao contrário de todo módulo anterior (`RbacRevealAuditTests`, `RoleAssignmentEndpointTests`, `AuditAndRevokeEndpointTests`), este módulo não tinha nenhum arquivo de teste dedicado em `tests/Security` ou `tests/Integration` — a única cobertura automatizada era a listagem de nomes de tool em `McpToolsTests.cs`. Fechado nas revisões seguintes: rev3 adiciona `McpAssignmentRenderTests.cs` (gap de `RevokedAt`, seção 12), rev4 adiciona `McpAssignmentValidationTests.cs` (invariante 3, seção 4) e `McpRegistryEndpointTests.cs` (AC-01, AC-02, AC-03, AC-05 acima). Restam sem teste dedicado: a superfície MCP (tools) para AC-01/AC-02/AC-03, e um teste isolado para AC-04 que assert o `AuditLog SECRET_REVEAL` diretamente.

## 14. Plano de entrega

- **Ordem de implementação:** já implementado fora de ordem (ver decisão M10, seção 1) — depende em tempo de execução dos módulos `02`, `03`, `04`, `06`, `09` (todos já estáveis).
- **Feature flags:** nenhuma.
- **Rollout:** já em `main`, sem flag — ver git status/commits do marco M10.
- **Dados de seed:** nenhum.
- **Compatibilidade:** aditiva.
- **Rollback/roll-forward:** `Down()` da migration reverte o schema; código pode ser revertido via `git revert` sem afetar outros módulos (sem FK de fora apontando para estas tabelas).
- **Documentação/manual:** esta spec (retroativa) e as menções em `docs/architecture/IMPLEMENTATION_READINESS.md` §3.3 e `docs/architecture/INTEGRATION_CONTRACT_MVP.md` §7, adicionadas na mesma revisão que criou este arquivo.

## 15. Definition Gate

Fechado **retroativamente** — nenhum item abaixo bloqueou a implementação real, que já aconteceu antes desta spec existir (ver decisão M10). Marcado com o estado real de cada item para orientar a próxima revisão deste módulo.

- [x] limites e casos de uso aprovados (seção 2/3, refletem o código real)
- [x] entidades/constraints aprovadas (seção 4, refletem a migration real)
- [x] estados/comandos aprovados (seção 5)
- [x] Policies/permissões aprovadas (`Permission.McpRegistryWrite` já na matriz)
- [x] API/eventos aprovados (seção 7/8, refletem os endpoints e tools reais)
- [x] UI e estados aprovados — `McpServersPage`/`McpServerDetailPage`/`McpAssignmentsPage` implementadas nesta revisão (seção 9); nenhuma tela de "quem está atribuído a este servidor" (a API não expõe essa consulta)
- [x] migration/rollback aprovados (seção 4, migration reversível e sem dependência externa)
- [x] testes e evidências definidos — AC-01, AC-02, AC-03, AC-05, AC-06 e o gap de `RevokedAt` (seção 12) têm teste dedicado (`McpRegistryEndpointTests`, `McpAssignmentRenderTests`, `McpAssignmentValidationTests`, `McpToolsTests.ToolsList_ReturnsAllPlannedTools`); resta a superfície MCP (tools) para AC-01/02/03 e um teste isolado de AC-04 asserindo `AuditLog SECRET_REVEAL` diretamente (ver seção 13)
- [ ] observabilidade definida — nenhuma métrica/log estruturado além de `AuditLog` (seção 11)
- [ ] nenhuma questão bloqueante aberta — duas abertas (seção 1): script de sync ausente, métricas ausentes. Os gaps de segurança das revisões anteriores (`RevokedAt` não checado em render, invariante 3 sobre `SecretParamNamesJson` não imposta) foram corrigidos nas revs 3 e 4 e não bloqueiam mais revisões futuras.

Revisão futura que queira estender este módulo (ex.: validar `SecretParamNamesJson`, construir a tela "quem está atribuído a este servidor") deve primeiro fechar as questões acima — o gap de `RevokedAt` da seção 12 já foi corrigido (rev3) e não é mais um bloqueio; os itens de cobertura de teste (AC-01 a AC-03, AC-05) e observabilidade seguem em aberto.
