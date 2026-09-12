# Module Specification — ForgeHub/ForgeRouter Integration and MCP Server

## 1. Controle

```yaml
spec_id: MOD-09-INTEGRATION-MCP
revision: 2
status: draft
owner: unassigned
approvers: []
target_release: Fase 2
architecture_refs:
  - docs/architecture/TARGET_ARCHITECTURE.md
  - docs/architecture/IMPLEMENTATION_READINESS.md §3.1 (M8)
decisions:
  - "M8: contrato de contexto ForgeHub -> ForgeVault fechado por revisão direta do código real do ForgeHub (não da sua arquitetura-alvo). ProjectTask não expõe project_id diretamente (requer join via PlanningItem/ChangeRequest), e não existe correlation-id propagado hoje. O contexto real e disponível é task_id + identidade do agente (onBehalfOfAgent) + TaskExecution.runtime_session_ref. Esses três campos são aceitos como argumentos OPCIONAIS de credential.request, gravados em AuditLog.Metadata apenas para rastreabilidade — nunca usados para autorização. ABAC por task/agente permanece Fase 3 (nenhuma mudança de escopo, apenas confirma o que o roadmap já previa)."
  - "M8: ForgeHub/ForgeRouter autenticam no MCP Server exatamente como na REST API — como service:forgehub/service:forgerouter via Service Accounts (mecanismo já existente desde M7, token fv_sa_...). Não foi criado nenhum mecanismo de autenticação MCP-específico; o AgentServiceCredential (token agt_...) que o ForgeHub usa para chamar a si mesmo não é reaproveitado pelo ForgeVault."
  - "M8: onda 1 de tools implementada com escopo deliberadamente restrito às que já têm lógica de backend real: secret.metadata, credential.request (só REVEAL), capability.check, admin.secret.create/update/rotate/revoke, admin.audit.search. As demais tools listadas na seção 7 (credential.status/release, access.*, session.*, approval.status, admin.policy.*) continuam Fase 2/3 — dependem de CredentialRequest/AccessGrant/Session/Lease/Approval, que não existem ainda."
open_blocking_questions: []
```

## 2. Objetivo e limite

- **Problema resolvido:** o MVP tem uma API REST utilizável, mas nenhuma integração real com os sistemas irmãos nem um MCP Server nativo, que é o caminho arquiteturalmente preferido para agentes (§90).
- **Resultado esperado:** ForgeHub autentica como `service:forgehub` e usa o contexto operacional da task para autorizar pedidos de credencial; ForgeRouter recupera credenciais de provider de LLM sem persistir localmente; agentes usam o MCP Server do ForgeVault em vez de REST cru.
- **Atores:** `service:forgehub`, `service:forgerouter`, agentes via MCP client identity.
- **Dentro do escopo:** Service Accounts (§31-32), MCP Server nativo e suas tools (§90-91), fluxo de autorização contextual usando dados do ForgeHub (§93).
- **Fora do escopo:** qualquer mudança no ForgeHub/ForgeRouter em si — este módulo especifica apenas o lado ForgeVault do contrato.

## 3. Casos de uso e processos

| ID | Ator | Gatilho | Fluxo principal | Resultado |
|---|---|---|---|---|
| UC-01 | ForgeHub | Agente precisa de um secret durante uma task | ForgeHub encaminha a solicitação ao ForgeVault com `task_id/project_id/assigned_agent` | ForgeVault autoriza com base no contexto e retorna via modo de acesso apropriado | Integração |
| UC-02 | ForgeRouter | Precisa de API Key de um provider de LLM | Autentica como `service:forgerouter`, chama `GET /secrets/{id}/value` (módulo 05) | credencial retornada, nunca persistida localmente pelo ForgeRouter | Integração |
| UC-03 | Agente via MCP | Precisa de uma credencial | chama `credential.request` no MCP Server do ForgeVault | ForgeVault aplica RBAC/ABAC e retorna conforme modo de acesso | MCP |

## 4. Modelo de domínio e dados

### Entidades novas

| Entidade | Atributo | Descrição |
|---|---|---|
| ServiceAccount | id, name (`service:forgehub`, `service:forgerouter`, ...), token (`fv_service_...`) | reaproveita o modelo de identidade do módulo `02`, com `type=service` |
| McpClientIdentity | id, name (`mcp:hermes-athos`, `mcp:forgehub`, ...) | identidade adicional para clientes MCP (§74) |

### Invariantes

1. Tokens de agente e tokens de serviço nunca são compartilhados entre si (§76).
2. Tools administrativas do MCP (`admin.secret.*`, `admin.policy.*`, `admin.audit.search`) nunca ficam disponíveis para identidades de agente comum — apenas para identidades explicitamente marcadas como administrativas.
3. `get_all_secrets`/`dump_vault`/`export_credentials` nunca existem como tools MCP (§91).

## 5. Máquinas de estados

Reaproveita o ciclo de vida de token do módulo `02` para Service Accounts e MCP clients.

## 6. Policies e permissões

| Ação | Papel/authority | AuditEvent |
|---|---|---|
| `service:forgehub` solicita credencial em nome de um agente/task | validado por contexto (task/project/environment) + RBAC do módulo 04 | `MCP_CALL` / `CREDENTIAL_REQUEST` |
| Tool administrativa MCP | apenas identidade com role `ADMIN`/`SECURITY_ADMIN` | `MCP_CALL` |

## 7. Contratos de API

Endpoint MCP (§90) — implementado no M8, mesmo processo/host da API REST:
```text
POST /mcp  (Streamable HTTP, stateless; RequireAuthorization — mesmo SmartAuth do M7)
```

