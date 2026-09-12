# Module Specification — Identity and Authentication

## 1. Controle

```yaml
spec_id: MOD-02-IDENTITY-AUTH
revision: 1
status: draft
owner: unassigned
approvers: []
target_release: MVP (Onda 1)
architecture_refs:
  - docs/architecture/TARGET_ARCHITECTURE.md
decisions:
  - "MFA (TOTP) completo adiado para o marco M6; seam de enforcement (RequireMfa) presente desde M3 — ver IMPLEMENTATION_READINESS.md §4"
open_blocking_questions: []
```

## 2. Objetivo e limite

- **Problema resolvido:** não existe hoje nenhuma forma de autenticar um ator (humano, agente, serviço, máquina) perante o ForgeVault.
- **Resultado esperado:** um usuário humano autentica com email+senha (+MFA quando exigido) e recebe um JWT curto + refresh token; toda identidade (humana ou não) é distinta e possui token próprio.
- **Atores:** usuário humano, agente de IA, serviço, máquina, MCP client (`ForgeVault.md` §74).
- **Responsabilidade deste módulo:** login, emissão/renovação/revogação de token, MFA, ciclo de vida de tokens de agente/serviço.
- **Responsabilidades de outros módulos:** autorização/RBAC (`04`), auditoria (`06`).
- **Dentro do escopo:** `POST /auth/login`, `/auth/refresh`, `/auth/logout`, `/auth/mfa/*`, tabelas `users`, `agents`/`service_accounts` (mínimo necessário para autenticação), `agent_tokens`/`service_tokens`.
- **Fora do escopo:** OAuth2/OIDC/SSO/LDAP (§14, Fase 4); WebAuthn/Passkeys completos (podem chegar depois de TOTP); MCP client identity completa (Fase 2, módulo `09`).

## 3. Casos de uso e processos

| ID | Ator | Gatilho | Fluxo principal | Alternativas | Resultado | Scope elements |
|---|---|---|---|---|---|---|
| UC-01 | Humano | Login | `POST /auth/login` com email+senha; se MFA exigido, exige segundo fator | senha incorreta → 401; MFA pendente → 401 com desafio | JWT + refresh token emitidos | Auth |
| UC-02 | Humano | Sessão expirando | `POST /auth/refresh` com refresh token válido | refresh token revogado/reusado → 401 e revoga a família de tokens | novo JWT emitido, refresh token rotacionado | Auth |
| UC-03 | Administrador | Onboarding de agente | Cria identidade `agent:<nome>` e emite token `fv_agent_<NOME>_...` (mostrado uma única vez) | — | Agente autenticado nas próximas chamadas via este token | Identity |
| UC-04 | Administrador | Onboarding de serviço | Cria identidade `service:<nome>` e emite token `fv_service_...` | — | Serviço autenticado | Identity |
| UC-05 | Humano | Habilitar MFA | Enrolla TOTP (`POST /auth/mfa/enroll`), confirma com `POST /auth/mfa/verify` | código inválido → 401 | MFA obrigatório passa a ser exigido no próximo login sensível | MFA (M6) |

## 4. Modelo de domínio e dados

### Entidades

| Entidade | Atributo | Tipo | Obrigatório | Default | Constraint | Sensibilidade |
|---|---|---|---|---|---|---|
| User | id | UUID | sim | gerado | PK | média |
| | email | string | sim | — | unique | média |
| | password_hash | string | sim | — | nunca exposto em resposta | alta |
| | mfa_enabled | bool | sim | false | — | baixa |
| | mfa_secret_encrypted | bytes | não | — | criptografado como um secret (reusa módulo 03) | alta |
| AgentIdentity / ServiceIdentity | id | UUID | sim | gerado | PK | média |
| | name | string | sim | — | unique | baixa |
| | type | string | sim | — | enum (`agent`,`service`,`machine`,`mcp_client`) | baixa |
| AuthToken (agente/serviço) | id | UUID | sim | gerado | PK | alta (só hash) |
| | identity_id | UUID | sim | — | FK | — |
| | token_hash | string | sim | — | unique, SHA-256 | alta |
| | token_prefix | string | sim | — | exibição segura (§117) | baixa |
| | issued_at/expires_at/revoked_at/last_used_at | timestamptz | issued_at sim | — | — | baixa |
| RefreshToken | id | UUID | sim | gerado | PK | alta (só hash) |
| | user_id | UUID | sim | — | FK→User | — |
| | token_hash | string | sim | — | unique | alta |
| | family_id | UUID | sim | — | usado para detecção de reuse | — |

