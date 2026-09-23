<p align="center">
  <img src="docs/assets/forgevault-icon.svg" width="96" height="96" alt="ForgeVault">
</p>

<h1 align="center">ForgeVault</h1>

<p align="center">
  Central credentials and secrets vault for the Darckware ecosystem.<br>
  The shared <em>Security Plane</em> consumed by <strong>ForgeHub</strong>, <strong>ForgeRouter</strong>, Hermes, and other agents/services.
</p>

<p align="center">
  <a href="https://github.com/marcelodarckferreira/forgevault/actions/workflows/ci.yml"><img src="https://github.com/marcelodarckferreira/forgevault/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
  <img src="https://img.shields.io/badge/PostgreSQL-17-336791" alt="PostgreSQL 17">
  <img src="https://img.shields.io/badge/status-Wave%201%20(MVP)%20%2B%20M8%2FM9-brightgreen" alt="Status">
  <img src="https://img.shields.io/badge/license-proprietary-lightgrey" alt="License">
</p>

---

## What ForgeVault is

No agent, service, or human should have to hardcode an API key, a database password,
or a third-party token to operate within the Darckware ecosystem. ForgeVault exists to
solve exactly that: a central credentials vault with **envelope encryption**, **hierarchical
RBAC**, **leak-proof auditing**, and native integration — via REST and via
**MCP Server** — so that ForgeHub, ForgeRouter, and autonomous agents can obtain credentials
on demand, without them being scattered across `.env` files, repositories, or configs.

## Key capabilities

| Capability | Description |
|---|---|
| **Envelope encryption** | AES-256-GCM per secret version, DEK protected by a Master Key (KEK) via a pluggable provider (`IKeyManagementProvider`) — switching to a real KMS/HSM is an implementation swap, not a contract change |
| **Immutable versioning** | every write creates a new `SecretVersion`; nothing is overwritten, previous versions remain readable |
| **Hierarchical RBAC** | `Organization → Project → Environment`, with role inheritance flowing down the hierarchy and an explicit role→permission matrix |
| **Leak-proof auditing** | every read/write — including denials — generates an `AuditLog`; actively tested to guarantee that no secret value ever appears in application or audit logs |
| **Dual authentication** | JWT (humans, with optional MFA/TOTP) and Service Account tokens (`fv_sa_...`, for ForgeHub/ForgeRouter/services) — the same `Authorization: Bearer` header resolves both automatically |
| **Native MCP Server** | besides the REST API, an MCP server (`/mcp`) exposes the same operations as *tools* for agents — same authentication, same RBAC, same audit trail, no parallel path |
| **Tested backup/restore** | scripts that run `pg_dump`/`pg_restore` inside the Postgres container, with the Master Key always backed up separately from the database |

## Architecture

Clean Architecture — `Domain` depends on nothing external; each layer above only knows the
one below it:

```text
ForgeVault.Domain            entities and domain rules
        ↑
ForgeVault.Application       use cases, interfaces (IKeyManagementProvider, IPermissionChecker, ...)
        ↑
ForgeVault.Infrastructure    EF Core + Npgsql, cryptography (AES-256-GCM), JWT, RBAC
        ↑
ForgeVault.Api               ASP.NET Core Web API — REST + MCP Server, composition root
ForgeVault.Worker            background jobs (expiration, scheduled rotation)
ForgeVault.Web               React + TypeScript (not yet implemented)
```

**Stack:** ASP.NET Core (.NET 10), Entity Framework Core + Npgsql, PostgreSQL 17 as the
single authoritative datastore, Redis as non-authoritative support, the official
`ModelContextProtocol` SDK for the MCP server.

## Repository structure

```text
forgevault/
├── src/
│   ├── ForgeVault.Domain/          # entities and domain rules
│   ├── ForgeVault.Application/     # use cases and interfaces
│   ├── ForgeVault.Infrastructure/  # EF Core, cryptography, JWT, RBAC
│   ├── ForgeVault.Api/             # Web API — REST + MCP Server
│   ├── ForgeVault.Worker/          # background jobs
│   ├── ForgeVault.Web/             # frontend (React + TypeScript)
│   └── ForgeVault.Cli/             # `fv` CLI — pure client on top of the API (module 08)
├── tests/
│   └── Unit/, Integration/, Security/, E2E/
├── deploy/
│   └── docker/, kubernetes/, scripts/
├── docs/                           # start at docs/README.md
├── docker-compose.yml
└── ForgeVault.slnx
```

