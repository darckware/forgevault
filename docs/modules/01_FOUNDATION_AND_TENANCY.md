# Module Specification — Foundation and Tenancy

## 1. Controle

```yaml
spec_id: MOD-01-FOUNDATION-TENANCY
revision: 1
status: draft
owner: unassigned
approvers: []
target_release: MVP (Onda 1)
architecture_refs:
  - docs/architecture/TARGET_ARCHITECTURE.md
  - docs/architecture/IMPLEMENTATION_READINESS.md
decisions:
  - "Modelo Organization→Project→Environment (ForgeVault.md §11), não o multi-tenant Tenant→Workspace de §81/§107 — ver IMPLEMENTATION_READINESS.md §4"
open_blocking_questions: []
```

## 2. Objetivo e limite

- **Problema resolvido:** hoje não existe nenhum lugar para agrupar secrets por organização, projeto e ambiente; sem isso nenhum outro módulo tem um recurso pai válido.
- **Resultado esperado:** um administrador consegue criar Organization → Project → Environment e o sistema tem um estado inicial seguro (bootstrap) sem depender de nenhum secret pré-existente dentro do próprio ForgeVault.
- **Atores:** administrador inicial (bootstrap), `OWNER`/`ADMIN` (uso corrente).
- **Responsabilidade deste módulo:** CRUD de Organization/Project/Environment; fluxo de bootstrap seguro (`ForgeVault.md` §141).
- **Responsabilidades de outros módulos:** identidade/autenticação (`02`), secrets (`03`), autorização (`04`).
- **Dentro do escopo:** Organization, Project, Environment, bootstrap.
- **Fora do escopo:** Tenant/Workspace multi-tenant (§81/§107, Fase 4); Row-Level Security (§157, Fase 4).
- **Classificações/solution types aplicáveis:** fundação de dados, sem lógica de negócio sensível a secrets.

## 3. Casos de uso e processos

| ID | Ator | Gatilho | Fluxo principal | Alternativas | Resultado | Scope elements |
|---|---|---|---|---|---|---|
| UC-01 | Operador de deploy | Primeira subida do sistema | Executa bootstrap: gera Master Key, inicializa banco, cria primeiro Organization, cria primeiro administrador, registra recovery material, habilita MFA, invalida bootstrap token (§141) | Bootstrap já executado → rejeitar nova execução | Sistema pronto para uso com um administrador válido | Bootstrap |
| UC-02 | Administrador | Quer isolar um novo produto | Cria Organization | Nome duplicado → erro de unicidade | Organization criada | Organization |
| UC-03 | Administrador | Quer organizar secrets de um produto | Cria Project dentro de uma Organization | Organization inexistente → 404 | Project criado | Project |
| UC-04 | Administrador | Quer segregar por estágio de deploy | Cria Environment (`development`/`staging`/`production`/`shared`) dentro de um Project | Nome de ambiente fora da lista permitida → erro de validação | Environment criado | Environment |

## 4. Modelo de domínio e dados

### Entidades

| Entidade | Atributo | Tipo | Obrigatório | Default | Constraint | Sensibilidade |
|---|---|---|---|---|---|---|
| Organization | id | UUID | sim | gerado | PK | baixa |
| | name | string | sim | — | unique | baixa |
| | slug | string | sim | — | unique | baixa |
| | status | string | sim | `active` | enum (`active`,`suspended`) | baixa |
| | created_at / updated_at | timestamptz | sim | now() | — | baixa |
| Project | id | UUID | sim | gerado | PK | baixa |
| | organization_id | UUID | sim | — | FK→Organization | baixa |
| | name, slug, description | string | name/slug sim | — | unique por organization | baixa |
| | status | string | sim | `active` | enum | baixa |
| Environment | id | UUID | sim | gerado | PK | baixa |
| | project_id | UUID | sim | — | FK→Project | baixa |
| | name | string | sim | — | enum (`development`,`staging`,`production`,`shared`) | baixa |
| | slug | string | sim | — | unique por project | baixa |

### Relações

| Origem | Relação | Destino | Cardinalidade | On delete | Regra cross-domain |
|---|---|---|---|---|---|
| Organization | possui | Project | 1:N | restrict se houver Project ativo | — |
| Project | possui | Environment | 1:N | restrict se houver Environment ativo | — |
| Environment | possui | Secret (módulo 03) | 1:N | restrict se houver Secret ativo | validado pelo módulo 03 |

### Invariantes

1. Uma Organization não pode ser removida enquanto tiver Project ativo.
2. Um Environment só pode usar um dos quatro nomes padronizados (§9).
3. Nenhuma credencial de produção é copiada automaticamente para outro Environment (§9, §140) — regra de fronteira para o módulo `03`, registrada aqui porque nasce da hierarquia.

### Migração e backfill

- Estado anterior: nenhum (primeira migration do sistema).
- Transformação: criação das tabelas `organizations`, `projects`, `environments`.
- Backfill: não aplicável (banco novo).
- Compatibilidade: N/A.
- Rollback/roll-forward: `dotnet ef database update <migration-anterior>` reverte; nenhuma dessas tabelas armazena dado sensível, então rollback não tem implicação de exposição de secret.

## 5. Máquinas de estados

### Estados

| Estado | Significado | Entradas permitidas | Saídas permitidas | Terminal? |
|---|---|---|---|---|
| `active` | recurso em uso normal | criação | `suspended` | não |
| `suspended` | recurso bloqueado para novas operações | `active` | — | não |

### Comandos

