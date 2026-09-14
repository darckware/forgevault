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

## 3.4. Marco de Engenharia da Onda 2 — M11 (acesso por credencial específica)

Motivado por uma limitação real de `RoleAssignment` (módulo 04): é sempre por escopo inteiro (Organization/Project/Environment) — não existia forma de dar a um agente acesso a **uma** credencial específica (um login de site, uma credencial de banco, um token de provider) sem também dar acesso a tudo mais naquele Environment.

| Entrega | Detalhe |
|---|---|
| `SecretAccessGrant` (nova entidade) | vínculo direto "esta identidade pode ler este Secret", independente de qualquer `RoleAssignment`. Mesmo princípio que `McpServerAssignment` já usava internamente para self-render (`McpAssignmentRenderer`: "self-render trusts the assignment"), generalizado para funcionar também contra `GET /secrets/{id}/value` e `credential.request` diretamente, não só dentro do render de config MCP |
| `SecretAccessAuthorizer` (`src/ForgeVault.Api/SecretAccessAuthorizer.cs`) | helper único compartilhado pelo endpoint REST de reveal e pela tool `credential.request` — acesso é RBAC de escopo OU grant específico, nunca lógica duplicada entre as duas superfícies (mesmo princípio do módulo 09 §12, reaplicado) |
| `SecretAccessGrantEndpoints.cs` (4 endpoints REST) + 2 tools MCP (`admin.secret.grant_access`, `admin.secret.revoke_access`) | gated por `SecretWrite` no escopo do secret — quem já pode gerenciar o secret decide quem mais pode lê-lo; idempotente por (secret, identity) igual ao padrão de `RoleAssignment`/`McpServerAssignment` |
| UI (`SecretDetailPage`, aba "Access") | listar/conceder/revogar por id colado — mesmo padrão de `AccessRolesPage` (sem endpoint de busca de identidade) |
| Sem nova `Permission` | reaproveita `SecretWrite` (para grant/revoke) e `RoleAssignmentWrite` (para a listagem "o que esta identidade pode acessar", mesma view de governança de `GET /identities/{id}/role-assignments`) — decisão deliberada para não inflar a matriz de permissões por uma única listagem read-only |
| Formulário estruturado por tipo (`src/ForgeVault.Web/src/lib/secretValue.ts`) | `Secret.Type` já classificava `Password`/`DatabaseCredential`/etc. desde o M4, mas o formulário sempre tratou todo tipo como um único campo de texto — agora `Password` (login de site: URL/usuário/senha) e `DatabaseCredential` (host/porta/banco/usuário/senha) têm campos próprios, codificados como JSON dentro do mesmo `Value` de sempre (sem mudança de schema/migration no backend); reveal decodifica de volta para os mesmos campos quando aplicável |

Testes: `tests/Security/ForgeVault.Security.Tests/SecretAccessGrantEndpointTests.cs` (grant sem nenhum `RoleAssignment` permite reveal, RBAC no grant/revoke, idempotência, revoke remove acesso efetivo e audita sem vazar o valor, grant é aditivo — RBAC de escopo continua funcionando sozinho).

## 3.5. Marco de Engenharia da Onda 2 — M12 (CLI `fv`, módulo 08)

Motivado pela outra metade do problema do `.env`: `deploy/scripts/import_env.py` (M10) cadastra credenciais que já estavam num `.env`, mas não tira a dependência do arquivo em si — um sistema consumidor (ForgeHub, ForgeRouter, Darckware) ainda precisava reescrever seu próprio código para chamar a API do ForgeVault, ou continuar lendo de um `.env` estático.

