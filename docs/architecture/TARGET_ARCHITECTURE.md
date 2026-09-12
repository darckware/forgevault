# ForgeVault — Arquitetura-Alvo (Target Architecture)

> **Status documental:** canônico para direção. Consolida `docs/ForgeVault.md` §71-73, §89, §160-162. Descreve o que o ForgeVault deve ser quando maduro — não confunda com o que já está implementado (nada, na data desta baseline; ver `docs/reference/README.md`).

## 1. Definição Oficial

> ForgeVault é o **Security Plane** da Darckware: uma plataforma centralizada de identidade, secrets, credenciais, políticas, sessões, leases, revogação, rotação, auditoria e intermediação de acesso para humanos, agentes, sistemas e máquinas (§71, §162).

## 2. Domínios Funcionais (quatro planos)

```text
ForgeVault

1. Identity Plane
   ├── HumanIdentity
   ├── AgentIdentity
   ├── ServiceIdentity
   └── MachineIdentity

2. Secret Plane
   ├── Credentials
   ├── Secret Versions
   ├── Encryption
   ├── Rotation
   └── Expiration

3. Access Plane
   ├── Policies
   ├── Grants
   ├── Leases
   ├── Sessions
   ├── Credential Broker
   └── MCP Server

4. Governance Plane
   ├── Audit
   ├── Notifications
   ├── Compliance
   ├── Usage Telemetry
   └── Anomaly Detection
```

(§72) Cada módulo em `docs/modules/` mapeia para um ou mais destes planos — ver `docs/architecture/IMPLEMENTATION_READINESS.md` para o mapeamento explícito.

## 3. Integração com o Ecossistema Darckware

```text
                     DARCKWARE ECOSYSTEM
                            │
            ┌───────────────┴───────────────┐
            │                               │
        ForgeHub                        ForgeVault
   Orchestration Plane               Security Plane
            │                               │
       Tasks / Agents                       │
            │                               │
            └──────── context ─────────────►│
                                            │
                               ┌────────────┼────────────┐
                               │            │            │
                            Policy      Secrets       Audit
                               │            │
                               └─────┬──────┘
                                     │
                     ┌───────────────┼────────────────┐
                     │               │                │
                   Broker          Lease           Session
                     │               │                │
                     ▼               ▼                ▼
                    APIs          CLIs/apps        Websites
```

(§73) Consumidores esperados: ForgeHub, ForgeRouter, Hermes, agentes Darckware, MCPs, n8n, CI/CD, Darckware Site, aplicações internas e de clientes, servidores, bancos de dados, serviços de terceiros.

Princípio de fronteira (§93):
> ForgeHub = contexto operacional. ForgeVault = contexto de segurança.
ForgeHub fornece `task_id`, `project_id`, `assigned_agent`, `task_status`, `required_action`; o ForgeVault avalia autorização com base nesse contexto — nunca o contrário.

## 4. Modos de Acesso (restrição de design de API)

```text
Prioridade recomendada:
BROKER > SESSION > LEASE > INJECT > REVEAL
```

(§89)

- **BROKER** — o ForgeVault utiliza a credencial em nome do consumidor.
- **SESSION** — o ForgeVault cria/intermedeia uma sessão autenticada.
- **LEASE** — credencial/grant temporário com TTL.
- **INJECT** — secret injetado somente no processo autorizado.
- **REVEAL** — valor real retornado; deve ser exceção, não o modo padrão de nenhum endpoint novo.

Implicação de design vinculante para todo endpoint de leitura de valor: mesmo quando só REVEAL está implementado (MVP), o contrato de API deve expor um parâmetro/campo `access_mode` (ver `docs/ForgeVault.md` §84) para que BROKER/SESSION/LEASE/INJECT sejam aditivos depois, nunca uma mudança quebrando compatibilidade.

## 5. Identidades

Toda entidade que executa ações possui identidade própria (§74):

```text
HumanIdentity
AgentIdentity
ServiceIdentity
MachineIdentity
McpClientIdentity
```

Tokens de agentes e de sistemas são conceitos distintos e nunca compartilhados entre si (§75-76). O token de autenticação de uma identidade no ForgeVault é sempre distinto da credencial de destino que ela acessa (§75):

