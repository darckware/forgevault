# Module Specification — Lifecycle, Rotation and Revocation

## 1. Controle

```yaml
spec_id: MOD-07-LIFECYCLE-ROTATION-REVOCATION
revision: 2
status: draft
owner: unassigned
approvers: []
target_release: Fase 2
architecture_refs:
  - docs/architecture/TARGET_ARCHITECTURE.md
  - docs/architecture/IMPLEMENTATION_READINESS.md §3.7 (M14)
decisions:
  - "M14: fecha a open_blocking_question original — em vez de uma entidade CredentialBinding genérica (§80), os dois vínculos 'identidade → este secret específico' que já existem no código (SecretAccessGrant, M11; McpServerAssignment, M10) são tratados como o dependency mapping de §108/§109. Revoke em cascade (POST /secrets/{id}/revoke) e impact analysis (GET /secrets/{id}/impact, novo) usam exatamente essas duas fontes — nenhuma migration/entidade nova foi necessária. RoleAssignment é deliberadamente excluído do cascade (ver seção 4 abaixo) — revogar um secret nunca deve tirar silenciosamente o acesso de alguém a todos os outros secrets do mesmo Environment."
  - "M14: RotationPolicy e CredentialHealth (§111, validação de credencial no provider) continuam fora do escopo implementado — dependem de um catálogo de integração por provider que não existe, já registrado como fora do escopo na seção 2 desta spec."
open_blocking_questions: []
```

## 2. Objetivo e limite

- **Problema resolvido:** o MVP cria e lê secrets, mas não revoga com cascade nem rotaciona com continuidade operacional, nem sabe quem depende de qual credencial antes de uma mudança disruptiva.
- **Resultado esperado:** rotate cria nova versão validada antes de migrar consumidores e revogar a antiga; revoke suporta múltiplos níveis (access/session/lease/credential/provider/cascade); impact analysis mostra consumidores afetados antes de uma operação destrutiva.
- **Atores:** `ADMIN`, `SECURITY_ADMIN`, sistema de rotação agendada (worker).
- **Responsabilidade deste módulo:** `POST /secrets/{id}/rotate`, revoke em múltiplos níveis, `CredentialBinding`/dependency mapping, credential health.
- **Responsabilidades de outros módulos:** armazenamento/versão (`03`), quem pode disparar rotate/revoke (`04`), registro da operação (`06`).
- **Dentro do escopo:** rotate, revoke (todos os níveis de §100), dependency mapping (§108), impact analysis (§109), credential health (§111).
- **Fora do escopo:** rotação automática integrada com APIs de provider externo (`PROVIDER_API`, §23 — exige um catálogo de integrações por provider, tratado como extensão futura deste módulo); quorum/multi-approval (§137, Fase 3).

## 3. Casos de uso e processos

| ID | Ator | Gatilho | Fluxo principal | Alternativas | Resultado | Scope elements |
|---|---|---|---|---|---|---|
| UC-01 | ADMIN | Secret precisa de novo valor | `POST /secrets/{id}/rotate`: cria nova versão, valida, marca ACTIVE, migra consumidores, revoga antiga (§104) | validação da nova credencial falha → rotate abortado, versão antiga permanece ACTIVE | continuidade operacional preservada | Rotate |
| UC-02 | SECURITY_ADMIN | Credencial comprometida | `POST /credentials/{id}/revoke` com `cascade: true` | — | grants/sessions/leases revogados, auditado (§103) | Revoke |
| UC-03 | ADMIN | Antes de revogar/rotacionar | consulta impact analysis | — | lista de consumidores afetados exibida antes da confirmação | Impact analysis |

## 4. Modelo de domínio e dados

### Entidades

**Implementado (M14):** nenhuma entidade nova — o dependency mapping usa `SecretAccessGrant` (M11) e `McpServerAssignment` (M10), ambas já existentes. `RotationPolicy`/`CredentialHealth` abaixo continuam apenas especificadas, não implementadas (ver decisão M14 na seção 1).

| Entidade | Atributo | Tipo | Obrigatório | Constraint | Sensibilidade |
|---|---|---|---|---|---|
| ~~CredentialBinding~~ | — | — | — | **não implementada** — substituída por `SecretAccessGrant`/`McpServerAssignment` (decisão M14) | — |
| RotationPolicy | id, secret_id, interval_days, type (`MANUAL`\|`SCHEDULED`\|`PROVIDER_API`) | — | sim | referenciada por `Secret.rotation_policy_id` (módulo 03) | baixa |
| CredentialHealth | secret_id, status (`HEALTHY`\|`INVALID`\|`EXPIRING`\|`EXPIRED`\|`UNKNOWN`), last_validated_at, last_success_at, last_failure_at | — | sim | §111 | baixa |

