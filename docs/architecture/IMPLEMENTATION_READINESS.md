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
| 11 | `11_MCP_REGISTRY.md` | 2, 4, 6, 9 | catálogo de servidores MCP por Organization, assignment por identidade amarrado a RoleAssignment real, render (REST + MCP) resolvendo referências a Secret | Fase 2 |

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

## 3.1. Marco de Engenharia da Onda 2 — M8 (integração real ForgeHub/ForgeRouter + MCP nativo)

Primeiro marco além do MVP, cobrindo a fatia do módulo `09` que já tem lógica de backend suficiente para ser implementável sem inventar entidades (`CredentialRequest`/`AccessGrant`/`Session`/`Lease`/`Approval` continuam Fase 2/3, ver §6 abaixo).

| Entrega | Detalhe |
|---|---|
| SDK oficial de MCP (`ModelContextProtocol` + `ModelContextProtocol.AspNetCore` 2.2.0, GA, `net10.0`) | Registrado no host existente da API (`AddMcpServer().WithHttpTransport(o => o.Stateless = true).WithTools<VaultTools>()`, `app.MapMcp("/mcp").RequireAuthorization()`) — sem processo/host separado; reaproveita o `SmartAuth` do M7 (JWT humano ou token `fv_sa_...` de serviço autenticam igual) |
| Contrato de contexto ForgeHub → ForgeVault (fecha a `open_blocking_question` do módulo `09`) | Revisão do código real do ForgeHub (não da sua arquitetura-alvo) mostrou que `ProjectTask` não expõe `project_id` diretamente (requer join) e que não existe correlation-id propagado hoje. O contexto real e disponível é `task_id` + identidade do agente (`onBehalfOfAgent`) + `TaskExecution.runtime_session_ref`. Esses três campos são aceitos como argumentos **opcionais** em `credential.request`, gravados em `AuditLog.Metadata` **apenas para rastreabilidade** — nunca usados para autorização (ABAC por task/agente permanece Fase 3, como o roadmap já previa) |
| 8 tools MCP implementadas (`src/ForgeVault.Api/Mcp/VaultTools.cs`) | `secret.metadata`, `credential.request` (só modo `REVEAL`), `capability.check`, `admin.secret.create`, `admin.secret.update`, `admin.secret.rotate`, `admin.secret.revoke`, `admin.audit.search` — cada uma reaproveita exatamente os mesmos serviços dos endpoints REST (`IPermissionChecker`, `AuditLogFactory`, `IEnvelopeEncryptionService`) em vez de duplicar a lógica de autorização/auditoria |
| Duas capacidades novas, pequenas, que faltavam desde marcos anteriores | `POST /api/v1/secrets/{id}/revoke` (o valor `Revoked` existe no enum `SecretStatus` desde M3, mas nada nunca o definia) e `GET /api/v1/audit` + `Permission.AuditRead` — corrige uma lacuna real em que o papel `Auditor` não tinha nenhuma permissão desde M5 |
| Descoberta empírica sobre o SDK (documentada para não se repetir) | Um parâmetro de tool `string?` sem valor default (`= null`) é marcado como obrigatório no schema JSON exposto via `tools/list` e o SDK lança `ArgumentException` se o chamador omitir o argumento — não basta o tipo ser anulável em C#, o parâmetro precisa de um valor default explícito para ser tratado como opcional. `McpException` é o mecanismo correto para sinalizar erro ao cliente: sua `Message` é incluída no resultado (`isError: true`) tal como a documentação do SDK promete; qualquer outra exceção vira uma mensagem genérica sem detalhe |

Testes: `tests/E2E/ForgeVault.E2E.Tests/McpToolsTests.cs` (chamadas JSON-RPC cruas sobre `HttpClient`/`WebApplicationFactory`, sem client MCP dedicado) cobrindo os AC-01/02/03 do módulo `09` em formato MCP; `tests/Security/ForgeVault.Security.Tests/AuditAndRevokeEndpointTests.cs` cobrindo os dois endpoints REST novos com o mesmo padrão de asserção "nenhum vazamento" de `RbacRevealAuditTests`.

## 3.2. Marco de Engenharia da Onda 2 — M9 (gestão de RoleAssignment + onboarding de agentes)

Motivado por uma necessidade operacional concreta: até o M8, o único jeito de conceder acesso
a uma nova identidade (humana ou agente) era um `INSERT` direto no banco — um gargalo real
para onboarding de agentes em escala, documentado como limitação desde M5.