### Relações

| Origem | Relação | Destino | Cardinalidade | On delete | Regra cross-domain |
|---|---|---|---|---|---|
| User | possui | RefreshToken | 1:N | cascade | — |
| AgentIdentity/ServiceIdentity | possui | AuthToken | 1:N | cascade | usado por `04` para checar permissões |

### Invariantes

1. O token completo (não o hash) é exibido apenas uma vez, na emissão (§115).
2. Um `AuthToken` revogado nunca volta a `active`.
3. Reuso de um `RefreshToken` já rotacionado revoga toda a família (`family_id`) — mitigação de roubo de token (§53).

### Migração e backfill

- Estado anterior: apenas tabelas do módulo `01`.
- Transformação: adiciona `users`, `agent_identities`/`service_identities` (ou tabela unificada `identities` com `type` — decisão de implementação, não bloqueante), `auth_tokens`, `refresh_tokens`.
- Backfill: não aplicável.
- Rollback/roll-forward: reversão padrão de migration; nenhum dado de produção existe ainda nesta fase.

## 5. Máquinas de estados

### Estados (AuthToken / RefreshToken)

| Estado | Significado | Entradas permitidas | Saídas permitidas | Terminal? |
|---|---|---|---|---|
| `active` | token válido para uso | emissão | `revoked`, `expired` | não |
| `revoked` | revogado manualmente ou por reuse detection | `active` | — | sim |
| `expired` | passou de `expires_at` | `active` | — | sim |

### Comandos

| Comando | Ator | Precondições | Transição | Side effects | Idempotência | Erros |
|---|---|---|---|---|---|---|
| `Login` | User | credenciais válidas; MFA satisfeito se exigido | — → sessão ativa | emite JWT+RefreshToken | não aplicável | 401 credenciais/MFA inválidos |
| `RefreshSession` | User (via refresh token) | refresh token `active` e não reusado | rotaciona RefreshToken | revoga token anterior, emite novo | idempotente por `family_id` na janela de rotação | 401 se reuso detectado (revoga família) |
| `IssueAgentToken` | ADMIN | identidade existe | — → `active` | token bruto retornado uma vez | por identidade+label | 404 identidade |
| `RevokeToken` | ADMIN, próprio dono | token existe | `active` → `revoked` | invalida uso futuro imediatamente | idempotente | 404 token |

## 6. Policies e permissões

| Ação | Papel/authority | Policy | Aprovação humana | Separation of duties | AuditEvent |
|---|---|---|---|---|---|
| Emitir token de agente/serviço | `ADMIN`, `SECURITY_ADMIN` | RBAC | não (MVP) | não | `TOKEN_ISSUED` |
| Revogar token | `ADMIN`, `SECURITY_ADMIN`, próprio dono (self-revoke) | RBAC | não | não | `TOKEN_REVOKED` |
| Habilitar MFA obrigatório para um papel | `SECURITY_ADMIN` | RBAC | não | não | `POLICY_CHANGED` |

## 7. Contratos de API

```yaml
operation_id: login
method: POST
path: /api/v1/auth/login
authorization: público (rate-limited)
idempotency: null
request_schema: {email: string, password: string, mfa_code: string?}
response_schema: {access_token, refresh_token, expires_in}
errors:
  - {status: 401, code: INVALID_CREDENTIALS, condition: email/senha incorretos}
  - {status: 401, code: MFA_REQUIRED, condition: MFA habilitado e código ausente/incorreto}
events: [LOGIN]
```

```yaml
operation_id: refresh
method: POST
path: /api/v1/auth/refresh
authorization: refresh token válido
idempotency: por family_id dentro da janela de rotação
request_schema: {refresh_token: string}
response_schema: {access_token, refresh_token, expires_in}
errors:
  - {status: 401, code: REFRESH_TOKEN_REUSED, condition: token já rotacionado é reapresentado — revoga a família inteira}
events: [TOKEN_ISSUED, TOKEN_REVOKED]
```

