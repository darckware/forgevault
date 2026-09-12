# ForgeVault — Contrato de Integração (MVP, M7)

> **Status documental:** descreve o mecanismo de integração REST realmente implementado na Onda 1 (MVP). A visão completa de integração (MCP Server nativo, tools `credential.request`/`access.request`, contexto operacional propagado pelo ForgeHub) é `docs/modules/09_FORGEHUB_FORGEROUTER_MCP_INTEGRATION.md` — Fase 2, ainda não implementada. Este documento é o contrato **mínimo e já funcional** que ForgeHub/ForgeRouter podem usar hoje.

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

ForgeHub autentica como `service:forgehub` para qualquer chamada que precise validar identidade/contexto de projeto no ForgeVault. Na Onda 1, isso se resume a: obter um token de serviço e conseguir chamar qualquer endpoint autenticado. A propagação de contexto operacional (`task_id`, `project_id`, `assigned_agent`) descrita em `docs/ForgeVault.md` §93 e no módulo 09 é Fase 2 — hoje o ForgeVault não recebe nem avalia esse contexto, apenas RBAC por identidade/escopo.

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

## 6. O que ainda não existe (Fase 2+)

- MCP Server nativo (`docs/modules/09_FORGEHUB_FORGEROUTER_MCP_INTEGRATION.md`) — hoje é só REST puro.
- Propagação de contexto operacional (task/project) do ForgeHub para decisões de autorização.
- Modos de acesso além de REVEAL (BROKER/SESSION/LEASE/INJECT).
- Endpoint de gestão de `RoleAssignment`/`ServiceAccount` via UI — hoje é inserção direta no banco.
- `correlation_id` propagado ponta a ponta entre Hermes → ForgeHub → ForgeVault → terceiro (`docs/ForgeVault.md` §113) — o `AuditLog` já tem a coluna, mas nada a preenche ainda.