| Entrega | Detalhe |
|---|---|
| `assignRole`/`revokeRoleAssignment`/listagem (módulo 04 §7, já especificados desde a revisão 1, nunca implementados) | `POST /api/v1/identities/{identityId}/role-assignments`, `POST /api/v1/role-assignments/{id}/revoke`, `GET /api/v1/identities/{identityId}/role-assignments` — mesma lógica espelhada como tools MCP `admin.role.grant`/`admin.role.revoke` |
| `Permission.RoleAssignmentWrite` | novo na matriz (`RolePermissions.cs`), concedido só a `Owner`/`Admin` — tradução literal do "role in [OWNER, ADMIN]" do módulo 04 §7 para o modelo de permissão fina já usado pelo resto do sistema |
| `admin.agent.register` (só MCP, extensão sobre a spec) | onboarding de agente em uma única chamada: cria a `ServiceAccount`, emite o token `fv_sa_...` (mostrado uma vez) e concede o `RoleAssignment`, tudo atômico na mesma transação. Só quem já tem `RoleAssignmentWrite` no escopo alvo pode chamá-la — um agente nunca se auto-registra |
| Fechamento retroativo de uma `open_blocking_question` do módulo 04 | "policies em tabela vs. atributos fixos por role em código" já estava decidido na prática desde M5 (`RolePermissions.cs` é um `Dictionary` em código), só nunca tinha sido registrado formalmente no módulo — corrigido no M9 |
| Superfície de escalonamento conhecida, documentada e não fechada | `RoleAssignmentWrite` não impõe hierarquia entre roles — um Owner/Admin pode conceder qualquer role, incluindo Owner, no escopo onde tem `RoleAssignmentWrite`. Mesmo "primeiro corte" de toda a matriz de permissões, não uma regressão introduzida pelo M9 |

Testes: `tests/Security/ForgeVault.Security.Tests/RoleAssignmentEndpointTests.cs` (RBAC,
idempotência, efeito real do revoke sobre uma permissão concedida, listagem gated) e os casos
adicionados a `tests/E2E/ForgeVault.E2E.Tests/McpToolsTests.cs`
(`AdminAgentRegister_ThenTheAgentRegistersItsOwnCredential_EndToEnd` prova o fluxo completo:
um Owner registra um agente, o agente usa **seu próprio token recém-emitido** — não o do
Owner — para cadastrar uma credencial que já tinha via `admin.secret.create`, e o agente
tentando se auto-promover a `Owner` via `admin.role.grant` é negado).

## 3.3. Marco de Engenharia da Onda 2 — M10 (catálogo de servidores MCP e assignment por identidade)

Motivado pelo mesmo tipo de gap operacional do M9, mas para configuração de MCP em vez de RBAC: o `mcp_servers:` de cada agente (Hermes ou outro) era editado à mão em `config.yaml`, incluindo tokens em texto puro (ex.: `FORGEHUB_AGENT_TOKEN`).

| Entrega | Detalhe |
|---|---|
| `McpServerDefinition`/`McpServerAssignment` (novas entidades) | catálogo de "quais servidores MCP existem e como conectar" por Organization, mais "qual identidade está autorizada a usar qual servidor, com quais parâmetros" — nunca o valor de um secret, só a referência (`{"secretId": "..."}`) |
| `McpRegistryEndpoints.cs` (7 endpoints REST) + 4 tools MCP (`admin.mcp.register`, `admin.mcp.assign`, `admin.mcp.revoke_assignment`, `mcp.render_config`) | mesmo padrão do M8/M9: a tool MCP e o endpoint REST equivalente compartilham a mesma lógica (`McpAssignmentRenderer` para o render, especificamente) |
| `Permission.McpRegistryWrite` | novo na matriz (`RolePermissions.cs`), concedido só a `Owner`/`Admin` — mesmo padrão de `RoleAssignmentWrite` (M9) |
| Checagem de autorização real antes de conceder acesso a MCP | criar uma `McpServerAssignment` para uma identidade sem nenhum `RoleAssignment` ativo é rejeitado (`409 identity_has_no_role_assignment`) — evita uma assignment "funcional" para uma identidade sem nenhuma autorização RBAC por trás |
| Gap de sequenciamento documental (registrado, não escondido) | este marco foi implementado **antes** de existir `docs/modules/11_MCP_REGISTRY.md` — contrariando a regra de `docs/README.md` de spec aprovada antes do código. A spec foi escrita retroativamente na mesma revisão que corrigiu este texto; ver `docs/modules/11_MCP_REGISTRY.md` §1 (decisões) e §15 (Definition Gate, com itens abertos) |
| Gap de segurança conhecido, não corrigido neste marco | nem `GET /api/v1/mcp-assignments/{id}/render` nem a tool `mcp.render_config` checam `RevokedAt` antes de resolver — uma assignment revogada continua renderizável por quem já tinha o `assignmentId` e ainda satisfaz a checagem de autorização do render. Ver `docs/modules/11_MCP_REGISTRY.md` §12 |
| Cobertura de teste | apenas a listagem das 4 tools novas em `tools/list` (`McpToolsTests.ToolsList_ReturnsAllPlannedTools`, corrigido nesta mesma revisão — a lista esperada não tinha sido atualizada quando as tools foram adicionadas). Nenhum teste dedicado de Security/Integration para os fluxos REST/MCP deste módulo existe ainda — ver `docs/modules/11_MCP_REGISTRY.md` §13 |

