# Module Specification — Access Broker and API

## 1. Controle

```yaml
spec_id: MOD-05-ACCESS-BROKER-API
revision: 1
status: draft
owner: unassigned
approvers: []
target_release: MVP (Onda 1)
architecture_refs:
  - docs/architecture/TARGET_ARCHITECTURE.md
decisions:
  - "Endpoint de valor sempre aceita/expõe access_mode; apenas REVEAL implementado no MVP; BROKER/SESSION/LEASE/INJECT chegam em fases posteriores sem quebrar o contrato"
open_blocking_questions: []
```

## 2. Objetivo e limite

- **Problema resolvido:** hoje não existe um endpoint para efetivamente obter o valor de um secret, nem um contrato de resposta consistente entre os diferentes modos de acesso previstos na arquitetura-alvo.
- **Resultado esperado:** uma identidade autorizada consegue obter o valor de um secret através de um contrato JSON padronizado, preparado para os modos BROKER/SESSION/LEASE/INJECT mesmo que só REVEAL exista no MVP.
- **Atores:** qualquer identidade autorizada pelo módulo `04`.
- **Responsabilidade deste módulo:** endpoint de leitura de valor, envelope de resposta padronizado, modo de acesso REVEAL.
- **Responsabilidades de outros módulos:** autorização (`04`), armazenamento/criptografia (`03`), auditoria da leitura (`06`).
- **Dentro do escopo:** `GET /secrets/{id}/value?mode=REVEAL`, envelope de resposta de §84.
- **Fora do escopo:** implementação real de BROKER/SESSION/LEASE/INJECT (Fase 2/3), MCP Server (Fase 2, módulo `09`).

## 3. Casos de uso e processos

| ID | Ator | Gatilho | Fluxo principal | Alternativas | Resultado | Scope elements |
|---|---|---|---|---|---|---|
| UC-01 | Identidade autorizada | Precisa do valor real de um secret | `GET /secrets/{id}/value?mode=REVEAL` | não autorizado → 403; secret expirado → 403/410 | valor retornado no envelope padronizado, leitura auditada | REVEAL |
| UC-02 | Identidade não autorizada | Mesma chamada | módulo `04` nega antes de chegar à criptografia | — | 403, sem tentativa de decrypt | Authorization boundary |

## 4. Modelo de domínio e dados

Este módulo não introduz novas tabelas — opera sobre `Secret`/`SecretVersion` (módulo `03`) e `RoleAssignment` (módulo `04`). Único artefato de contrato é o envelope de resposta:

```json
{
  "request_id": "REQ_ID",
  "identity": "IDENTITY_ID",
  "resource": "RESOURCE_ID",
  "access_mode": "REVEAL",
  "expires_at": "ISO_DATE",
  "credentials": {},
  "session": null,
  "broker": null,
  "lease": null,
  "metadata": {}
}
```

(§84) `credentials` só é preenchido quando `access_mode = REVEAL`; nos demais modos futuros, permanece nulo/mascarado.

### Invariantes

1. `access_mode` é sempre um dos valores conhecidos (`BROKER`, `SESSION`, `LEASE`, `INJECT`, `REVEAL`); o MVP só aceita/retorna `REVEAL`, mas o campo já existe no contrato.
2. Um `Secret` com `status ∈ {REVOKED, EXPIRED, SUSPENDED}` nunca retorna valor via este endpoint.
3. Toda chamada a este endpoint, autorizada ou não, gera exatamente um evento auditável (módulo `06`).

## 5. Máquinas de estados

Não introduz estados novos — consome o estado de `Secret` definido no módulo `03`.

### Comandos

| Comando | Ator | Precondições | Transição | Side effects | Idempotência | Erros |
|---|---|---|---|---|---|---|
| `RevealSecretValue` | identidade autorizada | RBAC ALLOW; Secret `ACTIVE`; não expirado | sem transição de estado do Secret | decrypt via módulo `03`; gera `SECRET_REVEAL` auditado | idempotente (leitura) | 403 (não autorizado), 404 (inexistente), 410/403 (expirado) |

## 6. Policies e permissões

| Ação | Papel/authority | Policy | Aprovação humana | Separation of duties | AuditEvent |
|---|---|---|---|---|---|
| `secret.read.value` | conforme RoleAssignment (módulo 04) no Environment do Secret | RBAC; risk_level L3/L4 pode exigir aprovação (dependência futura) | apenas L3/L4 (fora do MVP, ver módulo `07`) | não | `SECRET_REVEAL` |

## 7. Contratos de API