```text
AGENT_TOKEN
    │
    ▼
ForgeVault
    │
    ├── GitHub Token
    ├── OpenAI API Key
    ├── Cloudflare Token
    ├── SSH Credential
    ├── Database Credential
    └── outros secrets
```

## 6. MCP Server Nativo (Fase 2+, fora do MVP)

O ForgeVault deve expor um MCP Server próprio (§90-91), com tools padrão (`capability.list`, `credential.request`, `access.request`, `session.request`, `secret.metadata`) e tools administrativas (`admin.secret.*`, `admin.policy.*`, `admin.audit.search`) — nunca `get_all_secrets`/`dump_vault`/`export_credentials` para agentes comuns. Arquiteturalmente, o MCP Server é uma camada sobre a API REST, não um caminho paralelo de autorização.

## 7. Policy Engine

Modelo: RBAC + ABAC + Contextual Authorization (§95), com contexto incluindo identidade, agente, serviço, projeto, task, ambiente, recurso, ação, nível de risco, tempo, rede, tenant, workspace. Resultados possíveis: `ALLOW`, `APPROVAL_REQUIRED`, `DENY` — DENY explícito sempre prevalece sobre ALLOW.

Níveis de risco (§96):
```text
L1 - Internal
L2 - Sensitive
L3 - Critical
L4 - Restricted
```
Aprovação humana é exceção (§97, §160 princípio 19): L1/L2 automáticos, L3/L4 exigem aprovação.

## 8. Arquitetura Final Consolidada

```text
                         DARCKWARE
                            │
         ┌──────────────────┼──────────────────┐
         │                  │                  │
      Hermes             ForgeHub         ForgeRouter
         │                  │                  │
         ├──── MCP ─────────┤                  │
         │                  │                  │
         └──────────────┬───┴──────────────────┘
                        │
                        ▼
                   ForgeVault
                        │
         ┌──────────────┼──────────────┐
         │              │              │
    Identity Plane  Access Plane   Secret Plane
         │              │              │
         │          Policy/Grant       │
         │          Lease/Session      │
         │              │              │
         └──────────────┼──────────────┘
                        │
                        ▼
                 Governance Plane
                        │
              Audit / Events / Alerts
                        │
         ┌──────────────┼──────────────┐
         ▼              ▼              ▼
      PostgreSQL       Redis        Event Bus
         │
         ▼
   Encrypted Secrets
         │
         ▼
    KMS / Master Key
```

(§161)

## 9. Princípios Arquiteturais Consolidados

(§160, verbatim)

1. Cada agente possui identidade e token próprios.
2. Sistemas autônomos podem possuir identidade e token próprios.
3. Credenciais pertencem aos recursos, não aos agentes.
4. ForgeVault controla o vínculo entre identidade e credencial.
5. Agentes solicitam capacidade/acesso; não secrets indiscriminadamente.
6. REVEAL deve ser exceção.
7. BROKER, SESSION, LEASE e INJECT são preferenciais.
8. ForgeHub fornece contexto operacional.
9. ForgeVault fornece contexto de segurança.
10. Toda operação crítica deve ser auditável.
11. Toda revogação deve ser imediata, idempotente e rastreável.
12. Toda rotação deve considerar dependências e impacto.
13. A Master Key nunca deve residir junto dos dados criptografados.
14. Secrets nunca devem ser registrados em logs.
15. PostgreSQL é o banco autoritativo do ForgeVault.
16. Redis, se utilizado, é apenas apoio e nunca fonte autoritativa de secrets.
17. O MCP é uma interface oficial do ForgeVault.
18. O ForgeVault deve permanecer independente do ForgeHub.
19. Aprovação humana é exceção, não regra.
20. Segurança e disponibilidade devem ser tratadas em conjunto.

## 10. Divergência de Design System (Frontend)

O ForgeVault reaproveita a base tecnológica de frontend do ForgeHub (React + TypeScript + Vite + TailwindCSS), mas adota um **design system de componentes próprio**, distinto do shadcn/ui + Radix UI do ForgeHub, visualmente associado ao tema de segurança/cofre e à identidade Darckware. Esta é uma decisão de produto (não apenas técnica): o ForgeVault deve ser visualmente reconhecível como "o cofre", não como "mais uma tela do ForgeHub". Detalhamento da biblioteca/tokens concretos fica para um módulo de UI dedicado, referenciado em `docs/architecture/IMPLEMENTATION_READINESS.md`.