### Invariantes

1. Rotate nunca revoga a versão antiga antes de validar e ativar a nova (§104). **Satisfeito** — `POST /secrets/{id}/rotate` só adiciona a versão N+1; a versão anterior nunca é revogada (decisão M6: continuidade operacional — quem já tinha a versão anterior em cache continua funcionando).
2. Revoke de credencial segue a sequência de §102, na medida do que existe: marcar `Revoked` → impedir novos retornos (`SecretEndpoints`/`VaultTools` checam `Status` antes de ler/escrever/rotacionar) → revogar `SecretAccessGrant`s → revogar `McpServerAssignment`s que referenciam o secret → auditar. Sessions/leases/cache/revogação no provedor externo não existem neste codebase (Fase 2/3 — ver módulo 05/09).
3. Revoke de um `SecretAccessGrant` ou `McpServerAssignment` isolado (via seus próprios endpoints de revoke) não revoga o secret em si para outros consumidores — **satisfeito**, já era assim desde M10/M11.

## 5. Máquinas de estados

### Estados (Credential, estendendo os já definidos no módulo 03)

Reaproveita `ACTIVE/SUSPENDED/ROTATING/EXPIRED/REVOKED/ARCHIVED` do módulo `03` — este módulo é quem efetivamente dispara as transições `ROTATING`, `REVOKED`.

### Comandos

| Comando | Ator | Precondições | Transição | Side effects | Idempotência | Erros |
|---|---|---|---|---|---|---|
| `RotateSecret` | ADMIN/SECURITY_ADMIN | Secret `ACTIVE` | `ACTIVE`→`ROTATING`→`ACTIVE` | nova SecretVersion, versão antiga marcada não-current | não idempotente (cada chamada rotaciona) | 409 se já `ROTATING` |
| `RevokeCredential` | SECURITY_ADMIN | Secret existe | qualquer → `REVOKED` | cascade conforme §102 | idempotente (revogar já revogado não erra) | 404 |
| `RevokeAccess` | ADMIN | CredentialBinding existe | `active`→`revoked` | apenas aquela identidade perde acesso | idempotente | 404 |

## 6. Policies e permissões

| Ação | Papel/authority | Aprovação humana | AuditEvent |
|---|---|---|---|
| Rotate | ADMIN, SECURITY_ADMIN | conforme risk_level (L3/L4) | `SECRET_ROTATE` |
| Revoke credencial (cascade) | SECURITY_ADMIN | recomendado para L3/L4 | `SECRET_REVOKE` |

## 7. Contratos de API

**Implementado (M14)** — os nomes de role da spec original (`ADMIN`/`SECURITY_ADMIN` como papéis distintos com risk_level L3/L4) não existem neste codebase; a autorização real é `Permission.SecretWrite` no escopo do secret (módulo 04), concedida hoje a `Owner`/`Admin`/`SecurityAdmin`/`ProjectAdmin`/`Developer`/`Operator`/`Agent`/`ServiceAccount` (ver `RolePermissions.cs`) — não há gate de aprovação humana por risk_level (L3/L4) implementado.

```yaml
operation_id: rotateSecret
method: POST
path: /api/v1/secrets/{id}/rotate
authorization: Permission.SecretWrite no escopo do secret
request_schema: {value: string}
response_schema: SecretResponse {id, environmentId, name, type, provider, description, status, currentVersion, createdAt, updatedAt, expiresAt}
errors:
  - {status: 409, code: secret_not_active, condition: secret Revoked ou Archived}
events: [SECRET_ROTATE]
```

```yaml
operation_id: revokeSecret (implementado como cascade de secret, não de "credential" genérica)
method: POST
path: /api/v1/secrets/{id}/revoke
authorization: Permission.SecretWrite no escopo do secret
request_schema: (sem corpo)
response_schema: SecretRevokeResponse — SecretResponse + {revokedAccessGrants: int, revokedMcpAssignments: int}
errors:
  - {status: 404, condition: secret inexistente}
events: [SECRET_REVOKE]
equivalente_mcp: [admin.secret.revoke]
```

```yaml
operation_id: getSecretImpact (novo, M14 — não estava na revisão 1 desta spec)
method: GET
path: /api/v1/secrets/{id}/impact
authorization: Permission.SecretWrite no escopo do secret
response_schema: {secretId, roleAssignmentConsumers: [{identityId, role}], accessGrantIdentityIds: [guid], mcpAssignmentIds: [guid]}
equivalente_mcp: [admin.secret.impact]
```