UI (`McpServersPage`/`McpServerDetailPage`/`McpAssignmentsPage` em `src/ForgeVault.Web`, catálogo por Organization + gestão de assignment por identidade com render mascarado por padrão) adicionada em revisão subsequente do mesmo marco — ver `docs/modules/11_MCP_REGISTRY.md` rev2 §9.

Este marco **não** inclui: aplicar a config renderizada a um host de agente real (o script `deploy/scripts/sync_mcp_config.py` citado nos comentários de código não existe neste repositório).

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
| MCP Server nativo — onda 1 de 8 tools (`secret.metadata`, `credential.request` modo REVEAL, `capability.check`, `admin.secret.*`, `admin.audit.search`) | Fase 2 | **Implementado no M8** — ver §3.1 acima e `09_FORGEHUB_FORGEROUTER_MCP_INTEGRATION.md` |
| Tools MCP restantes (`credential.status/release`, `access.*`, `session.*`, `approval.status`, `admin.policy.*`) — dependem de `CredentialRequest`/`AccessGrant`/`Session`/`Lease`/`Approval`, que não existem | Fase 2/3 | `09_FORGEHUB_FORGEROUTER_MCP_INTEGRATION.md` (revisão futura) |
| Dynamic secrets, leases/TTL, session brokering real (BROKER/SESSION/LEASE além do stub REVEAL) | Fase 3 | `10_RESILIENCE_HA_OPERATIONS.md` / spec futura de Access Plane avançado |
| KMS/HSM reais (AWS KMS, Azure Key Vault, GCP KMS, Vault Transit, HSM) | Fase 4 (§143) | interface já pronta em `03_SECRETS_AND_ENCRYPTION.md`; troca de implementação apenas |
| SSO/OIDC/OAuth2/LDAP/Active Directory | Fase 4 (§14) | `02_IDENTITY_AND_AUTHENTICATION.md` (revisão futura) |
| Break-glass e quorum/multi-approval | Fase 3 (§136-137) | `07_LIFECYCLE_ROTATION_REVOCATION.md` (revisão futura) |
| Anomaly detection, telemetria avançada, webhooks/notificações | Fase 3 (§121-123) | `06_AUDIT_AND_GOVERNANCE.md` (revisão futura) |
| Multi-tenant (`Tenant`/`Workspace`) e Row-Level Security | Fase 4 (§81, §157) | `01_FOUNDATION_AND_TENANCY.md` (revisão futura) |
| HA, Kubernetes, auditoria particionada, compliance reports | Fase 3/4 (§144-152, §158) | `10_RESILIENCE_HA_OPERATIONS.md` |
| Aplicar a config de MCP renderizada a um host de agente real (`deploy/scripts/sync_mcp_config.py` citado em comentários de código, não implementado) | Fase 2 | `11_MCP_REGISTRY.md` (revisão futura) |

## 7. Módulo de UI / Design System

**Implementado** (`src/ForgeVault.Web/`) — fecha o item que este parágrafo antes registrava como em aberto. Stack: React + TypeScript + Vite + TailwindCSS (reaproveitando a base tecnológica do ForgeHub, conforme `docs/architecture/TARGET_ARCHITECTURE.md` §10), `@tanstack/react-query` (estado de servidor), `zustand` com `persist` (sessão de auth), `react-hook-form` + `zod` (formulários). Design system próprio, não o `components/ui/*` estilo shadcn do ForgeHub: primitivas Tailwind puro em `src/ForgeVault.Web/src/components/ui/`, paleta derivada de `docs/assets/forgevault-icon.svg` (`vault-bg`/`vault-surface`/`vault-accent` no `tailwind.config.js`), convenção de monospace para todo identificador/valor de secret (`MonoId`), e a interação de reveal como "cadeado fechado→aberto com glow e contagem regressiva" em vez de um ícone de olho genérico.

Cobre: login/refresh (fluxo de refresh real testado — token único em voo mesmo sob 401 concorrente, rotação confirmada), hierarquia Organization→Project→Environment→Secret completa (CRUD + reveal + rotate + revoke), Service Accounts (criar + emitir/revogar token), Access/Roles (grant/revoke via M9, desenhado em torno da ausência de endpoint de busca de identidade) e Audit (filtros + paginação). Deploy: `src/ForgeVault.Web/Dockerfile` (multi-stage node→nginx) + serviço `web` no `docker-compose.yml`, nginx fazendo proxy `/api/` para o container `api` (sem CORS, sem hostname público).

**Decisão de segurança que acompanha este marco**: tanto `web` quanto `api` no `docker-compose.yml` passaram a vincular a porta em `127.0.0.1` (não `0.0.0.0`) — "o consumo da api é pelo localhost" foi uma instrução explícita do usuário nesta revisão. A rota pública pré-existente do túnel Cloudflare para a porta da API é um problema separado, de configuração remota (fora do controle deste repositório), que precisa ser corrigido no dashboard da Cloudflare.

Verificação: percorrido manualmente end-to-end contra o stack real via Playwright headless (login → criar Organization/Project/Environment/Secret → revelar valor → versions) e o binding de rede confirmado via `ss -tlnp` (ambas as portas só em `127.0.0.1`).