| Entrega | Detalhe |
|---|---|
| `src/ForgeVault.Cli` (novo projeto, `AssemblyName=fv`) | zero `ProjectReference` para Domain/Infrastructure/Application (docs/modules/08_CLI_SDK.md §2: "este módulo é puramente cliente") — só `HttpClient`/`System.Text.Json` da BCL, sem parser de CLI de terceiros, para compilar offline sem superfície de NuGet nova |
| `fv login` (humano via email/senha, ou `--token fv_sa_...` de ServiceAccount) / `fv logout` / `fv whoami` | sessão local em `~/.forgevault/cli-session.json` (chmod 600) — mesma pasta que `LocalFileKeyProvider` já usa para a Master Key no servidor, mesma convenção "arquivo local, sem keychain do SO" registrada como simplificação conhecida (módulo 08 §12 pede keychain; MVP não tem) |
| `fv exec --environment <id> -- <comando>` | busca todos os secrets `Active` do Environment, decripta cada um (audita `SECRET_REVEAL` normalmente — não é bypass) e injeta como variáveis de ambiente **só no processo filho**, nunca em disco — a peça que faltava para um sistema parar de depender de `.env` sem reescrever código algum: só troca `docker compose up` por `fv exec --environment <id> -- docker compose up` |
| `fv export --environment <id>` | escape hatch de debug local (módulo 08 UC-03/invariante 1) — sempre imprime o aviso de que não é o caminho de produção, sem flag para suprimir |
| `fv credential list --environment <id>` | metadados apenas, nunca valor |
| `FORGEVAULT_URL`/`FORGEVAULT_TOKEN` (env vars) | alternativa a `fv login` para um runner de CI — nenhuma sessão em disco é necessária |
| Superfície não implementada (documentado, não escondido) | `credential metadata/rotate/revoke`, `access.*`, `session.*`, `token.*`, `audit search` (docs/modules/08_CLI_SDK.md §7) — a CLI cobre hoje só o caminho que motivou construí-la, não a spec inteira |

Testado manualmente ponta a ponta contra uma API real (Postgres isolado, fora do banco de dev): login humano e via token de ServiceAccount, `whoami`, `credential list`, `exec` provando injeção real (valor aparece só no processo filho, nunca hardcoded), `export` gerando o arquivo com o aviso. Nenhum teste automatizado dedicado ainda (E2E/Integration) — ver seção 6.

## 3.6. Marco de Engenharia da Onda 2 — M13 (administração de Users)

Motivado por um gap presente desde o primeiro commit: não existia nenhuma forma de criar uma conta humana (`User`) pela própria aplicação — todo `CreateAuthenticatedClientAsync` de teste, e o próprio reset do e-mail do administrador nesta revisão, dependeram de um `INSERT` direto no Postgres. Mesmo tipo de lacuna que `RoleAssignmentWrite` (M9) fechou para concessão de papéis.

| Entrega | Detalhe |
|---|---|
| `User.IsActive` (novo campo, default `true`) | mesma convenção de `ServiceAccount.IsActive`; checado em `AuthService.LoginAsync` — login de conta desativada falha com `401 account_disabled`, verificado depois das credenciais (diferente do `invalid_credentials` genérico, que deliberadamente nunca distingue "não existe" de "senha errada") |
| `Permission.UserManage` (nova) | Owner/Admin apenas, mesmo padrão de `RoleAssignmentWrite`/`McpRegistryWrite`; checado só via `HasPermissionAnywhereAsync` porque um `User` não pertence a uma Organization — quem o vincula a um escopo é o `RoleAssignment`, não o `User` em si |
| `UserEndpoints.cs` (`POST`/`GET /api/v1/users`, `POST .../deactivate`, `POST .../reactivate`) | cria com email+senha inicial (compartilhada fora de banda, sem fluxo de "trocar no primeiro login" ainda); `409 cannot_deactivate_self` — mesmo princípio de "ator não pode se auto-estrandar" já usado em `admin.role.grant` |
| UI (`UsersPage`, item "Users" na sidebar) | listar/criar/desativar/reativar |
| Ajuste de UX no `UserMenu` | rótulo de seção "Conta" adicionado para aproximar do agrupamento visual do `UserSettingsMenu` do ForgeHub — trocador de tema/idioma e modal "Sobre" do ForgeHub **não** replicados, por decisão já registrada em `docs/architecture/TARGET_ARCHITECTURE.md` §10 (ForgeVault tem design system próprio, não reusa o do ForgeHub) |

