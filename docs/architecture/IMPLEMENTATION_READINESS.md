# ForgeVault — Implementation Readiness

> **Status documental:** canônico para planejamento técnico. Define a ordem de implementação dos módulos em `docs/modules/`, mapeada às fases do roadmap original (`docs/ForgeVault.md` §63), e a fronteira autorizada da primeira onda (MVP). Nenhum módulo além da fronteira atual deve receber código sem uma revisão explícita deste documento.

## 1. Ondas e Fases

| Onda | Fase original (§63) | Módulos | Resultado verificável |
|---|---|---|---|
| 1 (MVP) | Fase 1 | `01`–`06` | Critérios de aceite do MVP (`docs/specs/PRD.md` §8 / `ForgeVault.md` §62) satisfeitos de ponta a ponta |
| 2 | Fase 2 | `07`–`09` | Rotação/revoke completos, CLI/SDK, integração real com ForgeHub/ForgeRouter e MCP nativo |
| 3/4 | Fase 3 e 4 | `10` | Dynamic secrets, HA, KMS/HSM real, SSO/LDAP, multi-tenant avançado |

## 2. Ordem de Dependência dos Módulos

| Ordem | Módulo | Dependências | Resultado verificável | Onda |
|---:|---|---|---|---|
| 1 | `01_FOUNDATION_AND_TENANCY.md` | nenhuma | Organization → Project → Environment funcionando, bootstrap seguro executado uma vez | MVP |
| 2 | `02_IDENTITY_AND_AUTHENTICATION.md` | 1 | login + JWT/refresh funcionando; MFA com seam de enforcement presente (TOTP completo pode fechar no fim da onda) | MVP |
| 3 | `03_SECRETS_AND_ENCRYPTION.md` | 1 | Secret/SecretVersion com envelope encryption (AES-256-GCM); nenhum valor em texto puro no banco | MVP |
| 4 | `04_AUTHORIZATION_AND_POLICY.md` | 2, 3 | RBAC aplicado sobre os endpoints de Secrets; usuário não autorizado recebe 403 | MVP |
| 5 | `05_ACCESS_BROKER_AND_API.md` | 3, 4 | Endpoint de leitura de valor com parâmetro `access_mode` (só `REVEAL` implementado); envelope JSON padronizado | MVP |
| 6 | `06_AUDIT_AND_GOVERNANCE.md` | 2, 3, 4, 5 | Toda leitura/escrita gera `AuditLog`; nenhum log contém valor de secret | MVP |
| 7 | `07_LIFECYCLE_ROTATION_REVOCATION.md` | 3, 4, 6 | rotate/revoke com cascade, dependency/impact analysis, credential health | Fase 2 |
| 8 | `08_CLI_SDK.md` | 5, 6 | CLI `fv` e SDKs consumindo a API estabilizada | Fase 2 |
| 9 | `09_FORGEHUB_FORGEROUTER_MCP_INTEGRATION.md` | 2, 4, 5, 6 | Service Accounts reais; ForgeHub autentica; ForgeRouter recupera credencial; MCP Server nativo | Fase 2 |
| 10 | `10_RESILIENCE_HA_OPERATIONS.md` | 6, 7 | backup/restore testado, observabilidade, HA, maintenance mode | Fase 3/4 |

Uma fase pode criar migrations preparatórias para a seguinte, mas não deve implementar comportamento cuja spec/dependência ainda não esteja aprovada (convenção herdada do `docs/modules/README.md` do ForgeHub).

## 3. Sequência de Marcos de Engenharia da Onda 1 (walking-skeleton-first)

Insumo de um exercício de planejamento de engenharia dedicado (ver histórico de decisão), detalhando como os módulos 01-06 se tornam código de forma incremental e sempre demonstrável, em vez de construir uma camada inteira (todo o Domain, depois toda a Application, ...) antes de qualquer coisa rodar:

