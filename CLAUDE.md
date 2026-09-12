# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Current state

This repository currently contains **no source code** — only a specification document at `docs/ForgeVault.md`. There is no build system, package manifest, test suite, or implemented architecture yet. Do not assume any framework, folder layout, or command exists until it is actually created in this repo.

When code is added, update this file with real build/lint/test commands and the actual architecture — do not invent them ahead of time.

## What this project is

ForgeVault is a planned corporate secrets manager (credentials, API keys, tokens, certificates, etc.) for the "Darckware" ecosystem, positioned alongside two sibling systems: **ForgeHub** (agent/task orchestration) and **ForgeRouter** (LLM provider routing). ForgeVault is meant to be their shared "Security Plane" — the place identities (humans, AI agents, services, machines) go to obtain credentials without those credentials being persisted or hardcoded elsewhere.

Documentation now follows the same spec-driven pattern as the sibling `forgehub` repo (`/root/project/forgehub/docs/`): `docs/ForgeVault.md` (162 sections) is the original historical source, and `docs/README.md` is the actual entry point — it defines the authority hierarchy across `docs/specs/` (baseline PRD/SPEC), `docs/architecture/` (target architecture + implementation readiness/milestones), `docs/modules/01`–`10` (per-slice implementable specs, each `status: draft` until approved), and `docs/reference/` (deliberately empty until real code exists — do not write to it preemptively). **Always read `docs/README.md` first**, then the specific module(s) relevant to the task, rather than the raw `ForgeVault.md`. Key decisions already fixed there that should carry into any code:

- **Recommended stack:** ASP.NET Core / .NET (C#) backend with Entity Framework Core + Npgsql, React/TypeScript frontend, PostgreSQL 17+ as the sole authoritative datastore (Redis only as non-authoritative support), Clean Architecture layering (`Domain → Application → Infrastructure → API`, domain has no infra dependencies).
- **Repo layout envisioned by the spec** (§51): `src/ForgeVault.{Api,Application,Domain,Infrastructure,Worker,Web}/`, `tests/{Unit,Integration,Security,E2E}/`, `deploy/{docker,kubernetes,scripts}/`.
- **Data hierarchy:** `Tenant → Workspace → Project → Environment → Resource → Credential`, with every credential/secret write creating a new immutable `SecretVersion` (never overwrite in place).
- **Crypto:** envelope encryption, AES-256-GCM, DEK encrypted by a Master Key that must never live in the database, Git repo, `.env`, or Dockerfile.
- **Identity model:** every actor (human, agent, service, machine, MCP client) has its own identity and its own auth token; an actor's token to ForgeVault is distinct from the target credential it retrieves (e.g. `agent:athos`'s ForgeVault token is separate from the GitHub/OpenAI/DB credential it's granted access to).
- **Access modes**, preferred order: `BROKER > SESSION > LEASE > INJECT > REVEAL` — `REVEAL` (returning the raw secret value) is meant to be the exception, not the default.
- **Audit rule (hard constraint):** secret values must never appear in audit logs, application logs, or health-check responses — only metadata (actor, resource, action, timestamp, result).
- **MCP Server:** ForgeVault is meant to expose a native MCP server (tools like `credential.request`, `access.request`, `session.request`, `admin.secret.*`) for consumption by Hermes/agents, separate from the REST API.
- **Policy model:** RBAC + ABAC with risk levels L1–L4; human approval is meant to be required only for L3/L4 operations, not the default path.

## Working in this repo right now

- Treat `docs/README.md`'s authority hierarchy as binding: code (none yet) > `docs/reference/*` (doesn't exist yet) > `docs/architecture/TARGET_ARCHITECTURE.md` > `docs/architecture/IMPLEMENTATION_READINESS.md` > `docs/modules/*` > `docs/specs/PRD.md`/`SPEC.md`/`ForgeVault.md`.
- Build order for the MVP (Onda 1) is `docs/modules/01` through `06`, in that dependency order — see `docs/architecture/IMPLEMENTATION_READINESS.md` for the full engineering-milestone breakdown (M0–M7) before scaffolding any code.
- The frontend deliberately does **not** reuse ForgeHub's shadcn/ui + Radix design system — ForgeVault gets its own component/design system, thematically tied to a vault/security identity (see `docs/architecture/TARGET_ARCHITECTURE.md` §10). Don't default to shadcn/ui here.
- Never write to `docs/reference/` with invented content — it stays a placeholder until a module actually has code/migrations (see `docs/reference/README.md`).
