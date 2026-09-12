# ForgeVault — Software Specification (SPEC)

> **Status documental:** especificação técnica baseline, derivada de `docs/ForgeVault.md`. Para a arquitetura-alvo consolidada, ver `docs/architecture/TARGET_ARCHITECTURE.md`; para a ordem de implementação, ver `docs/architecture/IMPLEMENTATION_READINESS.md`; para o detalhamento executável por fatia, ver `docs/modules/`.

## 1. Scope

Este documento define a especificação funcional e técnica do ForgeVault: cofre central de credenciais/secrets do ecossistema Darckware, atuando como Security Plane consumido por ForgeHub, ForgeRouter, Hermes, agentes, CI/CD e demais consumidores (`ForgeVault.md` §3, §73).

## 2. Canonical Stack Constraints

Backend (§7, §69):
```text
ASP.NET Core
.NET 10+
C#
REST API
OpenAPI
Entity Framework Core
Npgsql
```

Frontend (§7):
```text
React
TypeScript
Vite
TailwindCSS
```

**Decisão de design system (divergência deliberada do ForgeHub):** o ForgeVault reutiliza a base tecnológica de frontend do ForgeHub (React + TypeScript + Vite + TailwindCSS), mas **não** adota o mesmo design system de componentes (shadcn/ui + Radix UI) usado pelo ForgeHub. O ForgeVault terá um design system próprio, visualmente associado ao tema de segurança/cofre e à identidade do ecossistema Darckware (paleta escura, linguagem visual de credencial/chave/cofre). A biblioteca de componentes concreta, os tokens de design e a paleta ficam para um módulo de UI dedicado (ver `docs/architecture/IMPLEMENTATION_READINESS.md`) — esta seção apenas registra a decisão para que nenhuma implementação futura assuma shadcn/ui por padrão.

Banco de dados (§39, §82):
```text
PostgreSQL 17+
UUID primary keys
TIMESTAMPTZ em UTC (todas as colunas de tempo)
JSONB para campos flexíveis
```

Cache/apoio (§40): Redis é opcional, nunca fonte autoritativa de secrets.

## 3. Architecture Overview

Camadas (Clean Architecture, §52 — dependências sempre apontam para dentro):
```text
Domain
   ↑
Application
   ↑
Infrastructure
   ↑
Api
```

Componentes de alto nível (§6): Web UI → API Gateway → { Authentication, Secret Service } → { PostgreSQL, Crypto Layer, Audit Engine } → Master Key/KMS/HSM.

## 4. Domain Model

Modelo adotado na Fase 1 (decisão detalhada em `docs/architecture/IMPLEMENTATION_READINESS.md`, baseada em §11):

### 4.1 Organization
`id, name, slug, status, created_at, updated_at`

### 4.2 Project
`id, organization_id, name, slug, description, status, created_at, updated_at`

### 4.3 Environment
`id, project_id, name, slug, created_at`

### 4.4 Secret
`id, environment_id, name, type, provider, description, owner_id, status, current_version, created_at, updated_at, expires_at, rotation_policy_id`

### 4.5 SecretVersion
Colunas alinhadas à DDL de §83 (preferida sobre a nomenclatura mais antiga de §11):
`id, secret_id, version, ciphertext, encrypted_dek, nonce, auth_tag, algorithm, created_by, created_at`

### 4.6 AuditLog
`id, actor_id, actor_type, action, resource_type, resource_id, source_ip, user_agent, request_id, timestamp, metadata`

Modelo multi-tenant (`Tenant → Workspace → Project → Environment → Resource → Credential`, §81/§107) é Fase 4 — ver justificativa de sequenciamento em `docs/architecture/IMPLEMENTATION_READINESS.md`.

## 5. Functional Requirements

Por módulo (ver `docs/modules/` para o contrato completo de cada um):