| Marco | Entrega | Por que nesta ordem |
|---|---|---|
| M0 | Scaffold da solution (`ForgeVault.{Domain,Application,Infrastructure,Api,Worker,Web}`) + `docker-compose.yml` com Postgres 17 e Redis já presentes + CI mínimo (`dotnet build`/`dotnet test`) | Nada mais pode ser testado ou demonstrado sem um grafo de projetos que compila e um Postgres real; Postgres/Redis entram já no M0 porque toda migration e teste de integração subsequente depende deles |
| M1 | `ForgeVaultDbContext` + primeira migration (`organizations/projects/environments/secrets/secret_versions/audit_logs`) + `GET /health/ready` checando conectividade real com o banco | Prova o pipeline de migration ponta a ponta antes de qualquer feature, e dá a todo marco seguinte um schema para estender em vez de inventar sob pressão de feature |
| M2 | Módulo de criptografia isolado: `IKeyManagementProvider` + `IEnvelopeEncryptionService` (AES-256-GCM), implementação `LocalFileKeyProvider` — testado via unit tests, sem nenhum endpoint HTTP dependendo dele ainda | É o componente de maior risco e mais caro de retrofitar (§143 exige a abstração de KMS "desde o primeiro dia"); isolar do HTTP/auth permite fechar a corretude criptográfica com testes rápidos |
| M3 | Login + JWT + refresh token; MFA como "seam" de enforcement (policy/attribute), TOTP completo ainda não implementado | Desbloqueia tudo que precisa de um chamador autenticado sem forçar TOTP/WebAuthn antes de haver algo relevante para proteger; consistente com §15 (MFA obrigatório só para ações críticas específicas) |
| M4 | Secrets CRUD completo (org→project→env→secret), criptografia ligada ponta a ponta, nova escrita sempre cria nova versão imutável | Primeira fatia vertical totalmente demonstrável — primeira metade do checklist de aceite do MVP |
| M5 | RBAC + `GET /secrets/{id}/value?mode=REVEAL` (parâmetro `mode` já presente, mesmo com só REVEAL implementado) + AuditLog em toda leitura/escrita, incluindo negações | Autorização só é significativa contra um recurso real; testar RBAC direto contra os endpoints de Secrets valida o modelo de permissão imediatamente |
| M6 | TOTP completo, rotação de secret (nova versão, versão anterior permanece legível), expiração (`expires_at`) | Rotação/expiração só fazem sentido depois do modelo de versão (M4) e da trilha de auditoria (M5) existirem para observá-los |
| M7 | Service Accounts (`fv_sa_*`), contrato de integração ForgeHub/ForgeRouter, script de backup/restore com Master Key em local separado do backup do banco | Fecha os itens restantes do checklist de aceite do MVP (§62): ForgeHub autentica, ForgeRouter recupera credencial, backup restaurável |

Checkpoint final da Onda 1: percorrer os 13 itens de `docs/specs/PRD.md` §8 como checklist de aceite manual/E2E.

## 4. Decisões de Design Já Fixadas para a Onda 1

- **Modelo de dados**: `Organization → Project → Environment → Secret → SecretVersion → AuditLog` (§11), não o modelo multi-tenant de §81/§107. Justificativa: o próprio roadmap (§63) coloca "multi-tenant avançado" na Fase 4; o modelo simples satisfaz literalmente todos os critérios de aceite do MVP; migrar para `Tenant/Workspace` depois é aditivo (inserir camada acima), enquanto o caminho inverso seria disruptivo. Nomenclatura de `secret_versions` segue a DDL de §83 (`ciphertext`, `encrypted_dek`, `nonce`, `auth_tag`, `algorithm`), não os nomes mais antigos de §11.
- **Interfaces de criptografia** (`ForgeVault.Application`):
  ```csharp
  public interface IKeyManagementProvider
  {
      Task<byte[]> GetActiveMasterKeyAsync(CancellationToken ct);
      Task<byte[]> WrapDekAsync(byte[] dek, CancellationToken ct);
      Task<byte[]> UnwrapDekAsync(byte[] encryptedDek, CancellationToken ct);
      string ProviderName { get; }
  }

  public interface IEnvelopeEncryptionService
  {
      Task<EncryptedPayload> EncryptAsync(byte[] plaintext, CancellationToken ct);
      Task<byte[]> DecryptAsync(EncryptedPayload payload, CancellationToken ct);
  }

  public sealed record EncryptedPayload(
      byte[] Ciphertext, byte[] EncryptedDek, byte[] Nonce, byte[] AuthTag, string Algorithm);
  ```
  Implementações MVP (`ForgeVault.Infrastructure`): `LocalFileKeyProvider` (lê `/root/.forgevault/master.key`, valida permissão 600 e falha rápido se ausente/permissiva) + `AesGcmEnvelopeEncryptionService`. Trocar de provider de chave (KMS/HSM, Fase 4) é uma troca de registro de DI, não uma mudança de interface.
