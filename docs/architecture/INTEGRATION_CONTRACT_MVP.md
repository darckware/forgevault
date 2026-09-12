# ForgeVault — Contrato de Integração (MVP M7 + MCP M8 + Gestão de Acesso M9)

> **Status documental:** descreve os mecanismos de integração realmente implementados — REST (Onda 1/M7), MCP nativo (M8, onda 1 de tools) e gestão de `RoleAssignment`/onboarding de agente via API/MCP (M9). A visão completa (tools `access.*`/`session.*`/`approval.status`/`admin.policy.*`, propagação de contexto operacional para autorização) continua em `docs/modules/09_FORGEHUB_FORGEROUTER_MCP_INTEGRATION.md` — Fase 2/3, ainda não implementada. Este documento é o contrato **já funcional** que ForgeHub/ForgeRouter podem usar hoje, por qualquer uma das vias.

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

Uma `ServiceAccount` só acessa o que um `RoleAssignment` conceder a ela — exatamente o mesmo modelo RBAC de um usuário humano (`docs/modules/04_AUTHORIZATION_AND_POLICY.md`). Não há atalho de "confiar automaticamente em serviços".

Desde o M9, conceder esse `RoleAssignment` não exige mais inserção direta no banco — um Owner/Admin (humano ou outra `ServiceAccount` com `RoleAssignmentWrite`) faz isso via API ou MCP:

```http
POST /api/v1/identities/{service_account.id}/role-assignments
Authorization: Bearer <jwt de um Owner ou Admin>
Content-Type: application/json

{"role": "ServiceAccount", "scopeType": "Environment", "scopeId": "<environment.id>"}
```

Equivalente via MCP: tool `admin.role.grant` com os mesmos campos (`identityId`, `role`, `scopeType`, `scopeId`) — ver seção 7.

Recomendação: conceder o menor escopo possível (Environment, não Organization) para uma integração de produção real — o exemplo acima já segue essa recomendação.

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

Tools disponíveis hoje: `secret.metadata`, `credential.request` (só `REVEAL`), `capability.check`, `admin.secret.create/update/rotate/revoke`, `admin.audit.search`, `admin.role.grant/revoke` e `admin.agent.register` (M9) — ver `docs/modules/09_FORGEHUB_FORGEROUTER_MCP_INTEGRATION.md` §7 para a lista completa incluindo o que ainda não existe. Erros de negócio/autorização chegam como `CallToolResult` com `isError: true` e a mensagem em `content[0].text` (lançar `McpException` dentro da tool, não deixar vazar outra exceção).

### Onboarding de agente em uma chamada (M9)

Um Owner/Admin registra um agente novo — identidade, token e permissão — em uma única
chamada MCP, sem inserção manual no banco:

```json
{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{
  "name":"admin.agent.register",
  "arguments":{"name":"agent-athos","scopeType":"Environment","scopeId":"<environment.id>"}
}}
```

Resposta inclui `token` (`fv_sa_...`, mostrado uma única vez — o agente deve guardá-lo
imediatamente). A partir daí o próprio agente, com seu próprio token, chama
`admin.secret.create` para cadastrar as credenciais que já tinha em mãos, e
`credential.request` para recuperá-las depois. O agente nunca recebe `RoleAssignmentWrite`
neste fluxo — não pode conceder papéis a si mesmo nem a ninguém.

## 6. O que ainda não existe (Fase 2/3+)

- Tools MCP de `access.*`/`session.*`/`approval.status`/`admin.policy.*` e modos de acesso além de REVEAL (BROKER/SESSION/LEASE/INJECT) — dependem de `CredentialRequest`/`AccessGrant`/`Session`/`Lease`/`Approval`/Policy Engine, que não existem.
- Propagação de contexto operacional (task/project) do ForgeHub para *decisões* de autorização (ABAC) — hoje `taskId`/`onBehalfOfAgent`/`runtimeSessionRef` só chegam como metadado de auditoria (M8, seção 7 acima).
- Gestão de `RoleAssignment`/`ServiceAccount` via **UI** — via API/MCP já existe desde o M9 (seção 3 e 7 acima); só a tela mesmo não foi construída.
- Hierarquia entre roles ao conceder acesso — `RoleAssignmentWrite` (M9) não impede um Owner/Admin de conceder qualquer role, incluindo Owner, no escopo onde tem a permissão (ver `docs/modules/04_AUTHORIZATION_AND_POLICY.md` §1, decisão registrada).
- `correlation_id` propagado ponta a ponta entre Hermes → ForgeHub → ForgeVault → terceiro (`docs/ForgeVault.md` §113) — o `AuditLog` já tem a coluna, mas nada a preenche ainda.
- Métricas `mcp_calls_total`/`credential_requests_total` (módulo 09 §11) — não implementadas neste marco.