## 8. Eventos e auditoria

| Evento | Producer | Payload | Retention |
|---|---|---|---|
| `SECRET_ROTATE`, `SECRET_REVOKE` | Api | secret_id, actor, cascade flags, contagens de revogação — nunca o valor | conforme módulo 06 |

## 9. Interface

| Tela/Componente | Rota | Dados | Ações |
|---|---|---|---|
| Impact Analysis | modal em `/secrets/{id}` | lista de consumidores (§109) | confirmar rotate/revoke |
| Credential Health | coluna na lista de secrets | HEALTHY/INVALID/EXPIRING/EXPIRED/UNKNOWN | — |

## 10. Integrações e falhas

| Integração | Timeout | Retry | Falha visível |
|---|---|---|---|
| Validação de credencial no provider (§110, ex. chamada de teste à API do provider) | curto, síncrono | sem retry automático | rotate abortado, versão antiga preservada |

## 11. Observabilidade

- métricas: `vault_rotation_failures_total`, `secret_health_status` (gauge por status).

## 12. Segurança e privacidade

- nunca registrar o valor da credencial durante validação (§110).

## 13. Critérios de aceite

| ID | Given | When | Then | Nível | Evidência |
|---|---|---|---|---|---|
| AC-01 | secret ativo | rotate bem-sucedido | nova versão vira `CurrentVersion`, **versão anterior permanece legível** (não revogada — correção de wording: a redação original desta linha dizia "antiga revogada", que contradiz a decisão M6 de continuidade operacional; nenhuma indisponibilidade percebida pelos consumidores é justamente por a versão antiga não ser revogada) | Integration | já coberto desde M4/M6, sem teste dedicado novo nesta revisão |
| AC-02 | secret com 1 `SecretAccessGrant` e 1 `McpServerAssignment` ativos referenciando-o | revoke | os 2 são revogados e contabilizados em `SecretRevokeResponse` (`revokedAccessGrants`/`revokedMcpAssignments`); `RoleAssignment` no mesmo escopo permanece ativo | Security | **Satisfeito** — `SecretRevokeCascadeTests.Revoke_CascadesToAccessGrantAndMcpAssignment_ReportsCounts_ButLeavesRoleAssignmentIntact` |
| AC-03 (novo) | secret com consumidores dos três tipos | `GET /secrets/{id}/impact` antes de revogar | lista os `RoleAssignment`s com `SecretReadValue`, os `SecretAccessGrant`s e os `McpServerAssignment`s | Security | **Satisfeito** — mesmo teste do AC-02, que também chama `/impact` como parte do fluxo |

## 14. Plano de entrega

- ordem de implementação: após módulos `03`, `04`, `06` (ver `IMPLEMENTATION_READINESS.md`). Implementado no M14 (`docs/architecture/IMPLEMENTATION_READINESS.md` §3.7).
- **Fora desta revisão:** RotationPolicy/rotação agendada, CredentialHealth/validação de provider, sessions/leases (não existem), aprovação humana por risk_level.

## 15. Definition Gate

Fechado **parcialmente** para o escopo desta revisão (revoke em cascade + impact analysis), seguindo o mesmo padrão de honestidade retroativa dos módulos `09`/`11` — o restante da spec (RotationPolicy, CredentialHealth, sessions/leases) segue sem Definition Gate, porque não foi implementado.

- [x] limites e casos de uso aprovados (para o subconjunto rotate/revoke/impact — RotationPolicy/CredentialHealth continuam em aberto)
- [x] entidades/constraints aprovadas (decisão: sem entidade nova, reusa `SecretAccessGrant`/`McpServerAssignment`)
- [x] estados/comandos aprovados (rotate/revoke, seção 5)
- [x] Policies/permissões aprovadas (`SecretWrite`, já existente — nenhuma nova)
- [x] API/eventos aprovados (seção 7, refletem os endpoints reais)
- [ ] UI e estados aprovados — nenhuma UI de impact analysis/credential health construída ainda (só a API)
- [x] migration/rollback aprovados (nenhuma migration nova — decisão de reuso, reversível via `git revert` do código)
- [x] testes e evidências definidos (AC-02/AC-03, seção 13)
- [ ] observabilidade definida — `vault_rotation_failures_total`/`secret_health_status` (seção 11) não implementadas
- [x] nenhuma questão bloqueante aberta para o escopo desta revisão (fechada — ver seção 1); RotationPolicy/CredentialHealth continuam sem spec detalhada, retomar em revisão futura
