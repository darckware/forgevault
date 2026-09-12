# Module Specification — Secrets and Encryption

## 1. Controle

```yaml
spec_id: MOD-03-SECRETS-ENCRYPTION
revision: 1
status: draft
owner: unassigned
approvers: []
target_release: MVP (Onda 1)
architecture_refs:
  - docs/architecture/TARGET_ARCHITECTURE.md
decisions:
  - "Colunas de secret_versions seguem a DDL de ForgeVault.md §83 (ciphertext/encrypted_dek/nonce/auth_tag/algorithm)"
  - "IKeyManagementProvider/IEnvelopeEncryptionService desenhados desde o início para permitir troca de KMS sem reescrita — ver IMPLEMENTATION_READINESS.md §4"
open_blocking_questions: []
```

## 2. Objetivo e limite

- **Problema resolvido:** não existe hoje nenhuma forma de armazenar um secret de forma que o banco nunca veja o valor em texto puro.
- **Resultado esperado:** todo Secret criado é imediatamente criptografado (envelope encryption AES-256-GCM); toda alteração gera uma nova `SecretVersion` imutável; nenhum valor aparece mascarado incorretamente ou em log.
- **Atores:** desenvolvedor/administrador (cadastro), qualquer identidade autorizada (leitura, via módulo `04`/`05`).
- **Responsabilidade deste módulo:** CRUD de metadados de Secret, criação de SecretVersion, criptografia/decriptação, abstração de Master Key/KMS, mascaramento.
- **Responsabilidades de outros módulos:** quem pode ler o valor (`04`), como o valor é exposto via API (`05`), quem registra a leitura (`06`).
- **Dentro do escopo:** `Secret`, `SecretVersion`, `IKeyManagementProvider`, `IEnvelopeEncryptionService`, `LocalFileKeyProvider`.
- **Fora do escopo:** KMS/HSM reais (Fase 4, §143), rotação automatizada agendada (Fase 2, módulo `07`), templates de credencial por provider (§127, evolução futura).

## 3. Casos de uso e processos

| ID | Ator | Gatilho | Fluxo principal | Alternativas | Resultado | Scope elements |
|---|---|---|---|---|---|---|
| UC-01 | Desenvolvedor | Cadastrar um secret | `POST /secrets` com nome/tipo/provider/valor | nome duplicado no mesmo Environment → 409 | Secret criado com SecretVersion v1 criptografada | Secret |
| UC-02 | Desenvolvedor | Atualizar o valor de um secret | `PUT /secrets/{id}` com novo valor | — | nova SecretVersion (v2) criada; v1 permanece intacta | SecretVersion |
| UC-03 | Qualquer identidade | Consultar metadados | `GET /secrets/{id}` | — | metadados retornados, valor sempre mascarado (§19) | Masking |
| UC-04 | Sistema (bootstrap/deploy) | Master Key ausente | inicialização | Master Key não encontrada → falha rápida de startup, não geração silenciosa | processo não sobe sem chave válida | Key Management |

## 4. Modelo de domínio e dados

### Entidades

| Entidade | Atributo | Tipo | Obrigatório | Default | Constraint | Sensibilidade |
|---|---|---|---|---|---|---|
| Secret | id | UUID | sim | gerado | PK | baixa (metadados) |
| | environment_id | UUID | sim | — | FK→Environment | baixa |
| | name | string | sim | — | unique por environment; convenção `PROVIDER_RESOURCE_PURPOSE` (§64) | baixa |
| | type | string | sim | — | enum (§78: PASSWORD, API_KEY, ACCESS_TOKEN, REFRESH_TOKEN, LLM_TOKEN, SSH_PRIVATE_KEY, SSH_PASSWORD, DATABASE_CREDENTIAL, OAUTH_CLIENT, CERTIFICATE, PRIVATE_KEY, SERVICE_ACCOUNT, WEBHOOK_SECRET, ENV_SECRET, TOTP_SEED, SYSTEM_CREDENTIAL, GENERIC_SECRET) | baixa |
| | provider | string | não | — | — | baixa |
| | owner_id | UUID | sim | — | FK→identidade | baixa |
| | status | string | sim | `active` | enum (ACTIVE, SUSPENDED, REVOKED, EXPIRED, ROTATING, ARCHIVED — §102) | baixa |
| | current_version | int | sim | 1 | — | baixa |
| | expires_at | timestamptz | não | — | — | baixa |
| SecretVersion | id | UUID | sim | gerado | PK | alta (ciphertext) |
| | secret_id | UUID | sim | — | FK→Secret | — |
| | version | int | sim | — | unique por (secret_id, version) | baixa |
| | ciphertext | bytes | sim | — | nunca decodificado fora do serviço de crypto | alta |
| | encrypted_dek | bytes | sim | — | — | alta |
| | nonce | bytes | sim | — | único por versão | alta |
| | auth_tag | bytes | sim | — | — | alta |
| | algorithm | string | sim | `AES-256-GCM` | — | baixa |
| | created_by, created_at | — | sim | — | — | baixa |