Testes: `tests/Security/ForgeVault.Security.Tests/UserEndpointTests.cs` (RBAC no create/list, e-mail duplicado, senha curta, ciclo completo criar→logar→desativar→login bloqueado→reativar→login funciona de novo, não pode desativar a si mesmo).

## 3.7. Marco de Engenharia da Onda 2 — M14 (módulo 07: revoke em cascata + impact analysis)

`docs/modules/07_LIFECYCLE_ROTATION_REVOCATION.md` tinha uma `open_blocking_question` não resolvida ("modelo exato de CredentialBinding/dependency mapping"). Decisão desta revisão: em vez de uma entidade `CredentialBinding` genérica (§80 do `ForgeVault.md`), os dois vínculos "identidade → este secret específico" que já existem no código (`SecretAccessGrant`, M11; `McpServerAssignment`, M10) são exatamente o dependency mapping que §108/§109 pedem — não havia necessidade de inventar uma terceira entidade para o mesmo conceito. Fecha a questão bloqueante para este escopo específico; RotationPolicy/CredentialHealth (§111, validação de credencial no provider) continuam de fora — dependem de um catálogo de integração por provider que não existe (module 07 §2 já listava isso como fora do escopo).

| Entrega | Detalhe |
|---|---|
| `POST /api/v1/secrets/{id}/revoke` agora com cascade | revoga (RevokedAt) todo `SecretAccessGrant` e todo `McpServerAssignment` que referencia o secret via `{"secretId": ...}`, e devolve as contagens (`RevokedAccessGrants`/`RevokedMcpAssignments`) na resposta — `SecretRevokeResponse` é um superset de campos de `SecretResponse` (aditivo, não quebra nenhum consumidor existente) |
| Fronteira deliberada: `RoleAssignment` nunca é tocado pelo cascade | um papel no escopo do Environment dá acesso a *todos* os secrets ali — revogar um único secret jamais deve tirar silenciosamente o acesso de alguém a todos os outros; só os vínculos "este secret especificamente" (`SecretAccessGrant`/`McpServerAssignment`) cascadeiam |
| `GET /api/v1/secrets/{id}/impact` (novo) + `IPermissionChecker.ListIdentitiesWithPermissionAsync` (novo método) | lista, antes de uma operação destrutiva: identidades com role que concede `SecretReadValue` no escopo do secret, identidades com `SecretAccessGrant` ativo, e ids de `McpServerAssignment` ativos que referenciam o secret |
| `admin.secret.revoke` (MCP) e `admin.secret.impact` (MCP, novo) | mesma lógica compartilhada com REST (`SecretEndpoints.ToRevokeResponse`/`RevokeMcpAssignmentsReferencingSecretAsync`/`FindMcpAssignmentIdsReferencingSecretAsync`, agora `internal` para reuso entre as duas superfícies) |

Testes: `tests/Security/ForgeVault.Security.Tests/SecretRevokeCascadeTests.cs` — cria um `SecretAccessGrant` e um `McpServerAssignment` apontando pro mesmo secret, confirma `impact` os lista antes do revoke, confirma o revoke cascadeia e conta certo, confirma idempotência (segunda chamada conta zero), e confirma explicitamente que o `RoleAssignment` do agente no Environment **sobrevive** ao revoke do secret.

### Métricas do MCP Registry (fecha parcialmente a open_blocking_question do módulo 11)

`mcp_server_renders_total` (`src/ForgeVault.Api/Observability/McpMetrics.cs`), incrementado em cada saída de `McpAssignmentRenderer.RenderAsync` (tag `outcome`: Success/Forbidden/AssignmentRevoked/etc.) — só `System.Diagnostics.Metrics` da BCL, sem pipeline de export (Prometheus/OTLP) — nenhum exportador existe neste projeto ainda, e escolher um é uma decisão de infraestrutura maior do que este gap específico. Observável hoje via `dotnet-counters monitor ForgeVault.Mcp` sem configuração extra; um exportador futuro só precisaria de `AddMeter("ForgeVault.Mcp")`. `mcp_calls_total` (nível de todas as tools MCP, não só registry) continua sem implementar — instrumentar as ~20 tools uma a uma ficou fora do escopo desta revisão, que era especificamente "métricas do MCP Registry".

