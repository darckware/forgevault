# ForgeVault — Especificações dos Módulos-Alvo

## Regra de uso

Estes documentos transformam a arquitetura conceitual (`docs/architecture/TARGET_ARCHITECTURE.md`) em contratos implementáveis. A arquitetura define o porquê e as relações globais; cada spec define ownership, dados, estados, comandos, APIs, telas, eventos e aceite de uma fatia. Todos seguem `docs/templates/MODULE_SPEC_TEMPLATE.md`.

Ordem de leitura e implementação (detalhamento de dependências e marcos em `docs/architecture/IMPLEMENTATION_READINESS.md`):

| Ordem | Spec | Dependências | Resultado verificável | Onda |
|---:|---|---|---|---|
| 1 | `01_FOUNDATION_AND_TENANCY.md` | — | Organization/Project/Environment funcionando, bootstrap seguro executado | MVP |
| 2 | `02_IDENTITY_AND_AUTHENTICATION.md` | 1 | login + JWT/refresh funcionando; MFA com seam de enforcement | MVP |
| 3 | `03_SECRETS_AND_ENCRYPTION.md` | 1 | Secret/SecretVersion com envelope encryption; nada em texto puro no banco | MVP |
| 4 | `04_AUTHORIZATION_AND_POLICY.md` | 2, 3 | RBAC aplicado; 403 para não autorizado | MVP |
| 5 | `05_ACCESS_BROKER_AND_API.md` | 3, 4 | endpoint de valor com `access_mode`; envelope JSON padronizado | MVP |
| 6 | `06_AUDIT_AND_GOVERNANCE.md` | 2–5 | toda leitura/escrita audita; nenhum log com valor de secret | MVP |
| 7 | `07_LIFECYCLE_ROTATION_REVOCATION.md` | 3, 4, 6 | rotate/revoke com cascade e impact analysis | Fase 2 |
| 8 | `08_CLI_SDK.md` | 5, 6 | CLI `fv` e SDKs consumindo a API estável | Fase 2 |
| 9 | `09_FORGEHUB_FORGEROUTER_MCP_INTEGRATION.md` | 2, 4, 5, 6 | ForgeHub autentica, ForgeRouter recupera credencial, MCP nativo | Fase 2 |
| 10 | `10_RESILIENCE_HA_OPERATIONS.md` | 6, 7 | backup/restore testado, observabilidade, HA | Fase 3/4 |
| 11 | `11_MCP_REGISTRY.md` | 2, 4, 6, 9 | catálogo de servidores MCP + assignment por identidade + render de config resolvida (documentação retroativa — código já implementado no M10, ver `IMPLEMENTATION_READINESS.md` §3.3) | Fase 2 |

Uma fase pode criar migrations preparatórias para a seguinte, mas não deve implementar comportamento cuja spec/dependência ainda não esteja aprovada.

## Convenções comuns

- UUID como chave primária, `TIMESTAMPTZ` em UTC em todas as tabelas (`ForgeVault.md` §82).
- FKs cross-domain seguem a convenção que for adotada em `01_FOUNDATION_AND_TENANCY.md` (a definir na implementação — este documento não presume uma convenção de ORM ainda não implementada).
- Constraints de coluna/linha simples no banco; validações cross-row (existência, transição de estado) na camada de aplicação — mesma convenção adotada pelo ForgeHub.
- Nenhum secret ou valor sensível trafega em evento, log ou AuditLog — apenas metadados (§21).
- Toda escrita de secret cria uma nova `SecretVersion`; nunca há sobrescrita silenciosa (§22, §106).
- Toda listagem oferece paginação/filtros antes de produção em escala.
- Toda tela implementa loading, empty, error, forbidden, stale e success, e mascara valores sensíveis por padrão (§19).
- O código, quando existir, é autoritativo sobre estes documentos — ver hierarquia em `docs/README.md`.

## Gates transversais

Cada spec só está aprovada quando:

1. migrations e backfill possuem estratégia;
2. comandos e transições inválidas estão testáveis;
3. autorização humana/agente está definida;
4. audit/events estão definidos e não vazam valor de secret;
5. UI não exige UUID digitado;
6. integração tem timeout/retry/reconciliation;
7. rollout e rollback estão definidos;
8. critérios de aceite ponta a ponta possuem evidência.
