# Module Specification — Authorization and Policy

## 1. Controle

```yaml
spec_id: MOD-04-AUTHZ-POLICY
revision: 1
status: draft
owner: unassigned
approvers: []
target_release: MVP (Onda 1)
architecture_refs:
  - docs/architecture/TARGET_ARCHITECTURE.md
decisions:
  - "ABAC completo (contexto de risco, rede, tempo) é aprofundado na Fase 2; MVP cobre RBAC + o campo risk_level como metadado, sem motor de política dinâmico"
open_blocking_questions:
  - "Definir se o RBAC do MVP usa policies declarativas em tabela (policies) ou atributos fixos por role em código — não decidido nesta revisão"
```

## 2. Objetivo e limite

- **Problema resolvido:** sem este módulo, qualquer identidade autenticada poderia ler/escrever qualquer secret.
- **Resultado esperado:** toda operação sobre Organization/Project/Environment/Secret é checada contra um papel (role) atribuído à identidade; acesso negado retorna 403 e é auditado.
- **Atores:** todas as identidades definidas no módulo `02`.
- **Responsabilidade deste módulo:** roles, atribuição de role por escopo (organization/project/environment), verificação de permissão.
- **Responsabilidades de outros módulos:** identidade em si (`02`), o recurso protegido (`03`), o registro de auditoria da decisão (`06`).
- **Dentro do escopo:** roles fixos de `ForgeVault.md` §16, atribuição de role, middleware/policy de autorização.
- **Fora do escopo:** ABAC dinâmico completo (§17-18, aprofundado na Fase 2), quorum/multi-approval (§137, Fase 3), Policy Simulation (§138, Fase 3).

## 3. Casos de uso e processos

| ID | Ator | Gatilho | Fluxo principal | Alternativas | Resultado | Scope elements |
|---|---|---|---|---|---|---|
| UC-01 | ADMIN | Onboarding de usuário/agente | Atribui role a uma identidade em um escopo (organization/project/environment) | role inexistente → 422 | `RoleAssignment` criado | Roles |
| UC-02 | Qualquer identidade | Chamada a endpoint protegido | Middleware resolve roles efetivos no escopo do recurso e decide ALLOW/DENY | sem role no escopo → DENY | 200 ou 403 | Policy check |
| UC-03 | ADMIN | Revisão de acesso | Lista roles atribuídos a uma identidade/escopo | — | visão de quem tem acesso a quê | Roles |

## 4. Modelo de domínio e dados

### Entidades

| Entidade | Atributo | Tipo | Obrigatório | Default | Constraint | Sensibilidade |
|---|---|---|---|---|---|---|
| Role | id | UUID | sim | gerado | PK | baixa |
| | name | string | sim | — | enum (§16: OWNER, ADMIN, SECURITY_ADMIN, PROJECT_ADMIN, DEVELOPER, OPERATOR, AUDITOR, READ_ONLY, AGENT, SERVICE_ACCOUNT) | baixa |
| RoleAssignment | id | UUID | sim | gerado | PK | baixa |
| | identity_id | UUID | sim | — | FK→identidade (módulo 02) | baixa |
| | role_id | UUID | sim | — | FK→Role | baixa |
| | scope_type | string | sim | — | enum (organization, project, environment) | baixa |
| | scope_id | UUID | sim | — | referencia a entidade do escopo | baixa |

### Relações

| Origem | Relação | Destino | Cardinalidade | On delete | Regra cross-domain |
|---|---|---|---|---|---|
| Identity (módulo 02) | possui | RoleAssignment | 1:N | cascade | — |
| RoleAssignment | referencia | Organization/Project/Environment (módulo 01) | N:1 | cascade se o escopo for removido | validado por `scope_type` |

### Invariantes

1. Uma identidade sem `RoleAssignment` em um escopo não tem nenhuma permissão nesse escopo (default deny).
2. `DENY` explícito sempre prevalece sobre `ALLOW` quando existirem múltiplas atribuições conflitantes (§95, herdado do modelo-alvo mesmo sem ABAC completo no MVP).
3. Role `READ_ONLY` nunca autoriza escrita, revelação de valor ou mudança de permissão.

### Migração e backfill

- Estado anterior: módulos `01`-`03`.
- Transformação: adiciona `roles` (seed com os 10 valores fixos de §16), `role_assignments`.
- Backfill: o administrador criado pelo bootstrap (`01`) recebe `OWNER` na Organization inicial automaticamente.
- Rollback/roll-forward: reversão padrão.

## 5. Máquinas de estados

### Estados (RoleAssignment)

| Estado | Significado | Entradas permitidas | Saídas permitidas | Terminal? |
|---|---|---|---|---|
| `active` | concede a permissão do role no escopo | criação | `revoked` | não |
| `revoked` | não concede mais nada | — | — | sim |

### Comandos

