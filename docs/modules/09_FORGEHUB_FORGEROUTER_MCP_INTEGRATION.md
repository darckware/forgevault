# Module Specification — ForgeHub/ForgeRouter Integration and MCP Server

## 1. Controle

```yaml
spec_id: MOD-09-INTEGRATION-MCP
revision: 1
status: draft
owner: unassigned
approvers: []
target_release: Fase 2
architecture_refs:
  - docs/architecture/TARGET_ARCHITECTURE.md
decisions: []
open_blocking_questions:
  - "Contrato exato de contexto que o ForgeHub envia (task_id/project_id/assigned_agent/...) depende de uma revisão conjunta com o repositório forgehub — não fechado nesta revisão"
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

Endpoint MCP (§90):
```text
https://vault.darckware.net/mcp
```

Tools padrão (§91):
```text
capability.list
capability.check
credential.request
credential.status
credential.release
access.request
access.status
access.release
session.request
session.status
session.close
approval.status
secret.metadata
```

Tools administrativas:
```text
admin.secret.create
admin.secret.update
admin.secret.rotate
admin.secret.revoke
admin.policy.create
admin.policy.update
admin.policy.assign
admin.audit.search
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

| ID | Given | When | Then | Nível |
|---|---|---|---|---|
| AC-01 | `service:forgehub` autenticado | solicita credencial com contexto de task válido | autorização avaliada com base no contexto, resultado auditado | Integration |
| AC-02 | `service:forgerouter` autenticado | solicita API Key de provider | credencial retornada, nenhuma persistência local exigida do lado ForgeVault | Integration |
| AC-03 | agente comum via MCP | tenta chamar uma tool administrativa | negado | Security |

## 14. Plano de entrega

- ordem de implementação: depende dos módulos `02`, `04`, `05`, `06` estarem estáveis (ver `IMPLEMENTATION_READINESS.md`).

## 15. Definition Gate

- [ ] limites e casos de uso aprovados
- [ ] entidades/constraints aprovadas
- [ ] estados/comandos aprovados
- [ ] Policies/permissões aprovadas
- [ ] API/eventos aprovados
- [ ] UI e estados aprovados
- [ ] migration/rollback aprovados
- [ ] testes e evidências definidos
- [ ] observabilidade definida
- [ ] nenhuma questão bloqueante aberta (**há uma aberta — ver seção 1**)