- **MFA**: seam de enforcement desde M3 (`RequireMfa` como policy/attribute), TOTP real fechado em M6 — válido porque §15 só exige MFA obrigatório para ações críticas específicas, e §62 só exige MFA pronto ao final da Onda 1, não no primeiro marco.
- **Endpoint de leitura de valor**: sempre com parâmetro `mode`/`access_mode`, mesmo quando só `REVEAL` está implementado, para que `BROKER`/`SESSION`/`LEASE`/`INJECT` sejam aditivos e não quebrem contrato quando chegarem (Fase 2/3).

## 5. Testes por Marco (categorias de `ForgeVault.md` §61)

| Marco | Foco | Casos concretos mínimos |
|---|---|---|
| M1 | Integration | round-trip do DbContext contra Postgres real |
| M2 | Unit (prioridade máxima) | encrypt→decrypt determinístico; adulteração de `ciphertext`/`auth_tag` falha (GCM auth); nonce/DEK únicos por chamada; falha limpa se master key ausente/permissão errada; `UnwrapDekAsync` falha se DEK foi selado sob outra master key; nenhum log capturado contém texto plano ou DEK |
| M3 | Integration + Security | login/refresh/logout; JWT expirado/replay rejeitado; refresh token reusado após rotação é rejeitado |
| M4 | Integration | criação da hierarquia completa; ciphertext no banco não é o valor plano; segunda escrita cria versão 2 sem mutar a versão 1 |
| M5 | Security (prioridade máxima) | IDOR entre organizations; escalonamento de privilégio (role `READ_ONLY` chamando endpoint de escrita/reveal); bypass de autorização por claim ausente/malformada; nenhum `AuditLog` contém o valor do secret, inclusive em negações; negação também gera AuditLog |
| M6 | E2E + Unit | enroll TOTP → login com MFA → leitura de secret crítico; expiração nega leitura e audita a negação; rotação mantém versão antiga legível |
| M7 | E2E | checklist completo de `docs/specs/PRD.md` §8 como um único fluxo roteirizado; drill de backup→restore→decrypt de um secret conhecido |

## 6. Explicitamente Fora da Onda 1 (visível e intencional, não esquecido)

| Item | Fase original (§63) | Onde retomar |
|---|---|---|
| MCP Server nativo e tools (`credential.request`, `access.request`, `admin.secret.*`, ...) | Fase 2 (arquiteturalmente sobre a API REST, §90) | `09_FORGEHUB_FORGEROUTER_MCP_INTEGRATION.md` |
| Dynamic secrets, leases/TTL, session brokering real (BROKER/SESSION/LEASE além do stub REVEAL) | Fase 3 | `10_RESILIENCE_HA_OPERATIONS.md` / spec futura de Access Plane avançado |
| KMS/HSM reais (AWS KMS, Azure Key Vault, GCP KMS, Vault Transit, HSM) | Fase 4 (§143) | interface já pronta em `03_SECRETS_AND_ENCRYPTION.md`; troca de implementação apenas |
| SSO/OIDC/OAuth2/LDAP/Active Directory | Fase 4 (§14) | `02_IDENTITY_AND_AUTHENTICATION.md` (revisão futura) |
| Break-glass e quorum/multi-approval | Fase 3 (§136-137) | `07_LIFECYCLE_ROTATION_REVOCATION.md` (revisão futura) |
| Anomaly detection, telemetria avançada, webhooks/notificações | Fase 3 (§121-123) | `06_AUDIT_AND_GOVERNANCE.md` (revisão futura) |
| Multi-tenant (`Tenant`/`Workspace`) e Row-Level Security | Fase 4 (§81, §157) | `01_FOUNDATION_AND_TENANCY.md` (revisão futura) |
| HA, Kubernetes, auditoria particionada, compliance reports | Fase 3/4 (§144-152, §158) | `10_RESILIENCE_HA_OPERATIONS.md` |

## 7. Módulo de UI / Design System

A escolha concreta do design system de frontend (biblioteca de componentes, tokens, paleta — ver decisão registrada em `docs/architecture/TARGET_ARCHITECTURE.md` §10 e `docs/specs/SPEC.md` §2) ainda não tem módulo dedicado nesta versão do planejamento. Deve ser aberta como revisão de `05_ACCESS_BROKER_AND_API.md` (que já cobre a superfície de API que a UI consome) ou como um módulo `11_UI_DESIGN_SYSTEM.md` novo, a critério da próxima revisão — registrado aqui como item em aberto, não decidido por omissão.