- **Fundação e Tenancy** — CRUD de Organization/Project/Environment, bootstrap seguro inicial (§141).
- **Identidade e Autenticação** — login com MFA, JWT curto + refresh token, tipos de identidade (humano/agente/serviço/máquina), token lifecycle (§14-15, §74-77, §115-117).
- **Secrets e Criptografia** — CRUD de Secret/SecretVersion, envelope encryption (AES-256-GCM), abstração de Master Key/KMS, mascaramento por padrão (§10-13, §19, §83, §106, §142-143).
- **Autorização e Política** — RBAC+ABAC, roles, CredentialBinding, AccessGrant, risco L1-L4 (§16-18, §80, §95-99).
- **Broker de Acesso e API** — modos BROKER/SESSION/LEASE/INJECT/REVEAL, envelope JSON padronizado, endpoints REST (§27-30, §84-89).
- **Auditoria e Governança** — audit log append-only, correlation ID, telemetria de uso (§20-21, §112-114).

## 6. Business Rules

Princípios obrigatórios (§66):

1. Zero secrets em texto puro no banco.
2. Zero secrets em logs.
3. Zero secrets no Git.
4. Zero API Keys hardcoded.
5. MFA obrigatório para administradores e ações críticas.
6. Least privilege.
7. Auditoria obrigatória em toda leitura/escrita.
8. Segregação por ambiente — produção nunca compartilhada automaticamente com dev/staging.
9. Rotação periódica.
10. Revogação imediata.
11. Backup criptografado, com a Master Key nunca no mesmo local que o backup do banco (§56).
12. Master Key isolada (fora do banco, do Git, do `.env` e do Dockerfile).

Ver detalhamento em `docs/modules/*` para onde cada regra é efetivamente aplicada (DB constraint vs. camada de aplicação), a ser preenchido conforme cada módulo é implementado.

## 7. Acceptance Criteria

Ver `docs/specs/PRD.md` §8 (Definition of Done do MVP, verbatim de §62) e §159 (critérios adicionais de aceite antes de produção):

- cada agente possui identidade e token próprios;
- cada sistema pode possuir identidade própria;
- credenciais de bancos e serviços externos são recursos independentes;
- tokens são armazenados apenas como hash;
- retorno JSON segue contrato padronizado (§84);
- revoke de acesso não revoga necessariamente a credencial;
- revoke de credencial suporta cascade;
- rotate preserva continuidade operacional;
- nenhum secret aparece em logs;
- nenhum token administrativo universal é compartilhado.

## 8. Directory Layout

Layout de repositório alvo (§51):

```text
forgevault/
├── src/
│   ├── ForgeVault.Api/
│   ├── ForgeVault.Application/
│   ├── ForgeVault.Domain/
│   ├── ForgeVault.Infrastructure/
│   ├── ForgeVault.Worker/
│   └── ForgeVault.Web/
├── tests/
│   ├── Unit/
│   ├── Integration/
│   ├── Security/
│   └── E2E/
├── deploy/
│   ├── docker/
│   ├── kubernetes/
│   └── scripts/
├── docs/
├── docker-compose.yml
├── README.md
└── LICENSE
```

Grafo de referência entre projetos (Clean Architecture — dependências sempre para dentro):
- `Domain` — sem dependências de projeto.
- `Application` — referencia `Domain`.
- `Infrastructure` — referencia `Application` e `Domain`.
- `Api` / `Worker` — referenciam `Application` e `Infrastructure`.
- `Web` — sem project reference; consome a API via HTTP/OpenAPI.

## 9. Delivery Notes

- Implementação incremental e rastreável, seguindo a ordem definida em `docs/architecture/IMPLEMENTATION_READINESS.md`.
- Nenhum módulo é implementado antes de sua spec em `docs/modules/` sair de `status: draft`.
- Nenhum secret ou Master Key é commitado neste repositório.

## 10. Next Spec Artifacts

Após este SPEC, os próximos artefatos canônicos são:

- `docs/architecture/TARGET_ARCHITECTURE.md` — arquitetura-alvo consolidada.
- `docs/architecture/IMPLEMENTATION_READINESS.md` — ordem de implementação e fronteira da primeira onda.
- `docs/modules/01..10_*.md` — specs de módulo executáveis.
- `docs/reference/*` — dicionário de dados e regras de negócio do estado *implementado* (nasce vazio; populado conforme cada módulo ganha código real).
