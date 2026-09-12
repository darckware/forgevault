# Module Specification — Audit and Governance

## 1. Controle

```yaml
spec_id: MOD-06-AUDIT-GOVERNANCE
revision: 1
status: draft
owner: unassigned
approvers: []
target_release: MVP (Onda 1)
architecture_refs:
  - docs/architecture/TARGET_ARCHITECTURE.md
decisions:
  - "Append-only lógico no MVP (sem update/delete endpoints); hash chaining/WORM ficam para revisão futura (§114, não bloqueante para o MVP)"
open_blocking_questions: []
```

## 2. Objetivo e limite

- **Problema resolvido:** sem este módulo, nenhuma operação sobre identidade, secret ou permissão deixa rastro verificável.
- **Resultado esperado:** toda leitura e escrita relevante dos módulos `01`-`05` gera exatamente um `AuditLog`, nunca contendo o valor de um secret.
- **Atores:** todos os módulos anteriores (producers de evento); `AUDITOR`/`SECURITY_ADMIN` (consumidores/leitores).
- **Responsabilidade deste módulo:** schema de `AuditLog`, endpoint de consulta, regra de "nunca logar o valor" (§21).
- **Responsabilidades de outros módulos:** decidir *quando* emitir um evento (cada módulo emite os seus, listados em suas próprias specs).
- **Dentro do escopo:** tabela `audit_logs`, `GET /audit` (consulta), correlation ID propagado.
- **Fora do escopo:** hash chaining / WORM storage / SIEM externo (§114, revisão futura), compliance reports (§152, Fase 3), particionamento (§158, Fase 3/4), anomaly detection (§121, Fase 3).

## 3. Casos de uso e processos

| ID | Ator | Gatilho | Fluxo principal | Alternativas | Resultado | Scope elements |
|---|---|---|---|---|---|---|
| UC-01 | Qualquer módulo | Uma operação relevante ocorre (login, secret read/write, grant, revoke, falha de acesso) | Módulo produtor grava um `AuditLog` com actor/resource/action/timestamp/result/correlation_id | falha ao gravar auditoria → a operação de negócio não deve ser considerada bem-sucedida silenciosamente (ver invariante 3) | linha em `audit_logs` | Audit |
| UC-02 | AUDITOR/SECURITY_ADMIN | Investigação | `GET /audit?filters` | — | lista paginada de eventos | Consulta |

## 4. Modelo de domínio e dados

### Entidades

| Entidade | Atributo | Tipo | Obrigatório | Default | Constraint | Sensibilidade |
|---|---|---|---|---|---|---|
| AuditLog | id | UUID | sim | gerado | PK | baixa (por design, nunca sensível) |
| | actor_id | UUID/string | sim | — | identifica humano, agente, serviço ou máquina | baixa |
| | actor_type | string | sim | — | enum (human, agent, service, machine, mcp_client) | baixa |
| | action | string | sim | — | enum de §20/§114 (LOGIN, SECRET_READ, SECRET_REVEAL, ACCESS_GRANTED, etc.) | baixa |
| | resource_type / resource_id | string / UUID | sim | — | referência polimórfica (sem FK real, como no ForgeHub — ver `docs/reference/` quando existir) | baixa |
| | source_ip, user_agent | string | não | — | — | baixa |
| | request_id, correlation_id | string | sim | — | propagado por toda a cadeia da chamada (§113) | baixa |
| | timestamp | timestamptz | sim | now() | — | baixa |
| | metadata | JSONB | não | `{}` | **nunca** contém `value`/`ciphertext`/senha/token bruto | baixa (por constraint de aplicação) |

### Relações

`AuditLog` referencia qualquer entidade dos módulos `01`-`05` via `(resource_type, resource_id)` polimórfico — sem FK real, pelo mesmo motivo do ForgeHub: o alvo pode ser qualquer tabela do sistema.

### Invariantes

1. Nenhum campo de `AuditLog` pode conter o valor de um secret, senha ou token bruto — validado por revisão de código e por teste automatizado dedicado (grep sobre payload serializado).
2. `AuditLog` é append-only: a API não expõe `PUT`/`DELETE` sobre esta tabela.
3. Uma operação de negócio cujo `AuditLog` correspondente falhar ao ser persistido deve ser tratada como falha da operação inteira (transação única), não como sucesso silencioso sem rastro — essencial para a garantia "toda leitura gera auditoria" do §62.

### Migração e backfill

- Estado anterior: módulos `01`-`05`.
- Transformação: adiciona `audit_logs` com índice em `(actor_id, timestamp)` e `(resource_type, resource_id)`.
- Backfill: não aplicável.
- Rollback/roll-forward: reversão padrão — mas apagar `audit_logs` via rollback de migration em ambiente com dado real deve ser tratado como incidente, análogo à regra do módulo `03` para `secret_versions`.

## 5. Máquinas de estados

Não há estado — cada linha é imutável desde a criação (write-once).

### Comandos