## Getting started

### Prerequisites

- .NET SDK 10
- Docker (Postgres 17 + Redis via `docker-compose.yml`)
- `dotnet-ef` (`dotnet tool install --global dotnet-ef`)

### Bringing up the environment

```bash
# Postgres (port 5435 on the host — see docker-compose.yml for why it's non-standard) + Redis
docker compose up -d postgres redis

# apply the migrations
dotnet ef database update --project src/ForgeVault.Infrastructure --startup-project src/ForgeVault.Api

# Master Key — required, never generated silently by the application
deploy/scripts/generate-master-key.sh

# build and tests
dotnet build ForgeVault.slnx
dotnet test ForgeVault.slnx

# run the Api
dotnet run --project src/ForgeVault.Api
# GET /health/live  -> always 200
# GET /health/ready -> 200 if Postgres is reachable, 503 otherwise
```

The Master Key lives at `/root/.forgevault/master.key` with `600` permissions
(`LocalFileKeyProvider`, see `docs/modules/03_SECRETS_AND_ENCRYPTION.md`). Cryptography unit
tests use their own temporary keys and don't depend on this file.

### Dashboard (`ForgeVault.Web`)

```bash
# via Docker (build + serve with nginx, proxy /api -> api:8080 within the compose network)
docker compose up -d --build api web
# http://127.0.0.1:4200  — localhost only, never 0.0.0.0 (see "Security" below)

# or for active development, without Docker
cd src/ForgeVault.Web
npm install
npm run dev
# http://localhost:5173 — the Vite proxy forwards /api to the Api running on :8080
```