Tools padrão (§91) — **implementadas no M8** salvo indicação contrária:
```text
capability.check
credential.request          (só accessMode=REVEAL; BROKER/SESSION/LEASE/INJECT — Fase 2/3, NÃO implementado)
secret.metadata
```

Tools **NÃO implementadas** (dependem de `CredentialRequest`/`AccessGrant`/`Session`/`Lease`/`Approval`, que não existem — Fase 2/3):
```text
capability.list
credential.status
credential.release
access.request
access.status
access.release
session.request
session.status
session.close
approval.status
```

Tools administrativas — **implementadas no M8**:
```text
admin.secret.create
admin.secret.update
admin.secret.rotate
admin.secret.revoke          (novo em M8: antes disso Secret.Status nunca era definido como Revoked)
admin.audit.search           (novo em M8, junto de GET /api/v1/audit e Permission.AuditRead)
```

Tools administrativas **NÃO implementadas** (dependem de um modelo de Policy que não existe — Fase 2/3):
```text
admin.policy.create
admin.policy.update
admin.policy.assign
```

## 8. Eventos e auditoria

| Evento | Producer | Payload | Retention |
|---|---|---|---|
| `MCP_CALL` | MCP Server | tool, identity, resource, result — nunca o valor | conforme módulo 06 |
| `CREDENTIAL_REQUEST` | Api/MCP | identity, task_id, resource, resultado (Grant/ApprovalRequest/Denied, §99) | conforme módulo 06 |

Correlation ID (§113) deve ser propagado de ponta a ponta: Hermes → ForgeHub → ForgeVault → terceiro.

## 9. Interface

Nenhuma tela nova — este módulo é inteiramente API/MCP. Visibilidade de Service Accounts/MCP clients reaproveita a tela de identidades do módulo `02`.

## 10. Integrações e falhas

| Integração | Ownership | Timeout | Retry | Falha visível |
|---|---|---|---|---|
| ForgeHub (contexto operacional) | ForgeHub | curto | idempotency key recomendada | pedido negado com motivo claro, nunca silencioso |
| ForgeRouter (consumo de credencial) | ForgeRouter | curto | sem retry automático em 401/403 | erro propagado ao chamador do ForgeRouter |

## 11. Observabilidade

- métricas: `mcp_calls_total` por tool, `credential_requests_total` por resultado (Grant/Approval/Denied).

## 12. Segurança e privacidade

- MCP Server é uma interface oficial (§160 princípio 17), nunca um caminho paralelo que ignore RBAC/ABAC do módulo `04`.

## 13. Critérios de aceite

| ID | Given | When | Then | Nível | Status |
|---|---|---|---|---|---|
| AC-01 | identidade autenticada com `SecretReadValue` no escopo do secret, opcionalmente com `task_id`/`onBehalfOfAgent`/`runtimeSessionRef` | chama `credential.request` via MCP | valor retornado (modo REVEAL), `AuditLog` com ação `SECRET_REVEAL` gravado incluindo o contexto opcional em `Metadata` | Integration | **Satisfeito** — `McpToolsTests.AdminSecretCreate_ThenCredentialRequest_Authorized_RevealsValue_AndAudits` |
| AC-02 | identidade autenticada sem `SecretReadValue` no escopo do secret | chama `credential.request` via MCP | negado (`McpException("forbidden")`), `AuditLog` com ação `FAILED_ACCESS` gravado, nenhuma resposta ou log contém o valor | Security | **Satisfeito** — `McpToolsTests.CredentialRequest_Unauthorized_Denies_AndAudits_NoLeak` |
| AC-03 | identidade sem `AuditRead` em nenhum escopo (ex.: role `ReadOnly`) | tenta chamar a tool administrativa `admin.audit.search` | negado (`McpException("forbidden")`) | Security | **Satisfeito** — `McpToolsTests.AdminAuditSearch_DeniesReadOnlyRole_AllowsAuditor_NeverLeaksSecretValues` |

`service:forgehub`/`service:forgerouter` autenticando via Service Account e recuperando credencial já está coberto desde M7 (`ServiceAccountIntegrationTests`) — a via MCP reaproveita a mesma autenticação (`SmartAuth`), então nenhum teste adicional específico de identidade de serviço foi necessário além dos três acima.

## 14. Plano de entrega

- ordem de implementação: depende dos módulos `02`, `04`, `05`, `06` estarem estáveis (ver `IMPLEMENTATION_READINESS.md`).

## 15. Definition Gate

Status após M8 — cobre apenas a onda 1 de tools (seção 7); a fatia restante do módulo (Policy Engine, Session/Lease/Approval e as tools que dependem delas) permanece com gate fechado, sem implementação.

- [x] limites e casos de uso aprovados (onda 1 de tools; UC-01/02 completos via Service Accounts existentes, UC-03 completo)
- [x] entidades/constraints aprovadas (nenhuma entidade nova — reaproveita Secret/AuditLog/RoleAssignment)
- [x] estados/comandos aprovados (reaproveita o ciclo de vida de Service Account do módulo 02)
- [x] Policies/permissões aprovadas (`Permission.AuditRead` adicionado à matriz existente do módulo 04)
- [x] API/eventos aprovados (seção 7 e 8 refletem o que existe)
- [x] UI e estados aprovados (nenhuma UI nova, conforme seção 9)
- [x] migration/rollback aprovados (nenhuma migration de schema — `AuditRead` é lógica de aplicação, não coluna nova)
- [x] testes e evidências definidos (`McpToolsTests.cs`, `AuditAndRevokeEndpointTests.cs` — ver seção 13)
- [ ] observabilidade definida (`mcp_calls_total`/`credential_requests_total` da seção 11 não foram implementadas neste marco)
- [x] nenhuma questão bloqueante aberta (fechada no `revision: 2` — ver seção 1)