## 3.8. Marco de Engenharia da Onda 2 — M15 (perfil de usuário: foto, nome, username, badge de admin)

Pedido explícito do usuário: foto de perfil, nome/sobrenome, mais campos no cadastro de `User`, e um "perfil de admin" — igual ao ForgeHub. Antes de implementar, `IsAdmin` foi explicitamente decidido como **cosmético** (pergunta feita ao usuário, resposta registrada): no ForgeHub, `is_admin` é um bypass real de autorização (`if not principal.is_admin: <ignora a checagem>`, `backend/app/api/routes/governed_approval.py` etc.) — introduzir o mesmo aqui contradiria o modelo de RBAC com escopo que é o desenho central de um secrets manager. `IsAdmin` no ForgeVault nunca é lido por `IPermissionChecker` — só pela UI.

| Entrega | Detalhe |
|---|---|
| `User.Username`/`FirstName`/`LastName`/`AvatarDataUrl`/`IsAdmin` (novos campos) | espelha `backend/app/db/models/user.py` do ForgeHub campo a campo, exceto `FirstName`/`LastName` (ForgeHub tem um único `FullName`; split aqui a pedido explícito). `Username` é nullable com índice único **filtrado** (`WHERE username IS NOT NULL`) — não quebra as dezenas de usuários de teste pré-existentes sem username. `AvatarDataUrl` é uma data URI (`data:image/...;base64,...`) guardada direto na linha, mesma escolha do ForgeHub — não existe storage de arquivo para conteúdo de usuário |
| `GET/PUT /api/v1/auth/me` (self-service) | `GET` deixou de ler só claims do JWT e passou a consultar o banco (a foto/nome não cabem/não devem ir num token); `PUT` atualiza FirstName/LastName/AvatarDataUrl do próprio chamador — deliberadamente **não** aceita Username/IsAdmin/Email/senha, mesma fronteira que o `SelfUserUpdate` do ForgeHub traça. Avatar limitado a ~2MB e precisa começar com `data:image/`, senão `400` |
| `PATCH /api/v1/users/{id}` (admin, novo) | Username/FirstName/LastName/IsAdmin, gated por `UserManage` — espelha a rota admin-only `/users/{user_id}` do ForgeHub. Username duplicado → `409 username_taken` |
| UI: avatar com upload (ícone de câmera, `FileReader` → base64, sem redimensionamento), nome/sobrenome no modal de perfil, badge "Admin" (perfil + sidebar), campos extras no formulário "New User", botão "Grant/Remove admin badge" na tabela de Users | — |
| Sidebar agrupada | itens de navegação agrupados em seções com cabeçalho (General/Vault/Identity/MCP/Governance), inspirado em `NAV_SECTIONS` do ForgeHub — sem o collapse/persistência em `localStorage` do original, que não parecia valer a complexidade extra para uma sidebar deste tamanho |
| `marcelodarck@darckware.net` configurado via a própria API nova | `username=marcelodarck`, badge `isAdmin=true` — chamado via `PATCH /api/v1/users/{id}` já em produção, não mais um script direto no banco; o `RoleAssignment` `Owner` original (herdado do reset de `admin@darckware.local`) permanece intacto e é o que de fato concede acesso |

Testes: `tests/Security/ForgeVault.Security.Tests/UserProfileEndpointTests.cs` (self-update aceita nome/avatar válido e rejeita avatar grande demais ou que não é `data:image/`; PATCH admin seta username+badge, rejeita duplicata, exige `UserManage`). `AuthEndpointTests.Me_ReflectsMfaEnabled` foi atualizado — antes documentava que `/me` **não** refletia uma mudança de `MfaEnabled` na linha sem novo login (lido só do claim do JWT); agora que `/me` já faz uma consulta ao banco para os outros campos, `mfaEnabled` passou a vir da mesma linha, então o teste passou a esperar o valor **atualizado**.