| Comando | Ator | Precondições | Transição | Side effects | Idempotência | Erros |
|---|---|---|---|---|---|---|
| `RecordAuditEvent` (interno, chamado pelos outros módulos) | sistema | operação de negócio em andamento | N/A (insert) | nenhum | por `request_id` quando aplicável | falha aqui deve reverter a operação de negócio (invariante 3) |
| `QueryAuditLog` | AUDITOR, SECURITY_ADMIN | — | N/A (leitura) | nenhum | idempotente | 403 se sem role |

## 6. Policies e permissões

| Ação | Papel/authority | Policy | Aprovação humana | Separation of duties | AuditEvent |
|---|---|---|---|---|---|
| Consultar audit log | `AUDITOR`, `SECURITY_ADMIN`, `OWNER` | RBAC | não | não | a própria consulta pode opcionalmente gerar um evento de meta-auditoria (não obrigatório no MVP) |

## 7. Contratos de API

```yaml
operation_id: queryAuditLog
method: GET
path: /api/v1/audit
authorization: role in [AUDITOR, SECURITY_ADMIN, OWNER]
idempotency: null (leitura)
request_schema: {actor_id?, resource_type?, resource_id?, action?, from?, to?, page?, page_size?}
response_schema: {items: [AuditLog], page, page_size, total}
errors:
  - {status: 403, code: FORBIDDEN, condition: role sem permissão de auditoria}
events: []
```

## 8. Eventos e auditoria

Este módulo é o **consumidor** dos eventos definidos pelos módulos `01`-`05` (lista consolidada, não exaustiva — cada spec de origem é a fonte primária):

| Evento | Producer | Payload version | Retention |
|---|---|---|---|
| `LOGIN`, `LOGOUT`, `TOKEN_ISSUED`, `TOKEN_REVOKED` | módulo 02 | v1 | política a definir (MVP: sem expurgo automático) |
| `ORGANIZATION_CREATE`, `PROJECT_CREATE`, `ENVIRONMENT_CREATE` | módulo 01 | v1 | idem |
| `SECRET_CREATE`, `SECRET_UPDATE`, `SECRET_READ`, `SECRET_REVEAL` | módulo 03/05 | v1 | idem |
| `ACCESS_GRANTED`, `ACCESS_REVOKED`, `FAILED_ACCESS` | módulo 04 | v1 | idem |

Política de retenção definitiva (partições, expurgo, WORM) é revisão futura — registrada como item aberto arquitetural em `docs/architecture/IMPLEMENTATION_READINESS.md` §6, não bloqueante para o MVP.

## 9. Interface

| Tela/Componente | Rota | Atores | Dados | Ações | Policies |
|---|---|---|---|---|---|
| Trilha de Auditoria | `/audit` | AUDITOR, SECURITY_ADMIN, OWNER | tabela filtrável de eventos | filtrar, exportar (exportação real é Fase 2/3, ver §135) | RBAC |

## 10. Integrações e falhas

| Integração | Ownership | Timeout | Retry | Circuit/reconciliation | Falha visível | Fonte de verdade |
|---|---|---|---|---|---|---|
| Nenhuma externa no MVP — auditoria é síncrona e transacional com a operação de negócio | interno | síncrono | N/A | N/A | falha de auditoria = falha da operação (invariante 3) | PostgreSQL |

## 11. Observabilidade

- logs estruturados: os próprios `AuditLog` já são o log estruturado canônico (JSON, §43).
- métricas: `audit_events_total` por `action`, `audit_write_failures_total` (deve ser sempre zero em operação saudável, dada a invariante 3).

## 12. Segurança e privacidade

- threat model: adulteração retroativa do log (mitigada por design append-only; hash chaining é melhoria futura, não MVP), vazamento de valor de secret via metadata (mitigado por invariante 1 + teste dedicado).
- retenção: sem exclusão automática no MVP.

## 13. Critérios de aceite

| ID | Given | When | Then | Nível do teste | Evidência |
|---|---|---|---|---|---|
| AC-01 | qualquer leitura de secret (autorizada ou não) | ocorre | exatamente um `AuditLog` correspondente existe | Integration | teste ponta a ponta com os módulos 03/04/05 |
| AC-02 | qualquer `AuditLog` gravado neste sistema | inspecionado | nenhum campo contém valor de secret, senha ou token bruto | Security | teste de varredura de payload (grep-based), reforça AC-03 do módulo 03 |
| AC-03 | falha simulada na gravação do audit log durante uma operação de negócio | ocorre | a operação de negócio inteira falha (rollback), não fica em estado "sucesso sem rastro" | Integration | teste de transação combinada |

## 14. Plano de entrega

- ordem de implementação: Marco M5 em `IMPLEMENTATION_READINESS.md` (junto com RBAC e o primeiro endpoint de REVEAL, pois é quando a auditoria passa a ser testável ponta a ponta contra um recurso real).
- dados de seed: nenhum.
- rollback/roll-forward: ver invariante de tratamento como incidente acima.

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