| Comando | Ator | Precondições | Transição | Side effects | Idempotência | Erros |
|---|---|---|---|---|---|---|
| `CreateOrganization` | ADMIN/OWNER | nome único | — → `active` | nenhum | por `name` | 409 se nome duplicado |
| `CreateProject` | ADMIN/OWNER/PROJECT_ADMIN | Organization existe e está `active` | — → `active` | nenhum | por `(organization_id, name)` | 404 Organization, 409 nome duplicado |
| `CreateEnvironment` | ADMIN/OWNER/PROJECT_ADMIN | Project existe e está `active`; nome ∈ lista permitida | — → `active` | nenhum | por `(project_id, name)` | 404 Project, 422 nome inválido, 409 duplicado |

## 6. Policies e permissões

| Ação | Papel/authority | Policy | Aprovação humana | Separation of duties | AuditEvent |
|---|---|---|---|---|---|
| Bootstrap inicial | apenas o processo de instalação, uma única vez | token de bootstrap de uso único | implícita (execução manual) | N/A | `BOOTSTRAP_COMPLETED` |
| Criar Organization | `OWNER`, `ADMIN` | RBAC | não | não | `ORGANIZATION_CREATE` |
| Criar Project/Environment | `OWNER`, `ADMIN`, `PROJECT_ADMIN` | RBAC | não | não | `PROJECT_CREATE` / `ENVIRONMENT_CREATE` |

## 7. Contratos de API

```yaml
operation_id: createOrganization
method: POST
path: /api/v1/organizations
authorization: role in [OWNER, ADMIN]
idempotency: por (name)
request_schema: {name: string, slug: string}
response_schema: {id, name, slug, status, created_at}
errors:
  - {status: 409, code: ORG_NAME_TAKEN, condition: nome já existe}
events: [ORGANIZATION_CREATE]
```

```yaml
operation_id: createProject
method: POST
path: /api/v1/organizations/{organizationId}/projects
authorization: role in [OWNER, ADMIN, PROJECT_ADMIN]
idempotency: por (organization_id, name)
request_schema: {name: string, slug: string, description: string?}
response_schema: {id, organization_id, name, slug, status, created_at}
errors:
  - {status: 404, code: ORG_NOT_FOUND, condition: organização inexistente}
  - {status: 409, code: PROJECT_NAME_TAKEN, condition: nome duplicado na organização}
events: [PROJECT_CREATE]
```

```yaml
operation_id: createEnvironment
method: POST
path: /api/v1/projects/{projectId}/environments
authorization: role in [OWNER, ADMIN, PROJECT_ADMIN]
idempotency: por (project_id, name)
request_schema: {name: "development|staging|production|shared"}
response_schema: {id, project_id, name, slug, created_at}
errors:
  - {status: 404, code: PROJECT_NOT_FOUND, condition: projeto inexistente}
  - {status: 422, code: INVALID_ENVIRONMENT_NAME, condition: nome fora da lista permitida}
events: [ENVIRONMENT_CREATE]
```

## 8. Eventos e auditoria

| Evento | Producer | Payload version | Consumer | Idempotency key | Retention |
|---|---|---|---|---|---|
| `BOOTSTRAP_COMPLETED` | processo de instalação | v1 | AuditLog | run único | permanente |
| `ORGANIZATION_CREATE` | Api | v1 | AuditLog | `organization_id` | conforme política de retenção (módulo 06) |
| `PROJECT_CREATE` | Api | v1 | AuditLog | `project_id` | idem |
| `ENVIRONMENT_CREATE` | Api | v1 | AuditLog | `environment_id` | idem |

## 9. Interface

| Tela/Componente | Rota | Atores | Dados | Ações | Policies |
|---|---|---|---|---|---|
| Lista de Organizations | `/organizations` | OWNER, ADMIN | nome, status, contagem de projetos | criar, suspender | RBAC |
| Detalhe de Project | `/projects/{id}` | ADMIN, PROJECT_ADMIN | environments, contagem de secrets | criar environment | RBAC |

Sem dado sensível nesta camada — nenhuma tela deste módulo exibe valor de secret.

## 10. Integrações e falhas

| Integração | Ownership | Timeout | Retry | Circuit/reconciliation | Falha visível | Fonte de verdade |
|---|---|---|---|---|---|---|
| Nenhuma externa neste módulo | — | — | — | — | — | PostgreSQL |

## 11. Observabilidade

- logs estruturados: criação/suspensão de Organization/Project/Environment (sem dado sensível).
- métricas: `organizations_total`, `projects_total`, `environments_total`.
- health/readiness: `GET /health/ready` valida conectividade com o banco onde estas tabelas residem (M1 de `IMPLEMENTATION_READINESS.md`).

## 12. Segurança e privacidade

- threat model: nenhuma exposição de secret neste módulo; risco principal é criação não autorizada de estrutura (mitigado por RBAC do módulo `04`).
- secrets: nenhum secret é manipulado aqui.
- dados pessoais/sensíveis: nenhum.

## 13. Critérios de aceite

| ID | Given | When | Then | Nível do teste | Evidência |
|---|---|---|---|---|---|
| AC-01 | banco vazio | bootstrap é executado | Organization + admin inicial existem, bootstrap token invalidado | Integration | teste de bootstrap idempotente (segunda execução falha) |
| AC-02 | Organization existe | criar Project com nome novo | Project criado com status `active` | Integration | teste de criação |
| AC-03 | Project existe | criar Environment com nome fora da lista permitida | 422 | Unit/Integration | teste de validação |

## 14. Plano de entrega

- ordem de implementação: base do Marco M1 em `IMPLEMENTATION_READINESS.md`.
- feature flags: nenhuma.
- rollout: primeira migration do banco.
- dados de seed: nenhum além do resultado do bootstrap.
- rollback/roll-forward: reversão de migration padrão do EF Core.

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
- [ ] nenhuma questão bloqueante aberta