React + TypeScript + Vite + TailwindCSS, its own design system (not ForgeHub's shadcn/ui),
themed as a vault — palette derived from `docs/assets/forgevault-icon.svg`,
identifiers and secret values always in monospace, reveal styled as a closed/open
padlock with a countdown. Covers the entire
Organization→Project→Environment→Secret hierarchy, Service Accounts, Access/Roles (M9), and Audit.
Both `api` and `web` in `docker-compose.yml` only publish on `127.0.0.1` — never
`0.0.0.0` — because consumption is always local.

### Importing credentials from an existing `.env`

If you already have passwords/keys scattered across `.env` files, `deploy/scripts/import_env.py`
registers them in ForgeVault in one pass, grouping related variables (e.g.
`DB_HOST`/`DB_PASSWORD`/`DB_URL` become a single secret named `DB_PASSWORD` of type
`DatabaseCredential`, with host/port/link/user stored in the description) and automatically
classifying the service type (Postgres, MySQL, Redis, SSH, S3, etc. — including reading the
scheme from a connection string like `postgres://...` when the variable name doesn't reveal
the engine). A standalone variable (just a `HOST`/`PORT`, with no password/token in the same
group) never becomes a secret on its own — it only enriches the description of the group's
actual credential.

```bash
python3 deploy/scripts/import_env.py \
  --file /path/to/.env \
  --environment-id <environment-guid> \
  --email admin@darckware.local   # password prompted interactively, never via argument
# --dry-run shows what would be imported without registering anything
```

`import_env.py` only solves half the problem — it registers what was already in the `.env`,
but doesn't remove the dependency on the file itself. That's what the `fv` CLI is for
(`src/ForgeVault.Cli`, `docs/modules/08_CLI_SDK.md`): after the import, the consuming system
(ForgeHub, ForgeRouter, Darckware) can delete the `.env` and run via `fv exec` instead, which
fetches the Environment's secrets at runtime and injects them as environment variables only
into the child process — never writing anything to disk.

```bash
dotnet build src/ForgeVault.Cli   # builds bin/Debug/net10.0/fv

fv login --url http://127.0.0.1:8080 --token fv_sa_...   # ServiceAccount — recommended for CI
# or: fv login --url http://127.0.0.1:8080 --email dev@example.com

fv credential list --environment <environment-guid>
fv exec --environment <environment-guid> -- docker compose up
# FORGEVAULT_URL / FORGEVAULT_TOKEN replace the local session — useful in a CI runner that
# never calls `fv login`.

fv export --environment <environment-guid>   # local debug escape hatch — never in production
```

Currently covers `login`/`logout`/`whoami`, `credential list`, `exec`, and `export` — the rest
of the surface from `docs/modules/08_CLI_SDK.md §7` (`credential rotate/revoke`, `access.*`,
`session.*`, `token.*`, `audit search`) has not been implemented yet.

## API

### Authentication and MFA

```text
POST /api/v1/auth/login          JWT (15 min) + rotating refresh token with reuse detection
POST /api/v1/auth/refresh
GET  /api/v1/auth/me
POST /api/v1/auth/mfa/enroll     TOTP (RFC 6238), pure BCL HMACSHA1
POST /api/v1/auth/mfa/verify
```

### Organizations, projects, environments, and secrets

```text
POST/GET/PUT/DELETE /api/v1/organizations[/{id}]
POST/GET            /api/v1/organizations/{organizationId}/projects        RBAC: ProjectWrite
GET/PUT/DELETE      /api/v1/projects/{id}                                  RBAC: ProjectWrite
POST/GET            /api/v1/projects/{projectId}/environments              RBAC: EnvironmentWrite
GET/DELETE          /api/v1/environments/{id}                              RBAC: EnvironmentWrite
POST/GET            /api/v1/secrets                                       RBAC: SecretWrite
GET/PUT             /api/v1/secrets/{id}                                   PUT: RBAC SecretWrite
GET                 /api/v1/secrets/{id}/versions
GET                 /api/v1/secrets/{id}/value?mode=REVEAL                 RBAC: SecretReadValue + MFA
POST                /api/v1/secrets/{id}/rotate                            RBAC: SecretWrite
POST                /api/v1/secrets/{id}/revoke                            RBAC: SecretWrite
```

`mode` already exists in the read contract even though only `REVEAL` is implemented, so that
`BROKER`/`SESSION`/`LEASE`/`INJECT` can be added additively when they arrive.

### Service Accounts, Auditing, and Access Management (M9)

```text
POST /api/v1/service-accounts                              creates the service identity
POST /api/v1/service-accounts/{id}/tokens                  issues an fv_sa_... token (shown once)
POST /api/v1/service-accounts/{id}/tokens/{id}/revoke
GET  /api/v1/service-accounts

GET  /api/v1/audit                                         RBAC: AuditRead (checked at any scope)

POST /api/v1/identities/{identityId}/role-assignments       RBAC: RoleAssignmentWrite at the granted scope
GET  /api/v1/identities/{identityId}/role-assignments        RBAC: RoleAssignmentWrite at any scope
POST /api/v1/role-assignments/{id}/revoke                   RBAC: RoleAssignmentWrite at the assignment's scope
```

Up through M8, granting a `RoleAssignment` (giving someone access) required a direct database
insert — the `POST /api/v1/identities/{id}/role-assignments` endpoint above closes that gap.
Only `Owner`/`Admin` can grant/revoke access (`Permission.RoleAssignmentWrite`); the grant is
idempotent per `(identity, role, scope)`.

### MCP Server

Besides REST, ForgeVault exposes a native MCP server in the same process, with the same
authentication (human JWT or `fv_sa_...` service token):

```text
POST /mcp   (Streamable HTTP, stateless)
```

| Tool | Description |
|---|---|
| `secret.metadata` | a secret's metadata, never the value |
| `credential.request` | retrieves the value under RBAC (currently only `accessMode=REVEAL`); accepts `taskId`/`onBehalfOfAgent`/`runtimeSessionRef` as optional audit metadata — never as authorization input |
| `capability.check` | simulates a permission without executing or revealing anything |
| `admin.secret.create` / `update` / `rotate` / `revoke` | full lifecycle of a secret |
| `admin.audit.search` | searches the audit trail |
| `admin.role.grant` / `revoke` (M9) | grants/revokes a `RoleAssignment` — same RBAC as the equivalent `RoleAssignmentWrite` REST endpoint |
| `admin.agent.register` (M9) | **agent onboarding in a single call**: creates the `ServiceAccount`, issues its `fv_sa_...` token, and grants the role — see "Registering an agent" below |

Every tool reuses exactly the same services as the equivalent REST endpoints — no duplicated
authorization or auditing logic. See `docs/architecture/INTEGRATION_CONTRACT_MVP.md` for a
complete example call.

#### Registering an agent (M9)

Full flow for an agent to register, via MCP, the credentials it already has in hand:

```jsonc
// 1. An Owner/Admin registers the agent (identity + token + access, in one call):
{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{
  "name":"admin.agent.register",
  "arguments":{"name":"agent-athos","scopeType":"Environment","scopeId":"<environment.id>"}
}}
// -> returns {"token":"fv_sa_...", "role":"Agent", ...} — the token appears only here, once.

// 2. The agent uses ITS OWN token to register a credential it already had:
// Authorization: Bearer fv_sa_...
{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{
  "name":"admin.secret.create",
  "arguments":{"environmentId":"<environment.id>","name":"OPENAI_API_KEY","type":"ApiKey","value":"sk-..."}
}}
```

A registered agent never receives `RoleAssignmentWrite` — it cannot grant access to itself
or to anyone else; only someone who is already Owner/Admin at that scope can register new agents.

## Security

- **Cryptography:** AES-256-GCM envelope encryption; the Master Key never lives in the
  database, the repository, or an unprotected environment variable — only in a file with
  restricted permissions.
- **RBAC:** `RoleAssignment(identity_id, role, scope_type, scope_id)` with
  Organization → Project → Environment inheritance; the role→permission matrix lives in
  `ForgeVault.Infrastructure/Authorization/RolePermissions.cs`.
- **Auditing:** `AuditLog` is append-only and never contains a secret value — verified by
  tests that capture all of the application's log output during RBAC/reveal scenarios.
- **MFA:** TOTP is mandatory only for accounts that opted in to enable it, currently enforced
  on secret value reads.

## Tests

```bash
dotnet test ForgeVault.slnx
```

| Suite | Focus |
|---|---|
| `Unit` | cryptography, permissions — isolated, no I/O |
| `Integration` | real round-trip against Postgres |
| `Security` | RBAC, IDOR, privilege escalation, absence of leaks in logs/auditing |
| `E2E` | complete flows via `WebApplicationFactory` — authentication, MFA, Service Accounts, MCP |

CI (`.github/workflows/ci.yml`) builds and runs the full suite against a real Postgres 17
on every push/PR.

## Project status

**Wave 1 (MVP)** complete — scaffold, cryptography, authentication/MFA, Secrets CRUD,
RBAC/auditing, rotation/expiration, Service Accounts, and backup/restore. **Wave 2, M8**
complete — native MCP Server and the ForgeHub/ForgeRouter context contract finalized. **M9**
complete — `RoleAssignment` management via API/MCP and single-call agent onboarding, closing
the access-granting gap that previously required a direct database insert. **Dashboard
(`ForgeVault.Web`)** complete — the project's first UI, see section above. Milestone-by-
milestone detail in `docs/architecture/IMPLEMENTATION_READINESS.md`.

Out of scope for now, by explicit decision (not an oversight) — see
`docs/architecture/IMPLEMENTATION_READINESS.md` §6: dynamic secrets/leases, a real KMS/HSM,
SSO/OIDC/LDAP, break-glass and approval quorum, advanced multi-tenancy, HA/Kubernetes.

## Documentation

| Document | Contents |
|---|---|
| [`docs/README.md`](docs/README.md) | index and documentation authority hierarchy |
| [`docs/specs/PRD.md`](docs/specs/PRD.md) / [`SPEC.md`](docs/specs/SPEC.md) | vision and baseline specification |
| [`docs/architecture/TARGET_ARCHITECTURE.md`](docs/architecture/TARGET_ARCHITECTURE.md) | target architecture |
| [`docs/architecture/IMPLEMENTATION_READINESS.md`](docs/architecture/IMPLEMENTATION_READINESS.md) | implementation order and engineering milestones |
| [`docs/architecture/INTEGRATION_CONTRACT_MVP.md`](docs/architecture/INTEGRATION_CONTRACT_MVP.md) | REST + MCP integration contract for ForgeHub/ForgeRouter |
| [`docs/modules/`](docs/modules/) | spec for each implementable module |

---

<p align="center"><sub>Part of the Darckware ecosystem — sibling of <strong>ForgeHub</strong> and <strong>ForgeRouter</strong>.</sub></p>
