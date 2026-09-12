# ForgeVault — Contrato de Integração (MVP M7 + MCP M8)

> **Status documental:** descreve os dois mecanismos de integração realmente implementados — REST (Onda 1/M7) e MCP nativo (M8, onda 1 de tools). A visão completa (tools `access.*`/`session.*`/`approval.status`/`admin.policy.*`, propagação de contexto operacional para autorização) continua em `docs/modules/09_FORGEHUB_FORGEROUTER_MCP_INTEGRATION.md` — Fase 2/3, ainda não implementada. Este documento é o contrato **já funcional** que ForgeHub/ForgeRouter podem usar hoje, por qualquer uma das duas vias.

## 1. Identidade

Cada sistema consumidor (ForgeHub, ForgeRouter, ou qualquer outro serviço) deve ter sua própria `ServiceAccount` no ForgeVault — nunca reutilizar uma identidade humana (`docs/ForgeVault.md` §31).

Provisionamento hoje (sem UI/CLI de administração ainda — mesma limitação já registrada para `RoleAssignment` no M5):

```http
POST /api/v1/service-accounts
Authorization: Bearer <jwt de um usuário humano autenticado>
Content-Type: application/json

{"name": "forgehub"}
```

```http
POST /api/v1/service-accounts/{id}/tokens
Authorization: Bearer <jwt de um usuário humano autenticado>
```

A resposta retorna o token bruto (`fv_sa_...`) **uma única vez** — armazene-o imediatamente no cofre de secrets do próprio sistema consumidor (ironia reconhecida: um secret manager como o ForgeVault não pode resolver o problema de "onde ForgeHub guarda o token que usa para falar com o ForgeVault" — isso é responsabilidade operacional do ambiente onde ForgeHub roda).

## 2. Autenticação

Toda chamada subsequente usa o token como Bearer, exatamente como um usuário humano faria com seu JWT:

```http
GET /api/v1/secrets
Authorization: Bearer fv_sa_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
```

O ForgeVault distingue automaticamente um token de serviço (`fv_sa_...`) de um JWT humano pelo prefixo — não é necessário nenhum header adicional. MFA (`RequireMfa`) nunca se aplica a tokens de serviço: eles não têm claim `mfa_enabled`, então o handler os trata como não sujeitos a MFA.

## 3. Autorização

Uma `ServiceAccount` só acessa o que um `RoleAssignment` conceder a ela — exatamente o mesmo modelo RBAC de um usuário humano (`docs/modules/04_AUTHORIZATION_AND_POLICY.md`). Não há atalho de "confiar automaticamente em serviços". Hoje isso também exige inserção direta no banco (mesma limitação do M5):

```sql
INSERT INTO role_assignments (id, identity_id, role, scope_type, scope_id, created_at)
VALUES (gen_random_uuid(), '<service_account.id>', 'ServiceAccount', 'Organization', '<organization.id>', now());
```

Recomendação: conceder o menor escopo possível (Environment, não Organization) para uma integração de produção real — o exemplo acima usa Organization por brevidade.

## 4. Fluxo ForgeHub (autenticação)

ForgeHub autentica como `service:forgehub` para qualquer chamada que precise validar identidade/contexto de projeto no ForgeVault — via REST ou via MCP, mesmo token `fv_sa_...`, mesmo `SmartAuth`. Desde M8, uma chamada MCP a `credential.request` pode incluir `taskId`/`onBehalfOfAgent`/`runtimeSessionRef` (ver seção 7) — mas isso é metadado de auditoria, não entrada de autorização. `project_id` propagado para decisão de RBAC/ABAC por task/agente continua Fase 3 — hoje o ForgeVault avalia apenas RBAC por identidade/escopo, como antes do M8.

## 5. Fluxo ForgeRouter (recuperar credencial)

```http
GET /api/v1/secrets/{id}/value?mode=REVEAL
Authorization: Bearer fv_sa_<token do service:forgerouter>
```

Resposta (200, se autorizado):

```json
{
  "requestId": "...",
  "identity": "<service_account.id>",
  "resource": "<secret.id>",
  "accessMode": "REVEAL",
  "expiresAt": null,
  "credentials": {"value": "sk-..."},
  "session": null,
  "broker": null,
  "lease": null,
  "metadata": {}
}
```

ForgeRouter nunca deve persistir esse valor localmente além do necessário para a chamada em andamento (`docs/ForgeVault.md` §37, §94) — a cada uso, solicitar de novo ao ForgeVault, ou fazer cache em memória com TTL curto (segundos a poucos minutos), nunca em disco.

## 7. Fluxo via MCP (M8)

Alternativa ao REST para clientes MCP (agentes, Hermes, ou o próprio ForgeHub/ForgeRouter) — mesmo host, mesma autenticação `SmartAuth`, mesmas regras de RBAC/auditoria aplicadas dentro de cada tool (não via `[Authorize(Policy=...)]` do ASP.NET, que não modela autorização por recurso+escopo):

```http
POST /mcp
Authorization: Bearer <jwt humano ou fv_sa_... de serviço>
Content-Type: application/json
Accept: application/json, text/event-stream

{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{
  "name":"credential.request",
  "arguments":{"secretId":"<guid>","taskId":"task-42","onBehalfOfAgent":"agent-hermes"}
}}
```

Tools disponíveis hoje: `secret.metadata`, `credential.request` (só `REVEAL`), `capability.check`, `admin.secret.create/update/rotate/revoke`, `admin.audit.search` — ver `docs/modules/09_FORGEHUB_FORGEROUTER_MCP_INTEGRATION.md` §7 para a lista completa incluindo o que ainda não existe. Erros de negócio/autorização chegam como `CallToolResult` com `isError: true` e a mensagem em `content[0].text` (lançar `McpException` dentro da tool, não deixar vazar outra exceção).

## 6. O que ainda não existe (Fase 2/3+)

- Tools MCP de `access.*`/`session.*`/`approval.status`/`admin.policy.*` e modos de acesso além de REVEAL (BROKER/SESSION/LEASE/INJECT) — dependem de `CredentialRequest`/`AccessGrant`/`Session`/`Lease`/`Approval`/Policy Engine, que não existem.
- Propagação de contexto operacional (task/project) do ForgeHub para *decisões* de autorização (ABAC) — hoje `taskId`/`onBehalfOfAgent`/`runtimeSessionRef` só chegam como metadado de auditoria (M8, seção 7 acima).
- Endpoint de gestão de `RoleAssignment`/`ServiceAccount` via UI — hoje é inserção direta no banco.
- `correlation_id` propagado ponta a ponta entre Hermes → ForgeHub → ForgeVault → terceiro (`docs/ForgeVault.md` §113) — o `AuditLog` já tem a coluna, mas nada a preenche ainda.
- Métricas `mcp_calls_total`/`credential_requests_total` (módulo 09 §11) — não implementadas neste marco.
