# Module Specification — Resilience, HA and Operations

## 1. Controle

```yaml
spec_id: MOD-10-RESILIENCE-HA-OPS
revision: 1
status: draft
owner: unassigned
approvers: []
target_release: Fase 3/4
architecture_refs:
  - docs/architecture/TARGET_ARCHITECTURE.md
decisions: []
open_blocking_questions: []
```

## 2. Objetivo e limite

- **Problema resolvido:** o ForgeVault se torna infraestrutura crítica (§146) — se cair, ForgeHub/ForgeRouter/Hermes/agentes/sites param. O MVP não cobre HA, backup testado nem maintenance mode.
- **Resultado esperado:** backup/restore testado periodicamente, health endpoints completos, HA em produção madura, maintenance mode para operações administrativas.
- **Atores:** operações/infra.
- **Dentro do escopo:** backup/PITR (§144), restore tests (§145), HA (§147), health endpoints (§148), maintenance mode (§149), soft delete/purge (§150), data retention (§151), compliance reports (§152), auditoria particionada (§158).
- **Fora do escopo:** qualquer lógica de domínio de secrets/identidade — este módulo é puramente operacional.

## 3. Casos de uso e processos

| ID | Ator | Gatilho | Fluxo principal | Resultado |
|---|---|---|---|---|
| UC-01 | Operações | Rotina agendada | backup de banco + Master Key (armazenados separadamente, §56) | backup disponível para restore |
| UC-02 | Operações | Drill periódico | restore em ambiente isolado + integrity check + credential decrypt validation | confiança de que o backup é utilizável |
| UC-03 | Operações | Manutenção planejada | ativa `maintenance mode` (`READ_ONLY`/`NO_ROTATION`/`NO_ADMIN_CHANGES`/`FULL_MAINTENANCE`) | operações sensíveis bloqueadas durante a janela |

## 4. Modelo de domínio e dados

Este módulo é majoritariamente operacional (scripts, configuração de infraestrutura), com pouca modelagem de domínio nova. Onde há dado:

| Entidade | Atributo | Descrição |
|---|---|---|
| MaintenanceWindow | id, mode, started_at, ended_at, reason | registra períodos de modo de manutenção, para auditoria |

### Invariantes

1. Backup do banco e backup da Master Key nunca ficam no mesmo local (§56, regra de ouro).
2. Um restore não é considerado válido sem um teste de decrypt de pelo menos um secret conhecido.

## 5. Máquinas de estados

### Estados (MaintenanceWindow)

| Estado | Significado |
|---|---|
| `active` | modo de manutenção em vigor, restrições aplicadas conforme `mode` |
| `ended` | modo encerrado, operação normal restaurada |

## 6. Policies e permissões

| Ação | Papel/authority | AuditEvent |
|---|---|---|
| Ativar/desativar maintenance mode | `ADMIN`, `SECURITY_ADMIN` | `POLICY_CHANGED` (ou evento dedicado a definir) |

## 7. Contratos de API

```yaml
operation_id: healthLive
method: GET
path: /health/live
authorization: público
response_schema: {status: "ok"}
```

```yaml
operation_id: healthReady
method: GET
path: /health/ready
authorization: público (ou interno, a definir)
response_schema: {status, dependencies: {postgres, redis, kms, event_bus, worker}}
```

Nenhuma resposta de health inclui qualquer secret (§148).

## 8. Eventos e auditoria

| Evento | Producer | Payload | Retention |
|---|---|---|---|
| `BACKUP_COMPLETED` / `BACKUP_FAILED` | worker/script | timestamp, tamanho, resultado — nunca conteúdo | conforme módulo 06 |
| `RESTORE_DRILL_COMPLETED` | script | resultado do integrity/decrypt check | conforme módulo 06 |

## 9. Interface

| Tela/Componente | Rota | Dados |
|---|---|---|
| Status de Saúde | `/admin/health` | resultado agregado de `/health/ready` |
| Maintenance Mode | `/admin/maintenance` | ativar/desativar, histórico de janelas |

## 10. Integrações e falhas

| Integração | Timeout | Retry | Falha visível |
|---|---|---|---|
| PostgreSQL (backup/PITR) | conforme ferramenta de backup | conforme runbook de infra | alerta de `BACKUP_FAILED` |

## 11. Observabilidade

Stack sugerida (§42): Prometheus, Grafana, Loki, OpenTelemetry. Métricas: `vault_requests_total`, `vault_denied_total`, `vault_secret_reads_total`, `vault_rotation_failures_total`, `vault_api_latency`, `vault_active_sessions`.

## 12. Segurança e privacidade

- Master Key isolada do backup do banco (regra de ouro, §56).
- Nenhum endpoint de health/observabilidade expõe secret.

## 13. Critérios de aceite

| ID | Given | When | Then | Nível |
|---|---|---|---|---|
| AC-01 | backup recente existe | restore drill executado | dados restaurados corretamente, um secret conhecido é decifrado com sucesso | Integration/manual |
| AC-02 | `maintenance mode = FULL_MAINTENANCE` ativo | tentativa de operação administrativa | bloqueada com mensagem clara | Integration |

## 14. Plano de entrega

- ordem de implementação: após os módulos `06` e `07` estarem estáveis (ver `IMPLEMENTATION_READINESS.md`); HA/Kubernetes ficam para quando o volume de uso justificar.

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