| Comando | Ator | Precondições | Transição | Side effects | Idempotência | Erros |
|---|---|---|---|---|---|---|
| `AssignRole` | ADMIN, OWNER | identidade e escopo existem | — → `active` | nenhum | por (identity, role, scope) | 404 identidade/escopo, 422 role inválido |
| `RevokeRoleAssignment` | ADMIN, OWNER | assignment existe e `active` | `active` → `revoked` | acesso é negado na próxima chamada (imediato, não cacheado além do TTL de sessão) | idempotente | 404 |
| `CheckPermission` (interno) | qualquer requisição autenticada | — | sem transição | decide ALLOW/DENY | idempotente | N/A (não é endpoint público) |

## 6. Policies e permissões

| Ação | Papel/authority | Policy | Aprovação humana | Separation of duties | AuditEvent |
|---|---|---|---|---|---|
| Atribuir/revogar role | `OWNER`, `ADMIN` | RBAC | não (MVP) | um usuário não pode se auto-promover a `OWNER` sem já ser `OWNER` | `ACCESS_GRANTED` / `ACCESS_REVOKED` |
| Ler secret (`secret.read.value`) | qualquer role com permissão explícita | RBAC + `04` verifica escopo do Environment | conforme risk_level (L3/L4 exige aprovação — dependência futura do módulo `07`) | não | `SECRET_READ` (auditado pelo módulo 06) |

## 7. Contratos de API

```yaml
operation_id: assignRole
method: POST
path: /api/v1/identities/{identityId}/role-assignments
authorization: role in [OWNER, ADMIN]
idempotency: por (identity_id, role_id, scope_type, scope_id)
request_schema: {role: string, scope_type: "organization|project|environment", scope_id: uuid}
response_schema: {id, identity_id, role, scope_type, scope_id, status}
errors:
  - {status: 404, code: SCOPE_NOT_FOUND, condition: escopo inexistente}
  - {status: 422, code: INVALID_ROLE, condition: role fora da lista permitida}
events: [ACCESS_GRANTED]
```

```yaml
operation_id: revokeRoleAssignment
method: POST
path: /api/v1/role-assignments/{id}/revoke
authorization: role in [OWNER, ADMIN]
idempotency: idempotente (revogar já revogado não é erro)
request_schema: {}
response_schema: {id, status: "revoked"}
errors:
  - {status: 404, code: ASSIGNMENT_NOT_FOUND, condition: assignment inexistente}
events: [ACCESS_REVOKED]
```

## 8. Eventos e auditoria

| Evento | Producer | Payload version | Consumer | Idempotency key | Retention |
|---|---|---|---|---|---|
| `ACCESS_GRANTED` / `ACCESS_REVOKED` | Api | v1 | AuditLog (módulo 06) | assignment_id | conforme módulo 06 |
| `FAILED_ACCESS` (403 emitido) | Api (middleware) | v1 | AuditLog | request_id | conforme módulo 06 |

## 9. Interface

| Tela/Componente | Rota | Atores | Dados | Ações | Policies |
|---|---|---|---|---|---|
| Gestão de Acessos | `/identities/{id}/access` | OWNER, ADMIN | roles atribuídos por escopo | atribuir, revogar | RBAC |

## 10. Integrações e falhas

| Integração | Ownership | Timeout | Retry | Circuit/reconciliation | Falha visível | Fonte de verdade |
|---|---|---|---|---|---|---|
| Nenhuma externa — este módulo é puramente interno | — | — | — | — | — | PostgreSQL |

## 11. Observabilidade

- logs estruturados: decisões de DENY (nunca de ALLOW em detalhe, para não vazar padrão de acesso desnecessariamente em log de nível baixo — a decisão em si vai para AuditLog via módulo 06).
- métricas: `authz_denied_total`, `role_assignments_total`.

## 12. Segurança e privacidade

- threat model: escalonamento de privilégio (mitigado por checagem em toda operação sensível + teste de segurança dedicado), IDOR entre organizações (mitigado por `scope_id` sempre validado contra o recurso solicitado).
- least privilege: nenhuma identidade recebe role além do necessário por padrão (default deny).

## 13. Critérios de aceite

| ID | Given | When | Then | Nível do teste | Evidência |
|---|---|---|---|---|---|
| AC-01 | identidade sem role no Environment X | tenta ler secret do Environment X | 403 | Security | teste de default deny |
| AC-02 | identidade com role `READ_ONLY` | tenta `POST /secrets` | 403 | Security | teste de escrita negada |
| AC-03 | identidade da Organization A | tenta acessar secret da Organization B por ID | 403/404, nunca o secret | Security | teste de IDOR |
| AC-04 | qualquer decisão de acesso (ALLOW ou DENY) | ocorre | gera evento auditável correspondente | Integration | teste de auditoria de decisão |

## 14. Plano de entrega

- ordem de implementação: Marco M5 em `IMPLEMENTATION_READINESS.md`, junto com o primeiro endpoint de leitura de valor do módulo `05`.
- dados de seed: os 10 roles fixos de §16, seedados na migration inicial deste módulo.
- rollback/roll-forward: reversão padrão; revogar todas as atribuições de um role específico é uma operação administrativa disponível desde o início (mitigação de incidente).

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