```yaml
operation_id: issueAgentToken
method: POST
path: /api/v1/identities/{identityId}/tokens
authorization: role in [ADMIN, SECURITY_ADMIN]
idempotency: null (cada chamada emite um novo token)
request_schema: {label: string}
response_schema: {token: string (uma única vez), token_prefix, expires_at}
errors:
  - {status: 404, code: IDENTITY_NOT_FOUND, condition: identidade inexistente}
events: [TOKEN_ISSUED]
```

## 8. Eventos e auditoria

| Evento | Producer | Payload version | Consumer | Idempotency key | Retention |
|---|---|---|---|---|---|
| `LOGIN` / `LOGOUT` | Api | v1 | AuditLog | request_id | conforme módulo 06 |
| `TOKEN_ISSUED` / `TOKEN_REVOKED` | Api | v1 | AuditLog | token_id | conforme módulo 06 |
| `MFA_ENROLLED` / `MFA_VERIFIED_FAILED` | Api | v1 | AuditLog | request_id | conforme módulo 06 |

Nenhum destes eventos contém senha, segredo de TOTP ou valor de token — apenas `token_prefix`/`identity_id` (§21, §117).

## 9. Interface

| Tela/Componente | Rota | Atores | Dados | Ações | Policies |
|---|---|---|---|---|---|
| Login | `/login` | todos | email, senha, campo MFA condicional | autenticar | pública |
| Gestão de Tokens | `/identities/{id}/tokens` | ADMIN, SECURITY_ADMIN | lista de tokens (prefixo, criado, último uso, status) | emitir, revogar | RBAC |
| Enroll MFA | `/settings/mfa` | usuário autenticado | QR code de enrollment | confirmar código | próprio usuário |

O token bruto emitido é exibido uma única vez em um modal de "copie agora" — nunca reaparece na lista.

## 10. Integrações e falhas

| Integração | Ownership | Timeout | Retry | Circuit/reconciliation | Falha visível | Fonte de verdade |
|---|---|---|---|---|---|---|
| Nenhuma externa no MVP (TOTP é gerado localmente, sem terceiro) | — | — | — | — | — | PostgreSQL |

## 11. Observabilidade

- logs estruturados: tentativas de login (sucesso/falha, sem senha), emissão/revogação de token.
- métricas: `login_attempts_total`, `login_failures_total`, `token_issued_total`, `token_revoked_total`.
- alertas: taxa anômala de falhas de login (base para módulo 06/anomaly detection futuro).

## 12. Segurança e privacidade

- threat model: credential stuffing (mitigado por rate limit + MFA), replay de refresh token (mitigado por rotação+reuse detection), roubo de token de agente (mitigado por hash-only storage + revogação imediata).
- autenticação: JWT curto (5-15 min), refresh token 7-30 dias, armazenado apenas como hash.
- secrets: senha via hash (Argon2/BCrypt), segredo de TOTP armazenado criptografado (reusa módulo 03).
- abuso/rate limit: `login: 5/min` (§44), a refinar com o módulo 06.

## 13. Critérios de aceite

| ID | Given | When | Then | Nível do teste | Evidência |
|---|---|---|---|---|---|
| AC-01 | usuário válido sem MFA | login com credenciais corretas | JWT + refresh emitidos | Integration | teste de login |
| AC-02 | refresh token já rotacionado | reapresentado | 401 e toda a família é revogada | Security | teste de reuse detection |
| AC-03 | token de agente emitido | usado em uma chamada autenticada | chamada é aceita e identifica o agente corretamente | Integration | teste de auth por token de agente |
| AC-04 | qualquer log ou resposta de erro | qualquer falha de auth | nunca contém senha, segredo TOTP ou token bruto | Security | teste de captura de log |

## 14. Plano de entrega

- ordem de implementação: Marco M3 (login/JWT/refresh + seam de MFA) e M6 (TOTP completo) em `IMPLEMENTATION_READINESS.md`.
- feature flags: `Auth:MfaEnforcement` (off até M6 fechar TOTP).
- dados de seed: usuário administrador criado pelo bootstrap do módulo `01`.
- rollback/roll-forward: reversão de migration padrão; revogar todos os refresh tokens é uma operação administrativa disponível desde o início.

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