### Relações

| Origem | Relação | Destino | Cardinalidade | On delete | Regra cross-domain |
|---|---|---|---|---|---|
| Environment (módulo 01) | possui | Secret | 1:N | restrict se Secret ativo | validado aqui |
| Secret | possui | SecretVersion | 1:N | nunca deleta versão antiga (soft-delete/archive apenas) | — |

### Invariantes

1. `secret_versions` nunca é atualizado (UPDATE) após criado — apenas INSERT de novas linhas (§22, §106).
2. `ciphertext`/`encrypted_dek`/`nonce`/`auth_tag` nunca trafegam em log, evento ou resposta de erro.
3. Um `Secret` com `status=EXPIRED` não pode ser lido via `mode=REVEAL` (aplicado em conjunto com o módulo `05`).

### Migração e backfill

- Estado anterior: apenas tabelas dos módulos `01`/`02`.
- Transformação: adiciona `secrets`, `secret_versions`.
- Backfill: não aplicável.
- Rollback/roll-forward: reversão padrão; qualquer rollback que remova `secret_versions` deve ser tratado como incidente de perda de dado criptografado, não uma operação trivial — exigir confirmação explícita fora do fluxo normal de migration.

## 5. Máquinas de estados

### Estados (Secret)

| Estado | Significado | Entradas permitidas | Saídas permitidas | Terminal? |
|---|---|---|---|---|
| `ACTIVE` | disponível para uso | criação, reativação | `SUSPENDED`, `REVOKED`, `EXPIRED`, `ROTATING` | não |
| `SUSPENDED` | temporariamente bloqueado | `ACTIVE` | `ACTIVE`, `REVOKED` | não |
| `ROTATING` | nova versão sendo emitida (módulo 07) | `ACTIVE` | `ACTIVE` | não |
| `EXPIRED` | passou de `expires_at` | `ACTIVE` | — | não (pode ser reativado por rotação) |
| `REVOKED` | revogado (módulo 07) | — | — | sim |
| `ARCHIVED` | fora de uso, mantido para auditoria | — | — | sim |

### Comandos

| Comando | Ator | Precondições | Transição | Side effects | Idempotência | Erros |
|---|---|---|---|---|---|---|
| `CreateSecret` | identidade autorizada (módulo 04) | Environment existe e ativo; nome único no Environment | — → `ACTIVE` | cria SecretVersion v1 criptografada | por `(environment_id, name)` | 404 Environment, 409 nome duplicado |
| `UpdateSecretValue` | identidade autorizada | Secret existe e não `REVOKED`/`ARCHIVED` | mantém estado, incrementa versão | cria nova SecretVersion, `current_version++` | não idempotente (cada chamada cria versão) | 404, 409 (estado inválido) |
| `ReadSecretMetadata` | identidade autorizada | Secret existe | sem transição | nenhum (valor sempre mascarado) | idempotente | 404 |

## 6. Policies e permissões

| Ação | Papel/authority | Policy | Aprovação humana | Separation of duties | AuditEvent |
|---|---|---|---|---|---|
| Criar/atualizar Secret | `DEVELOPER`, `OPERATOR`, `ADMIN` | RBAC (módulo 04) | não para L1/L2; sim para L3/L4 (§96-97) | não | `SECRET_CREATE` / `SECRET_UPDATE` |
| Ler metadados | qualquer role com acesso ao Environment | RBAC | não | não | `SECRET_READ` |

## 7. Contratos de API

```yaml
operation_id: createSecret
method: POST
path: /api/v1/secrets
authorization: RBAC (módulo 04), escopo = environment
idempotency: por (environment_id, name)
request_schema: {name, type, provider, environment_id, value}
response_schema: {id, name, type, provider, status, current_version, created_at}
errors:
  - {status: 404, code: ENVIRONMENT_NOT_FOUND, condition: ambiente inexistente}
  - {status: 409, code: SECRET_NAME_TAKEN, condition: nome duplicado no ambiente}
events: [SECRET_CREATE]
```