## 3.9. Marco de Engenharia da Onda 2 — M16 (paridade adicional com ForgeHub: sidebar, versão do sistema, tema claro/escuro)

Três pedidos pontuais do usuário, cada um mirando um mecanismo específico do ForgeHub:

| Entrega | Detalhe |
|---|---|
| Botão de recolher/expandir a sidebar corrigido | a primeira versão (mesma revisão, M15) colocava um botão dedicado no rodapé; o padrão real do ForgeHub é o próprio logo virar o gatilho de expandir quando recolhido (hover troca o ícone + mostra tooltip flutuante). Refeito em `Sidebar.tsx` para seguir exatamente esse padrão, com o botão de recolher (quando expandido) na mesma linha do logo, não mais separado |
| `GET /api/v1/system/version` (novo, sem autenticação, mesmo padrão de `/health/*`) | espelha `backend/app/api/routes/system_info.py` do ForgeHub: `appVersion`/`gitSha`/`buildDate` vêm de variáveis de ambiente setadas em build-time (`deploy/scripts/build.sh`, novo, espelha `scripts/build.sh` do ForgeHub; `VERSION` na raiz do repo); `postgresVersion` via `SELECT version()` ao vivo; `latestMigrationBundled` via `Database.GetMigrations()`. `Dockerfile.api` ganhou os `ARG`/`ENV` correspondentes; `docker-compose.yml` repassa como `build.args` com fallback `unknown` |
| Modal "Sobre" na UserMenu | espelha o `AboutModal` do `UserSettingsMenu` do ForgeHub campo a campo (versão, commit com link, build, PostgreSQL, migration, link do GitHub) |
| Tema claro/escuro/sistema, de verdade | decisão do usuário foi fazer certo, não um toggle cosmético: todo token `vault-*` e a escala `slate` (100-700) do Tailwind agora resolvem via CSS custom properties (`src/index.css`, `:root` = claro, `.dark` = escuro — os valores escuros são idênticos aos hex fixos que já existiam, então o padrão visual de todo usuário existente não muda), com `tailwind.config.js` usando o padrão `rgb(var(--x) / <alpha-value>)` para manter os modificadores de opacidade (`bg-vault-ink/40` etc.) funcionando. `src/lib/theme.tsx` espelha o `lib/theme.tsx` do ForgeHub (Context + `matchMedia` para "system"), com uma diferença deliberada: default `"dark"`, não `"system"` como o ForgeHub, para não mudar a aparência de ninguém que nunca escolheu um tema. Um punhado de cores fora da família slate/vault usadas cruas como texto (`amber-100/200`, `rose-300`, `sky-300`) ganharam par `dark:` explícito, já que não passam pelo remapeamento da escala slate. `vault-ink` propositalmente mantém o mesmo valor escuro nos dois temas — os painéis de ênfase tipo "porta do cofre" (`RevealSecretButton`, `RenderMcpConfigButton`) devem continuar lendo como um destaque deliberadamente escuro |
| Fora do escopo, decisão explícita | `LoginPage.tsx` mantém sua própria estética fixa e escura (gradientes/glow artesanais específicos da tela de login) — não foi migrada para os tokens de tema; é a única tela que não respeita a preferência de tema ainda, uma tela "hero" separada do app autenticado, e o seletor de tema em si só existe dentro do app autenticado |

Verificação: `dotnet build`/`vite build` limpos, contagem de commits/dark idêntica aos valores originais confirmada lendo o CSS compilado. **Sem verificação visual em navegador real** — não havia ferramenta de browser disponível nesta sessão; o tema claro em particular não foi visto renderizado, só verificado estaticamente (config do Tailwind, variáveis CSS geradas no bundle). Recomendável conferir visualmente antes de considerar definitivo.

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