```yaml
operation_id: revealSecretValue
method: GET
path: /api/v1/secrets/{id}/value
authorization: permission "secret.read.value" (módulo 04)
idempotency: null (leitura)
request_schema: {mode: "REVEAL" (default e único valor aceito no MVP)}
response_schema: envelope de §84 acima
errors:
  - {status: 403, code: FORBIDDEN, condition: identidade sem permissão}
  - {status: 404, code: SECRET_NOT_FOUND, condition: secret inexistente}
  - {status: 409, code: SECRET_NOT_ACTIVE, condition: secret expirado/revogado/suspenso}
  - {status: 422, code: UNSUPPORTED_ACCESS_MODE, condition: mode diferente de REVEAL solicitado antes de existir suporte}
events: [SECRET_REVEAL, FAILED_ACCESS]
```

Demais endpoints REST core deste módulo (CRUD já coberto pelos módulos `01`/`03`, listado aqui apenas como referência de superfície completa da Fase 1):

```text
POST   /api/v1/auth/login          (módulo 02)
POST   /api/v1/auth/refresh        (módulo 02)
POST   /api/v1/organizations       (módulo 01)
POST   /api/v1/projects            (módulo 01)
POST   /api/v1/environments        (módulo 01)
GET    /api/v1/secrets             (módulo 03)
POST   /api/v1/secrets             (módulo 03)
GET    /api/v1/secrets/{id}        (módulo 03)
PUT    /api/v1/secrets/{id}        (módulo 03)
GET    /api/v1/secrets/{id}/versions (módulo 03)
GET    /api/v1/secrets/{id}/value  (este módulo)
GET    /health/live, /health/ready (transversal)
```

## 8. Eventos e auditoria

| Evento | Producer | Payload version | Consumer | Idempotency key | Retention |
|---|---|---|---|---|---|
| `SECRET_REVEAL` | Api | v1 | AuditLog (módulo 06) | request_id | conforme módulo 06 |
| `FAILED_ACCESS` | Api | v1 | AuditLog | request_id | conforme módulo 06 |

Nenhum destes eventos contém o valor revelado — apenas `secret_id`, `actor`, `access_mode`, `result` (§21).

## 9. Interface

| Tela/Componente | Rota | Atores | Dados | Ações | Policies |
|---|---|---|---|---|---|
| Detalhe de Secret (extensão da tela do módulo 03) | `/secrets/{id}` | identidade autorizada | valor mascarado por padrão | botão "Revelar" (ação explícita, nunca automática) | RBAC + confirmação explícita (§19) |

## 10. Integrações e falhas

| Integração | Ownership | Timeout | Retry | Circuit/reconciliation | Falha visível | Fonte de verdade |
|---|---|---|---|---|---|---|
| Módulo `03` (crypto) | interno | síncrono | sem retry automático em falha de decrypt (falha é definitiva, não transitória) | N/A | erro 500 genérico ao chamador, detalhe completo apenas em log interno sem payload sensível | PostgreSQL |

## 11. Observabilidade

- logs estruturados: toda chamada a este endpoint (resultado ALLOW/DENY, nunca o valor).
- métricas: `secret_reveal_total`, `secret_reveal_denied_total`.
- rate limiting recomendado (§44): `secret reveal: 20/min` por identidade — implementação real fica a critério do módulo `10` (rate limiting transversal), registrado aqui como requisito não funcional deste endpoint.

## 12. Segurança e privacidade

- threat model: uso indevido de um token comprometido para exfiltrar secrets em massa — mitigado por rate limit por identidade e por auditoria de todo REVEAL.
- secrets: este é o único módulo da Fase 1 que efetivamente retorna um valor em texto puro ao chamador — superfície de maior risco, tratada com o maior rigor de teste de segurança (ver `IMPLEMENTATION_READINESS.md` §5, Marco M5).

## 13. Critérios de aceite

| ID | Given | When | Then | Nível do teste | Evidência |
|---|---|---|---|---|---|
| AC-01 | identidade autorizada, secret ativo | `GET .../value?mode=REVEAL` | 200 com envelope contendo o valor correto | Integration | teste de reveal autorizado |
| AC-02 | identidade não autorizada | mesma chamada | 403, sem tentativa de decrypt (verificável por ausência de chamada ao módulo 03 em teste unitário/mock) | Security | teste de authorization boundary |
| AC-03 | secret expirado | mesma chamada por identidade autorizada | negado (403/409), e a negação é auditada | Integration | teste de expiração |
| AC-04 | qualquer chamada a este endpoint | ocorre | envelope de resposta sempre no formato de §84, mesmo em erro | Integration | teste de contrato de resposta |

## 14. Plano de entrega

- ordem de implementação: Marco M5 em `IMPLEMENTATION_READINESS.md`.
- feature flags: nenhuma — o parâmetro `mode` é parte do contrato desde o início mesmo com um único valor suportado.
- compatibilidade: adicionar `BROKER`/`SESSION`/`LEASE`/`INJECT` no futuro é aditivo (novo valor aceito em `mode`), não uma mudança de contrato existente.

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