```yaml
operation_id: getSecretMetadata
method: GET
path: /api/v1/secrets/{id}
authorization: RBAC (módulo 04)
idempotency: null (leitura)
request_schema: {}
response_schema: {id, name, type, provider, status, current_version, expires_at, masked_preview}
errors:
  - {status: 404, code: SECRET_NOT_FOUND, condition: secret inexistente}
events: [SECRET_READ]
```

Nunca retorna o valor real — leitura de valor pertence ao módulo `05` (`/secrets/{id}/value?mode=...`).

## 8. Eventos e auditoria

| Evento | Producer | Payload version | Consumer | Idempotency key | Retention |
|---|---|---|---|---|---|
| `SECRET_CREATE` / `SECRET_UPDATE` | Api | v1 | AuditLog (módulo 06) | secret_id + version | conforme módulo 06 |
| `SECRET_READ` (metadados) | Api | v1 | AuditLog | request_id | conforme módulo 06 |

Payload contém apenas `secret_id`, `name`, `type`, `actor_id`, `version` — nunca `ciphertext` nem valor decifrado (§21).

## 9. Interface

| Tela/Componente | Rota | Atores | Dados | Ações | Policies |
|---|---|---|---|---|---|
| Lista de Secrets | `/environments/{id}/secrets` | conforme RBAC | nome, tipo, status, versão atual, valor mascarado (`sk-proj-****`) | criar, abrir detalhe | RBAC |
| Detalhe de Secret | `/secrets/{id}` | conforme RBAC | metadados + histórico de versões | atualizar valor (nova versão) | RBAC |

Revelar o valor real exige ação explícita e é tratado inteiramente pelo módulo `05` (não por este).

## 10. Integrações e falhas

| Integração | Ownership | Timeout | Retry | Circuit/reconciliation | Falha visível | Fonte de verdade |
|---|---|---|---|---|---|---|
| Master Key (arquivo local no MVP) | Infra/Ops | leitura síncrona no startup | falha rápida, sem retry silencioso | processo não sobe sem chave válida (§141) | erro de startup explícito | arquivo `/root/.forgevault/master.key`, permissão 600 |

## 11. Observabilidade

- logs estruturados: criação/atualização de secret (metadados apenas), falha de criptografia/decriptação (sem payload sensível).
- métricas: `secrets_total`, `secret_versions_total`, `encryption_failures_total`.
- health/readiness: verificação de que o `IKeyManagementProvider` consegue acessar a chave ativa (sem expor a chave em texto).

## 12. Segurança e privacidade

- threat model: exfiltração do banco (mitigada por envelope encryption — ciphertext sozinho é inútil sem a Master Key), adulteração de ciphertext (detectada pelo `auth_tag` do AES-GCM).
- secrets: toda a superfície deste módulo é sobre secrets — regra de ouro §56 (backup do banco e Master Key nunca no mesmo local) é responsabilidade operacional do módulo `10`, mas nasce aqui como invariante de design.
- retenção: SecretVersion nunca é fisicamente apagada por operação normal — apenas arquivada (ligação com módulo `07`).

## 13. Critérios de aceite

| ID | Given | When | Then | Nível do teste | Evidência |
|---|---|---|---|---|---|
| AC-01 | Environment existe | criar Secret com valor X | linha em `secret_versions.ciphertext` é diferente de X e não reversível sem a Master Key | Integration | teste de criação + inspeção do banco |
| AC-02 | Secret com v1 existe | atualizar valor | v2 criada, v1 permanece inalterada e recuperável | Integration | teste de versionamento |
| AC-03 | qualquer payload de encrypt/decrypt | operação bem-sucedida ou falha | nenhum log contém texto plano ou DEK | Unit/Security | teste de captura de log (prioritário, ver `IMPLEMENTATION_READINESS.md` §5, Marco M2) |
| AC-04 | `ciphertext` ou `auth_tag` adulterado | tentativa de decrypt | falha limpa (exceção de autenticação GCM), nunca retorna dado corrompido | Unit | teste de tamper detection |

## 14. Plano de entrega

- ordem de implementação: Marco M2 (crypto isolado) e M4 (Secrets CRUD ponta a ponta) em `IMPLEMENTATION_READINESS.md`.
- feature flags: `KeyProvider` selecionável via config (`local-file` no MVP), preparando troca futura de implementação sem mudança de interface.
- dados de seed: nenhum secret real; ambiente de dev pode usar valores de teste explicitamente marcados como não sensíveis.
- rollback/roll-forward: qualquer rollback de migration que apague `secret_versions` deve ser tratado como incidente, não operação de rotina.

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
