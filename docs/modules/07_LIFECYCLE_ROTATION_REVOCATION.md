# Module Specification — Lifecycle, Rotation and Revocation

## 1. Controle

```yaml
spec_id: MOD-07-LIFECYCLE-ROTATION-REVOCATION
revision: 1
status: draft
owner: unassigned
approvers: []
target_release: Fase 2
architecture_refs:
  - docs/architecture/TARGET_ARCHITECTURE.md
decisions: []
open_blocking_questions:
  - "Modelo exato de CredentialBinding/dependency mapping (§108) não foi detalhado nesta revisão — depende de como agentes/serviços passam a consumir secrets no módulo 09"
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

### Entidades (novas em relação aos módulos 01-06)

| Entidade | Atributo | Tipo | Obrigatório | Constraint | Sensibilidade |
|---|---|---|---|---|---|
| CredentialBinding | id, identity_id, credential_id, permission, conditions, issued_at, expires_at, status | — | sim | liga identidade a secret/credencial (§80) | baixa |
| RotationPolicy | id, secret_id, interval_days, type (`MANUAL`\|`SCHEDULED`\|`PROVIDER_API`) | — | sim | referenciada por `Secret.rotation_policy_id` (módulo 03) | baixa |
| CredentialHealth | secret_id, status (`HEALTHY`\|`INVALID`\|`EXPIRING`\|`EXPIRED`\|`UNKNOWN`), last_validated_at, last_success_at, last_failure_at | — | sim | §111 | baixa |

### Invariantes

1. Rotate nunca revoga a versão antiga antes de validar e ativar a nova (§104).
2. Revoke de credencial segue a sequência de §102: marcar REVOKED → impedir novos retornos → revogar grants → revogar leases → encerrar sessões → invalidar caches → revogar no provedor externo quando suportado → auditar.
3. Revoke de acesso (`CredentialBinding`) não revoga a credencial em si para outros consumidores (§101).

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

```yaml
operation_id: rotateSecret
method: POST
path: /api/v1/secrets/{id}/rotate
authorization: role in [ADMIN, SECURITY_ADMIN]
request_schema: {value: string}
response_schema: {id, new_version, status}
errors:
  - {status: 409, code: ROTATION_IN_PROGRESS, condition: já existe rotação em andamento}
events: [SECRET_ROTATE]
```

```yaml
operation_id: revokeCredential
method: POST
path: /api/v1/credentials/{id}/revoke
authorization: role SECURITY_ADMIN
request_schema: {cascade: bool, provider_revoke: bool, reason: string}
response_schema: {operation: "revoke", resource, status: "REVOKED", provider_revocation, revoked_grants, revoked_sessions, revoked_leases}
errors:
  - {status: 404, code: CREDENTIAL_NOT_FOUND, condition: credencial inexistente}
events: [SECRET_REVOKE]
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

| ID | Given | When | Then | Nível |
|---|---|---|---|---|
| AC-01 | secret ativo | rotate bem-sucedido | nova versão ACTIVE, antiga revogada, nenhuma indisponibilidade percebida pelos consumidores | Integration |
| AC-02 | credencial com 3 consumidores | revoke com cascade | os 3 grants/sessions são revogados e contabilizados na resposta | Integration |

## 14. Plano de entrega

- ordem de implementação: após módulos `03`, `04`, `06` (ver `IMPLEMENTATION_READINESS.md`).

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
