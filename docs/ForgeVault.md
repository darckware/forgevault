# ForgeVault

## 1. Visão Geral

O **ForgeVault** é o cofre central de credenciais e secrets do ecossistema Darckware.

Seu objetivo é centralizar, proteger, controlar e auditar o uso de informações sensíveis utilizadas por aplicações, agentes de IA, serviços, servidores e equipes técnicas.

O ForgeVault deve armazenar e gerenciar, entre outros:

- Senhas
- API Keys
- Access Tokens
- Refresh Tokens
- Tokens de LLMs
- Credenciais SSH
- Credenciais de banco de dados
- Service Accounts
- Certificados
- Chaves privadas
- Secrets de aplicações
- Variáveis de ambiente sensíveis
- Webhook Secrets
- Tokens OAuth
- Credenciais de infraestrutura

O sistema deve ser projetado como um **Secrets Manager corporativo**, com foco em segurança, auditoria, governança e integração com o ecossistema Darckware.

---

# 2. Objetivos

## 2.1 Objetivo principal

Eliminar a dispersão de credenciais em:

- arquivos `.env`
- planilhas
- mensagens
- documentos
- scripts
- repositórios Git
- configurações locais
- anotações pessoais
- bancos sem criptografia adequada

## 2.2 Objetivos secundários

- Centralizar credenciais.
- Aplicar controle de acesso.
- Registrar auditoria de uso.
- Permitir rotação de secrets.
- Evitar exposição direta de credenciais.
- Integrar agentes do ForgeHub.
- Integrar providers do ForgeRouter.
- Permitir uso por aplicações externas.
- Suportar ambientes on-premises e híbridos.
- Permitir expansão futura para secrets dinâmicos.

---

# 3. Posicionamento no Ecossistema Darckware

```text
Darckware
│
├── ForgeHub
│   ├── Agentes
│   ├── Projetos
│   ├── Tarefas
│   ├── Planejamento
│   └── Comunicação
│
├── ForgeRouter
│   ├── Providers
│   ├── Modelos
│   ├── Roteamento
│   └── Políticas de IA
│
└── ForgeVault
    ├── Secrets
    ├── Credenciais
    ├── Tokens
    ├── Chaves
    ├── Certificados
    ├── Controle de acesso
    └── Auditoria
```

---

# 4. Casos de Uso

## 4.1 Usuário humano

Um administrador acessa o ForgeVault para:

- cadastrar uma API Key
- atualizar uma senha
- consultar uma credencial
- revogar um token
- revisar logs de acesso
- alterar permissões

## 4.2 Agente de IA

Um agente do ForgeHub solicita acesso a um secret:

```text
AGENTE
   │
   ▼
ForgeHub
   │
   ▼
ForgeVault API
   │
   ├── valida identidade
   ├── valida projeto
   ├── valida ambiente
   ├── valida política
   ├── registra auditoria
   ▼
SECRET
```

## 4.3 ForgeRouter

O ForgeRouter pode recuperar credenciais de providers:

```text
ForgeRouter
   │
   ├── OpenAI
   ├── Anthropic
   ├── Google
   ├── OpenRouter
   └── outros
        │
        ▼
    ForgeVault
```

O objetivo é evitar API Keys persistidas diretamente em arquivos de configuração.

---

# 5. Escopo Inicial

O MVP deve incluir:

- autenticação
- organizações
- usuários
- projetos
- ambientes
- cadastro de secrets
- criptografia
- controle de acesso
- histórico de versões
- auditoria
- API REST
- interface web
- integração inicial com ForgeHub e ForgeRouter
- backup
- logs
- observabilidade básica

---

# 6. Arquitetura Proposta

```text
                        ┌──────────────────┐
                        │      Web UI      │
                        └────────┬─────────┘
                                 │
                                 ▼
                        ┌──────────────────┐
                        │   API Gateway    │
                        └────────┬─────────┘
                                 │
                ┌────────────────┴────────────────┐
                │                                 │
                ▼                                 ▼
       ┌──────────────────┐              ┌──────────────────┐
       │ Authentication   │              │ Secret Service   │
       └──────────────────┘              └────────┬─────────┘
                                                  │
                        ┌─────────────────────────┼──────────────────────┐
                        │                         │                      │
                        ▼                         ▼                      ▼
                ┌──────────────┐          ┌──────────────┐      ┌──────────────┐
                │ PostgreSQL   │          │ Crypto Layer │      │ Audit Engine │
                └──────────────┘          └──────┬───────┘      └──────────────┘
                                                │
                                                ▼
                                        ┌───────────────┐
                                        │ Master Key /  │
                                        │ KMS / HSM     │
                                        └───────────────┘
```

---

# 7. Componentes

## 7.1 Web UI

Responsável por:

- autenticação
- dashboard
- criação de secrets
- consulta controlada
- organização por projetos
- gestão de permissões
- auditoria
- aprovação de acesso
- gestão de versões

Sugestão:

```text
React
TypeScript
Vite ou Next.js
TailwindCSS
```

---

## 7.2 Backend

Sugestão principal:

```text
ASP.NET Core
.NET 10+
C#
REST API
OpenAPI
Entity Framework Core
```

Alternativa:

```text
Node.js
NestJS
TypeScript
```

Para o ecossistema Darckware, ASP.NET Core oferece melhor aderência para APIs corporativas, segurança, performance e manutenção de longo prazo.

---

# 8. Organização dos Secrets

```text
Organization
└── Project
    └── Environment
        └── Secret
```

Exemplo:

```text
Darckware
├── ForgeRouter
│   ├── Development
│   │   ├── OPENAI_API_KEY
│   │   └── OPENROUTER_API_KEY
│   │
│   └── Production
│       ├── OPENAI_API_KEY
│       └── OPENROUTER_API_KEY
│
└── ForgeHub
    ├── Development
    └── Production
```

---

# 9. Ambientes

Padrão recomendado:

```text
development
staging
production
shared
```

Nunca compartilhar automaticamente secrets entre ambientes.

---

# 10. Tipos de Secrets

```text
PASSWORD
API_KEY
ACCESS_TOKEN
REFRESH_TOKEN
LLM_TOKEN
SSH_PRIVATE_KEY
SSH_PASSWORD
DATABASE_CREDENTIAL
CERTIFICATE
PRIVATE_KEY
SERVICE_ACCOUNT
WEBHOOK_SECRET
ENV_SECRET
OAUTH_CLIENT
GENERIC_SECRET
```

---

# 11. Modelo de Dados

## Organization

```text
id
name
slug
status
created_at
updated_at
```

## Project

```text
id
organization_id
name
slug
description
status
created_at
updated_at
```

## Environment

```text
id
project_id
name
slug
created_at
```

## Secret

```text
id
environment_id
name
type
provider
description
owner_id
status
current_version
created_at
updated_at
expires_at
rotation_policy_id
```

## SecretVersion

```text
id
secret_id
version
encrypted_value
encrypted_data_key
nonce
algorithm
created_by
created_at
```

## AuditLog

```text
id
actor_id
actor_type
action
resource_type
resource_id
source_ip
user_agent
request_id
timestamp
metadata
```

---

# 12. Criptografia

## 12.1 Princípio

O banco nunca deve armazenar secrets em texto puro.

## 12.2 Envelope Encryption

Arquitetura recomendada:

```text
SECRET
  │
  ▼
DATA ENCRYPTION KEY
  │
  ▼
AES-256-GCM
  │
  ▼
SECRET CRIPTOGRAFADO
```

A Data Encryption Key também deve ser criptografada:

```text
DEK
 │
 ▼
MASTER KEY
 │
 ▼
ENCRYPTED DEK
```

## 12.3 Algoritmo

Recomendação:

```text
AES-256-GCM
```

Fornece:

- confidencialidade
- integridade
- autenticação do ciphertext

---

# 13. Master Key

A Master Key não deve ficar:

```text
❌ banco de dados
❌ repositório Git
❌ arquivo .env da aplicação
❌ Dockerfile
```

Opções:

1. HashiCorp Vault Transit
2. Azure Key Vault
3. AWS KMS
4. Google Cloud KMS
5. HSM
6. arquivo protegido no host com permissão restrita para MVP

Para MVP on-premises:

```text
/root/.forgevault/master.key
```

Permissões:

```bash
chmod 600 /root/.forgevault/master.key
chown root:root /root/.forgevault/master.key
```

Para produção madura, utilizar KMS ou HSM.

---

# 14. Autenticação

MVP:

```text
email + senha
MFA
JWT curto
Refresh Token
```

Posteriormente:

```text
OAuth2
OIDC
SSO
Microsoft Entra ID
Google Workspace
LDAP/Active Directory
```

---

# 15. MFA

Suportar:

```text
TOTP
WebAuthn
Passkeys
```

MFA obrigatório para:

- administradores
- leitura de secrets críticos
- break-glass
- alteração de permissões
- exportação

---

# 16. Autorização

Combinar:

```text
RBAC + ABAC
```

## Roles

```text
OWNER
ADMIN
SECURITY_ADMIN
PROJECT_ADMIN
DEVELOPER
OPERATOR
AUDITOR
READ_ONLY
AGENT
SERVICE_ACCOUNT
```

---

# 17. ABAC

Permissões adicionais podem considerar:

```text
organization
project
environment
secret_type
network
time
actor_type
risk_level
```

Exemplo:

```text
AGENT=ATHOS
PROJECT=FORGEHUB
ENVIRONMENT=PRODUCTION
SECRET=OPENAI_API_KEY
ACTION=READ
```

---

# 18. Política de Acesso

Exemplo:

```json
{
  "subject": "agent:athos",
  "resource": "secret:forge-router/prod/openai",
  "actions": ["read"],
  "conditions": {
    "project": "forge-router",
    "environment": "production"
  }
}
```

---

# 19. Mascaramento

Secrets devem aparecer por padrão como:

```text
sk-proj-************************************
```

Visualização completa deve exigir ação explícita.

---

# 20. Auditoria

Registrar:

```text
LOGIN
LOGOUT
SECRET_CREATE
SECRET_READ
SECRET_REVEAL
SECRET_UPDATE
SECRET_DELETE
SECRET_ROTATE
ACCESS_GRANTED
ACCESS_REVOKED
POLICY_CHANGE
EXPORT
BREAK_GLASS
FAILED_ACCESS
```

---

# 21. Regra Crítica de Auditoria

Nunca registrar o valor do secret.

Correto:

```json
{
  "action": "SECRET_READ",
  "secret_id": "SEC_123",
  "actor": "agent:athos"
}
```

Errado:

```json
{
  "secret": "sk-123456789"
}
```

---

# 22. Versionamento

Cada alteração gera nova versão.

```text
OPENAI_API_KEY

v1
v2
v3 ← atual
```

Versões antigas devem permanecer criptografadas e acessíveis somente a perfis autorizados.

---

# 23. Rotação

Tipos:

```text
MANUAL
SCHEDULED
PROVIDER_API
```

Exemplo:

```text
OPENAI_API_KEY
rotation_interval = 90 days
```

---

# 24. Expiração

Campos:

```text
expires_at
rotation_due_at
last_rotated_at
```

Alertas:

```text
30 dias
15 dias
7 dias
1 dia
```

---

# 25. Leases e TTL

Fase futura:

```text
Agent solicita secret
     │
     ▼
ForgeVault gera lease
     │
     ▼
TTL = 15 minutos
     │
     ▼
lease expira
```

Ideal para agentes e automações.

---

# 26. Break-Glass

Mecanismo emergencial para acesso administrativo.

Deve exigir:

- MFA
- justificativa
- auditoria
- notificação
- expiração automática

---

# 27. API REST

Base:

```text
https://vault.darckware.net/api/v1
```

---

# 28. Endpoints

## Authentication

```http
POST /auth/login
POST /auth/refresh
POST /auth/logout
POST /auth/mfa/verify
```

## Secrets

```http
GET    /secrets
POST   /secrets
GET    /secrets/{id}
PUT    /secrets/{id}
DELETE /secrets/{id}
POST   /secrets/{id}/rotate
GET    /secrets/{id}/versions
```

---

# 29. Criar Secret

```http
POST /api/v1/secrets
```

```json
{
  "name": "OPENAI_API_KEY",
  "type": "LLM_TOKEN",
  "provider": "openai",
  "project": "forge-router",
  "environment": "production",
  "value": "SECRET_VALUE"
}
```

---

# 30. Recuperar Secret

```http
GET /api/v1/secrets/SEC_ID/value
```

O endpoint deve exigir permissão específica:

```text
secret.read.value
```

---

# 31. Service Accounts

Aplicações devem utilizar identidades próprias.

Exemplo:

```text
service:forgerouter
service:forgehub
service:monitoring
```

Nunca reutilizar usuário humano para integrações.

---

# 32. Tokens de Serviço

Formato conceitual:

```text
fv_sa_xxxxxxxxxxxxxxxxxx
```

Armazenar somente hash do token de autenticação.

---

# 33. CLI

Nome sugerido:

```text
fv
```

Exemplos:

```bash
fv login

fv secret list

fv secret get OPENAI_API_KEY

fv secret set OPENAI_API_KEY

fv secret rotate OPENAI_API_KEY
```

---

# 34. Injeção em Processos

Exemplo:

```bash
fv exec -- dotnet ForgeRouter.dll
```

O ForgeVault injeta secrets somente no processo.

---

# 35. Exportação Temporária

Exemplo:

```bash
fv export --format env --project FORGEROUTER --environment PRODUCTION
```

Saída:

```text
OPENAI_API_KEY=...
OPENROUTER_API_KEY=...
```

Recomenda-se limitar ou desabilitar esse recurso em produção.

---

# 36. Integração ForgeHub

Fluxo:

```text
ForgeHub Agent
      │
      ▼
Service Account
      │
      ▼
ForgeVault
      │
      ├── RBAC
      ├── ABAC
      ├── Audit
      └── Secret
```

Cada agente pode possuir identidade individual.

Exemplo:

```text
agent:athos
agent:lara
agent:aegis
agent:daedalus
```

---

# 37. Integração ForgeRouter

O ForgeRouter não deve armazenar permanentemente API Keys de providers.

Fluxo:

```text
Request
   │
   ▼
ForgeRouter
   │
   ▼
ForgeVault
   │
   ▼
Provider Credential
   │
   ▼
OpenAI / Anthropic / etc.
```

---

# 38. Cache

Secrets podem permanecer temporariamente em memória.

Nunca utilizar cache persistente sem criptografia.

TTL recomendado:

```text
30 segundos a 5 minutos
```

Dependendo do tipo de secret.

---

# 39. Banco de Dados

Recomendado:

```text
PostgreSQL
```

Motivos:

- confiabilidade
- transações
- JSONB
- extensibilidade
- boa integração com .NET
- excelente suporte a auditoria

---

# 40. Redis

Opcional para:

- rate limiting
- sessões
- locks distribuídos
- filas temporárias
- cache não sensível

Nunca armazenar secrets puros no Redis.

---

# 41. Mensageria

Para eventos:

```text
RabbitMQ
NATS
Redis Streams
```

Eventos:

```text
SecretCreated
SecretRotated
SecretExpired
AccessDenied
PolicyChanged
```

---

# 42. Observabilidade

Stack sugerida:

```text
Prometheus
Grafana
Loki
OpenTelemetry
```

Métricas:

```text
vault_requests_total
vault_denied_total
vault_secret_reads_total
vault_rotation_failures_total
vault_api_latency
vault_active_sessions
```

---

# 43. Logs

Logs estruturados JSON.

Exemplo:

```json
{
  "timestamp": "2026-09-11T20:00:00Z",
  "level": "INFO",
  "event": "SECRET_READ",
  "actor": "agent:athos",
  "secret": "SEC_123"
}
```

Nunca registrar valores sensíveis.

---

# 44. Rate Limiting

Exemplo:

```text
login:
5 tentativas/minuto

API:
100 requests/minuto

secret reveal:
20/minuto
```

Configuração por usuário, agente e IP.

---

# 45. Sessões

JWT:

```text
5 a 15 minutos
```

Refresh Token:

```text
7 a 30 dias
```

Refresh Tokens devem ser:

- rotacionados
- revogáveis
- armazenados por hash

---

# 46. Segurança HTTP

Obrigatório:

```text
HTTPS
TLS 1.2+
HSTS
Secure Cookies
HttpOnly
SameSite
CSP
X-Content-Type-Options
Referrer-Policy
```

---

# 47. Reverse Proxy

Sugestão:

```text
Cloudflare
        │
        ▼
Nginx / Traefik
        │
        ▼
ForgeVault
```

---

# 48. DNS

Sugestão:

```text
vault.darckware.net
```

API:

```text
vault.darckware.net/api
```

---

# 49. Containerização

Estrutura:

```text
forgevault/
├── api/
├── web/
├── worker/
├── migrations/
├── docker/
└── docs/
```

---

# 50. Docker Compose

Exemplo conceitual:

```yaml
services:

  api:
    image: darckware/forgevault-api:latest

  web:
    image: darckware/forgevault-web:latest

  postgres:
    image: postgres:17

  redis:
    image: redis:8
```

Secrets reais não devem ser gravados diretamente no arquivo Compose.

---

# 51. Estrutura de Repositório

```text
forgevault/
│
├── src/
│   ├── ForgeVault.Api/
│   ├── ForgeVault.Application/
│   ├── ForgeVault.Domain/
│   ├── ForgeVault.Infrastructure/
│   ├── ForgeVault.Worker/
│   └── ForgeVault.Web/
│
├── tests/
│   ├── Unit/
│   ├── Integration/
│   ├── Security/
│   └── E2E/
│
├── deploy/
│   ├── docker/
│   ├── kubernetes/
│   └── scripts/
│
├── docs/
│
├── docker-compose.yml
├── README.md
└── LICENSE
```

---

# 52. Clean Architecture

```text
Domain
   ↑
Application
   ↑
Infrastructure
   ↑
API
```

O domínio não deve depender da infraestrutura.

---

# 53. Threat Model

Ameaças principais:

## Exfiltração do banco

Mitigação:

- criptografia
- envelope encryption

## Roubo de token

Mitigação:

- TTL curto
- rotação
- revogação

## Insider

Mitigação:

- RBAC
- ABAC
- auditoria
- aprovação

## SQL Injection

Mitigação:

- ORM
- queries parametrizadas

## Credential stuffing

Mitigação:

- MFA
- rate limiting
- bloqueio progressivo

---

# 54. Hardening

Aplicação:

```text
non-root container
read-only filesystem
drop Linux capabilities
seccomp
AppArmor
```

Banco:

```text
rede privada
sem exposição pública
TLS
usuário dedicado
least privilege
```

---

# 55. Backup

Backup deve incluir:

```text
database
encrypted secrets
configuration
audit logs
```

Master Key deve possuir backup separado.

---

# 56. Regra de Ouro

Nunca armazenar:

```text
BACKUP DO BANCO + MASTER KEY
```

no mesmo local.

---

# 57. Disaster Recovery

Definir:

```text
RPO
RTO
```

Sugestão inicial:

```text
RPO <= 1 hora
RTO <= 4 horas
```

Para ambiente crítico, reduzir conforme necessidade.

---

# 58. Governança e LGPD

Embora secrets normalmente não sejam dados pessoais, o sistema pode armazenar credenciais associadas a pessoas.

Aplicar:

- princípio do menor privilégio
- rastreabilidade
- retenção
- controle de acesso
- segregação
- accountability

---

# 59. DevSecOps

Pipeline:

```text
Commit
  │
  ▼
SAST
  │
  ▼
Dependency Scan
  │
  ▼
Secret Scan
  │
  ▼
Tests
  │
  ▼
Container Scan
  │
  ▼
Deploy
```

Ferramentas possíveis:

```text
Gitleaks
Trivy
Semgrep
Dependabot
CodeQL
```

---

# 60. CI/CD

Branches:

```text
main
develop
feature/*
hotfix/*
```

Ambientes:

```text
development
staging
production
```

Deploy em produção somente após aprovação.

---

# 61. Testes

## Unitários

- criptografia
- políticas
- roles
- expiração

## Integração

- banco
- cache
- autenticação
- API

## Segurança

- privilege escalation
- IDOR
- injection
- brute force
- replay
- token reuse

## E2E

- cadastro
- login
- criação de secret
- leitura
- rotação
- auditoria

---

# 62. Critérios de Aceite do MVP

O MVP é considerado funcional quando:

- usuário autentica com MFA
- consegue criar organization
- consegue criar projeto
- consegue criar environment
- consegue criar secret
- valor fica criptografado
- usuário autorizado consegue recuperar
- usuário não autorizado recebe 403
- toda leitura gera auditoria
- versões são mantidas
- secrets podem expirar
- ForgeHub consegue autenticar
- ForgeRouter consegue recuperar credencial
- backup pode ser restaurado

---

# 63. Roadmap

## Fase 1 — MVP

- Web UI
- API
- Auth
- MFA
- RBAC
- Secrets
- Encryption
- Audit
- PostgreSQL

## Fase 2

- ForgeHub
- ForgeRouter
- Service Accounts
- CLI
- ABAC
- rotação

## Fase 3

- Dynamic Secrets
- leases
- TTL
- approvals
- break-glass
- notifications

## Fase 4

- KMS
- HSM
- SSO
- LDAP
- Active Directory
- multi-tenant avançado

---

# 64. Convenções de Nomes

Formato recomendado:

```text
PROVIDER_RESOURCE_PURPOSE
```

Exemplo:

```text
OPENAI_API_KEY
OPENROUTER_API_KEY
POSTGRES_ADMIN_PASSWORD
CLOUDFLARE_API_TOKEN
GITHUB_DEPLOY_TOKEN
FORGEHUB_SIGNING_KEY
```

---

# 65. Identificadores

Sugestão:

```text
ORG_xxx
PRJ_xxx
ENV_xxx
SEC_xxx
POL_xxx
USR_xxx
AGT_xxx
SVC_xxx
AUD_xxx
```

---

# 66. Políticas de Segurança

Princípios obrigatórios:

1. Zero secrets em texto puro no banco.
2. Zero secrets em logs.
3. Zero secrets no Git.
4. Zero API Keys hardcoded.
5. MFA obrigatório para administradores.
6. Least privilege.
7. Auditoria obrigatória.
8. Segregação por ambiente.
9. Rotação periódica.
10. Revogação imediata.
11. Backup criptografado.
12. Master Key isolada.

---

# 67. Fluxo Ideal para Agentes

```text
Agent
  │
  ▼
ForgeHub Identity
  │
  ▼
ForgeVault Authorization
  │
  ▼
Policy Engine
  │
  ▼
Temporary Secret Access
  │
  ▼
Provider
```

O agente nunca precisa conhecer permanentemente o secret.

---

# 68. Visão Futura

O ForgeVault deve evoluir de simples gerenciador de credenciais para uma infraestrutura central de identidade e secrets:

```text
ForgeVault
│
├── Secrets Manager
├── Credential Broker
├── Dynamic Credentials
├── Certificate Manager
├── Key Management
├── Access Policies
├── Audit
├── Machine Identity
└── Agent Identity
```

---

# 69. Recomendação Arquitetural

Para a primeira versão:

```text
Frontend:
React + TypeScript

Backend:
ASP.NET Core

Database:
PostgreSQL

Cache:
Redis

Encryption:
AES-256-GCM

Authentication:
JWT + Refresh Token + MFA

Proxy:
Nginx ou Traefik

Observability:
Prometheus + Grafana + Loki

Deployment:
Docker Compose
```

Depois da estabilização:

```text
Kubernetes
KMS/HSM
OIDC
SSO
Dynamic Secrets
```

---

# 70. Conclusão

O **ForgeVault** deve ser tratado como componente crítico do ecossistema Darckware.

Sua responsabilidade não é apenas armazenar senhas, mas funcionar como a camada central de custódia, autorização, distribuição e auditoria de credenciais.

A arquitetura deve privilegiar:

```text
SEGURANÇA
    +
MENOR PRIVILÉGIO
    +
RASTREABILIDADE
    +
AUTOMAÇÃO
    +
ROTAÇÃO
    +
INTEGRAÇÃO
```

A evolução natural do produto é permitir que aplicações e agentes nunca precisem conhecer ou persistir permanentemente secrets, utilizando o ForgeVault como broker seguro entre identidades, aplicações e provedores externos.

---

# 71. Atualização Arquitetural — ForgeVault como Security Plane da Darckware

O ForgeVault deve ser tratado como um serviço transversal e independente, consumido por todos os projetos e agentes do ecossistema Darckware.

Definição oficial:

> **ForgeVault é a plataforma central de identidade operacional, secrets, credenciais, autorização, intermediação de acesso e auditoria do ecossistema Darckware.**

O objetivo é permitir que humanos, agentes, sistemas e máquinas utilizem credenciais de forma segura, contextual, temporária e auditável.

---

# 72. Domínios Funcionais

O ForgeVault deve ser estruturado em quatro planos:

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

---

# 73. Integração com o Ecossistema Darckware

Arquitetura de alto nível:

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

Consumidores esperados:

```text
ForgeHub
ForgeRouter
Hermes
Agentes Darckware
MCPs
n8n
CI/CD
Darckware Site
Aplicações internas
Aplicações de clientes
Servidores
Bancos de dados
Serviços de terceiros
```

---

# 74. Identidades

Toda entidade que executa ações deve possuir identidade própria.

Tipos:

```text
HumanIdentity
AgentIdentity
ServiceIdentity
MachineIdentity
McpClientIdentity
```

Exemplos:

```text
user:marcelo

agent:athos
agent:lara
agent:aegis
agent:daedalus
agent:hephaestus

service:forgehub
service:forgerouter
service:hermes
service:n8n

machine:vps01
machine:srv-app02

mcp:hermes-athos
mcp:forgehub
mcp:forgevault
```

---

# 75. Tokens Individuais por Agente

Cada agente deve possuir seu próprio token de autenticação no ForgeVault.

Exemplo:

```text
agent:athos
token: fv_agent_ATHOS_xxxxxxxxx

agent:lara
token: fv_agent_LARA_xxxxxxxxx

agent:aegis
token: fv_agent_AEGIS_xxxxxxxxx
```

O token identifica o agente perante o ForgeVault.

O token do agente não é a credencial de destino.

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

Benefícios:

- identidade individual;
- segregação de privilégios;
- revogação seletiva;
- auditoria por agente;
- definição de escopos por agente;
- rastreabilidade operacional.

---

# 76. Tokens de Sistemas

Sistemas também podem possuir identidade e token próprios quando executam tarefas de forma autônoma.

Exemplos:

```text
service:forgehub
service:forgerouter
service:hermes
service:n8n
service:darckware-site
```

Formato sugerido:

```text
fv_service_xxxxxxxxx
```

Regra:

> Tokens de agentes e tokens de sistemas representam identidades diferentes e não devem ser compartilhados.

---

# 77. Credenciais de Recursos

Credenciais pertencem aos recursos consumidos.

Exemplos:

```text
database:postgres-forgehub
database:postgres-forgevault

provider:openai
provider:anthropic
provider:openrouter

github:darckware
cloudflare:darckware.net

server:vps01
server:srv-app02
```

Separação conceitual:

```text
Credencial DE autenticação no ForgeVault
≠
Credencial DO recurso final
```

---

# 78. Taxonomia de Credenciais

```text
PASSWORD
API_KEY
ACCESS_TOKEN
REFRESH_TOKEN
LLM_TOKEN
SSH_PRIVATE_KEY
SSH_PASSWORD
DATABASE_CREDENTIAL
OAUTH_CLIENT
CERTIFICATE
PRIVATE_KEY
SERVICE_ACCOUNT
WEBHOOK_SECRET
ENV_SECRET
TOTP_SEED
SYSTEM_CREDENTIAL
GENERIC_SECRET
```

---

# 79. Database Credential

Tipo específico:

```text
DATABASE_CREDENTIAL
```

Estrutura conceitual:

```json
{
  "engine": "postgresql",
  "host": "DB_HOST",
  "port": 5432,
  "database": "DB_NAME",
  "username": "DB_USER",
  "password": "DB_PASSWORD",
  "ssl_mode": "require"
}
```

O valor sensível deve permanecer criptografado no banco.

---

# 80. Credential Binding

Entidade que define quais identidades podem utilizar quais credenciais.

```text
CredentialBinding
├── id
├── identity_id
├── credential_id
├── permission
├── project_id
├── environment_id
├── conditions
├── issued_at
├── expires_at
└── status
```

Exemplos:

```text
agent:athos
    → github:darckware
    → READ

agent:hephaestus
    → cloudflare:darckware.net
    → DNS.UPDATE

service:forgerouter
    → provider:openai
    → USE

service:forgehub
    → database:postgres-forgehub
    → CONNECT
```

---

# 81. Organização Multi-Tenant

Modelo recomendado:

```text
Tenant
└── Workspace
    └── Project
        └── Environment
            └── Resource
                └── Credential
```

Exemplo:

```text
DARCKWARE
├── CORE
│   ├── ForgeVault
│   ├── ForgeHub
│   └── ForgeRouter
├── AI
│   ├── Hermes
│   └── Agents
├── INFRA
│   ├── Proxmox
│   ├── Cloudflare
│   ├── VPS
│   └── Docker
└── CLIENTS
    ├── Client-A
    └── Client-B
```

---

# 82. PostgreSQL — Especificação Oficial

Banco principal:

```text
PostgreSQL 17+
```

Stack:

```text
ORM:
Entity Framework Core

Provider:
Npgsql

Migrations:
EF Core Migrations

Data model:
Relacional + JSONB

Primary keys:
UUID

Timestamps:
TIMESTAMPTZ em UTC
```

Entidades principais:

```text
tenants
workspaces
projects
environments

identities
users
agents
service_accounts
machine_identities
mcp_clients

agent_tokens
service_tokens
token_scopes
token_rotations
token_usage

roles
permissions
policies
role_assignments

resources
credentials
credential_bindings
secret_versions

credential_requests
access_grants
leases
sessions

approval_requests
approval_actions

integrations
providers
oauth_credentials

audit_logs
events
notifications
```

---

# 83. Armazenamento Criptografado

Valores sensíveis nunca devem ser persistidos como texto puro.

Exemplo:

```sql
CREATE TABLE secret_versions (
    id UUID PRIMARY KEY,
    secret_id UUID NOT NULL,
    version INTEGER NOT NULL,

    ciphertext BYTEA NOT NULL,
    encrypted_dek BYTEA NOT NULL,
    nonce BYTEA NOT NULL,
    auth_tag BYTEA,

    algorithm VARCHAR(50) NOT NULL DEFAULT 'AES-256-GCM',

    created_by UUID NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),

    UNIQUE(secret_id, version)
);
```

O PostgreSQL armazena somente:

```text
ciphertext
encrypted DEK
nonce
metadata criptográfica
```

A Master Key permanece fora do banco.

---

# 84. Retorno JSON Padronizado de Credenciais

O retorno padrão da API deve utilizar um envelope comum.

```json
{
  "request_id": "REQ_ID",
  "identity": "IDENTITY_ID",
  "resource": "RESOURCE_ID",
  "access_mode": "REVEAL|BROKER|SESSION|LEASE|INJECT",
  "expires_at": "ISO_DATE",
  "credentials": {},
  "session": null,
  "broker": null,
  "lease": null,
  "metadata": {}
}
```

Regra:

> Se `access_mode = REVEAL`, o campo `credentials` pode conter valores sensíveis. Nos demais modos, o campo deve ser nulo ou mascarado.

---

# 85. Exemplo JSON — PostgreSQL

```json
{
  "request_id": "REQ_123456",
  "identity": "agent:athos",
  "resource": "database:postgres-forgehub",
  "access_mode": "REVEAL",
  "expires_at": "2026-09-11T21:30:00-03:00",
  "credentials": {
    "engine": "postgresql",
    "host": "db.internal.darckware.net",
    "port": 5432,
    "database": "forgehub",
    "username": "forgehub_app",
    "password": "SECRET_VALUE",
    "ssl_mode": "require"
  }
}
```

---

# 86. Exemplo JSON — API Key

```json
{
  "request_id": "REQ_789012",
  "identity": "agent:athos",
  "resource": "provider:openai",
  "access_mode": "REVEAL",
  "credentials": {
    "type": "api_key",
    "provider": "openai",
    "api_key": "SECRET_VALUE"
  }
}
```

---

# 87. Exemplo JSON — Login em Site

```json
{
  "request_id": "REQ_345678",
  "identity": "agent:lara",
  "resource": "site:crm-cliente-a",
  "access_mode": "REVEAL",
  "credentials": {
    "url": "https://crm.example.com",
    "username": "USER",
    "password": "SECRET_VALUE"
  }
}
```

---

# 88. Exemplo JSON — SSH

```json
{
  "request_id": "REQ_456789",
  "identity": "agent:hephaestus",
  "resource": "server:vps01",
  "access_mode": "REVEAL",
  "credentials": {
    "host": "SERVER_HOST",
    "port": 22,
    "username": "root",
    "private_key": "-----BEGIN OPENSSH PRIVATE KEY-----\n...\n-----END OPENSSH PRIVATE KEY-----"
  }
}
```

---

# 89. Modos de Acesso

Modos suportados:

```text
BROKER
SESSION
LEASE
INJECT
REVEAL
```

Prioridade recomendada:

```text
BROKER > SESSION > LEASE > INJECT > REVEAL
```

## BROKER

O ForgeVault utiliza a credencial em nome do consumidor.

## SESSION

O ForgeVault cria ou intermedeia uma sessão autenticada.

## LEASE

Credencial ou grant temporário com TTL.

## INJECT

Secret é injetado somente no processo autorizado.

## REVEAL

Valor real é retornado. Deve ser exceção.

---

# 90. MCP Server Nativo

O ForgeVault deve possuir MCP Server próprio.

Endpoint sugerido:

```text
https://vault.darckware.net/mcp
```

Arquitetura:

```text
Hermes / Agents / MCP Clients
            │
            ▼
      ForgeVault MCP
            │
            ▼
      ForgeVault API
            │
    ┌───────┼────────┐
    ▼       ▼        ▼
 Policy   Secrets   Audit
```

---

# 91. MCP Tools

Tools padrão:

```text
capability.list
capability.check

credential.request
credential.status
credential.release

access.request
access.status
access.release

session.request
session.status
session.close

approval.status
secret.metadata
```

Tools administrativas:

```text
admin.secret.create
admin.secret.update
admin.secret.rotate
admin.secret.revoke

admin.policy.create
admin.policy.update
admin.policy.assign

admin.audit.search
```

Não disponibilizar para agentes comuns:

```text
get_all_secrets
dump_vault
export_credentials
```

---

# 92. Integração com Hermes

Modelo:

```text
Hermes
├── ForgeHub MCP
└── ForgeVault MCP
```

Responsabilidades:

```text
ForgeHub MCP
├── projetos
├── tarefas
├── mensagens
└── planejamento

ForgeVault MCP
├── credenciais
├── capacidades
├── acessos
├── sessões
├── grants
└── aprovações
```

O profile do Hermes pode representar a identidade operacional do agente:

```text
hermes profile chat athos
        │
        ▼
agent:athos
```

---

# 93. Integração com ForgeHub

Princípio:

```text
ForgeHub = contexto operacional
ForgeVault = contexto de segurança
```

ForgeHub fornece:

```text
task_id
project_id
assigned_agent
task_status
required_action
```

ForgeVault avalia a autorização com base nesse contexto.

---

# 94. Integração com ForgeRouter

O ForgeRouter deve utilizar o ForgeVault como origem das credenciais de LLM providers.

```text
ForgeRouter
   │
   ▼
ForgeVault
   │
   ├── OpenAI
   ├── Anthropic
   ├── OpenRouter
   └── Google
```

As API Keys não devem permanecer hardcoded nem persistidas desnecessariamente no ForgeRouter.

---

# 95. Policy Engine

Modelo:

```text
RBAC + ABAC + Contextual Authorization
```

Contexto:

```text
identity
agent
service
project
task
environment
resource
action
risk_level
time
network
tenant
workspace
```

Resultados:

```text
ALLOW
APPROVAL_REQUIRED
DENY
```

Regra:

> DENY explícito prevalece sobre ALLOW.

---

# 96. Níveis de Risco

```text
L1 - Internal
L2 - Sensitive
L3 - Critical
L4 - Restricted
```

Exemplos:

```text
OPENAI_API_KEY              → L2
DATABASE_PROD_PASSWORD      → L3
CLOUDFLARE_GLOBAL_KEY       → L4
PROXMOX_ROOT                → L4
```

---

# 97. Aprovação Humana

Aprovação humana deve ser exceção.

Política sugerida:

```text
L1 → AUTO
L2 → AUTO ou TASK_BOUND
L3 → APPROVAL_REQUIRED
L4 → APPROVAL_REQUIRED
```

Tipos:

```text
ONCE
TIME_BOUND
TASK_BOUND
PROJECT_BOUND
PERMANENT_POLICY
```

---

# 98. Access Grant

```text
AccessGrant
├── id
├── identity_id
├── task_id
├── project_id
├── credential_id
├── resource
├── action
├── access_mode
├── granted_by
├── issued_at
├── expires_at
├── status
└── justification
```

---

# 99. Credential Request

```text
CredentialRequest
├── id
├── identity_id
├── task_id
├── project_id
├── service
├── resource
├── requested_action
├── reason
├── requested_at
├── risk_level
└── status
```

Fluxo:

```text
CredentialRequest
       │
       ▼
Policy Evaluation
       │
       ├── Grant
       ├── ApprovalRequest
       └── Denied
```

---

# 100. Revoke — Modelo Oficial

O ForgeVault deve suportar múltiplos níveis de revogação.

```text
ACCESS REVOKE
SESSION REVOKE
LEASE REVOKE
CREDENTIAL REVOKE
PROVIDER REVOKE
CASCADE REVOKE
```

---

# 101. Revoke Access

Remove apenas a permissão de uma identidade.

```text
agent:athos
↓
não pode mais acessar
↓
database:postgres-forgehub
```

A credencial permanece válida para outros consumidores autorizados.

---

# 102. Revoke Credential

Fluxo:

```text
1. marcar credencial como REVOKED
2. impedir novos retornos
3. revogar grants ativos
4. revogar leases
5. encerrar sessões
6. invalidar caches
7. revogar no provedor externo quando suportado
8. registrar auditoria
```

Estados:

```text
ACTIVE
SUSPENDED
REVOKED
EXPIRED
ROTATING
ARCHIVED
```

---

# 103. Revoke API

```http
POST /credentials/{id}/revoke
POST /grants/{id}/revoke
POST /sessions/{id}/revoke
POST /leases/{id}/revoke
```

Exemplo:

```json
{
  "cascade": true,
  "provider_revoke": true,
  "reason": "credential compromised"
}
```

Resposta:

```json
{
  "operation": "revoke",
  "resource": "credential:CRED_123",
  "status": "REVOKED",
  "provider_revocation": "SUCCESS",
  "revoked_grants": 4,
  "revoked_sessions": 2,
  "revoked_leases": 1
}
```

---

# 104. Rotate

Rotate é diferente de revoke.

```text
ROTATE != REVOKE
```

Fluxo recomendado:

```text
cria nova credencial
↓
valida nova credencial
↓
marca nova como ACTIVE
↓
migra consumidores
↓
revoga antiga
↓
atualiza auditoria
```

---

# 105. Lifecycle Completo de Credenciais

```text
CREATE
VALIDATE
ACTIVATE
USE
ROTATE
SUSPEND
REVOKE
EXPIRE
ARCHIVE
PURGE
```

---

# 106. Secret Versioning

Toda alteração gera uma nova versão.

```text
Credential
├── v1
├── v2
└── v3 ← CURRENT
```

Nunca sobrescrever secret silenciosamente.

---

# 107. Credential Ownership

Toda credencial deve possuir:

```text
owner
tenant
workspace
project
environment
resource
responsible_team
risk_level
created_at
expires_at
rotation_policy
```

---

# 108. Dependency Mapping

O ForgeVault deve saber quais consumidores utilizam determinada credencial.

Exemplo:

```text
CRED_OPENAI_PROD
├── ForgeRouter
├── agent:athos
└── automation:content-worker
```

Antes de revoke/rotate deve ser possível executar análise de impacto.

---

# 109. Impact Analysis

Exemplo:

```text
Credential:
CRED_OPENAI_PROD

Consumers affected:
3

- ForgeRouter production
- agent:athos
- worker:content
```

Isso deve ser exibido antes de operações administrativas críticas.

---

# 110. Credential Validation

Credenciais devem poder ser validadas antes de serem marcadas como `ACTIVE`.

Exemplos:

```text
API Key     → chamada de validação do provider
PostgreSQL  → tentativa de conexão
SSH         → handshake controlado
OAuth       → token introspection
```

Nunca registrar o valor da credencial durante a validação.

---

# 111. Credential Health

Estados adicionais:

```text
HEALTHY
INVALID
EXPIRING
EXPIRED
UNKNOWN
```

Monitorar:

```text
last_validated_at
last_success_at
last_failure_at
expires_at
rotation_due_at
```

---

# 112. Usage Telemetry

Registrar:

```text
credential_id
identity_id
resource
action
timestamp
result
source
task_id
correlation_id
```

Sem registrar o conteúdo sensível.

---

# 113. Correlation ID

Propagar um `correlation_id` por todo o fluxo.

```text
Hermes
  ↓
ForgeHub
  ↓
ForgeVault
  ↓
Third Party
```

Exemplo:

```text
CORR_01JXYZ...
```

Isso permite rastrear uma operação completa.

---

# 114. Audit Imutável

A auditoria deve ser lógica ou fisicamente append-only.

Evoluções possíveis:

```text
hash chaining
WORM storage
SIEM externo
object storage imutável
```

Eventos:

```text
LOGIN
LOGOUT
TOKEN_ISSUED
TOKEN_REVOKED
SECRET_CREATE
SECRET_READ
SECRET_REVEAL
SECRET_ROTATE
SECRET_REVOKE
CREDENTIAL_REQUEST
ACCESS_GRANTED
ACCESS_DENIED
APPROVAL_REQUESTED
APPROVAL_GRANTED
APPROVAL_DENIED
SESSION_CREATED
SESSION_REVOKED
LEASE_CREATED
LEASE_EXPIRED
MCP_CALL
POLICY_CHANGED
```

---

# 115. Token Lifecycle

Tokens de agentes e sistemas também possuem lifecycle.

```text
ISSUE
ACTIVATE
USE
ROTATE
SUSPEND
REVOKE
EXPIRE
```

Armazenar somente:

```text
token_hash
token_prefix
fingerprint
issued_at
expires_at
last_used_at
revoked_at
```

O token completo deve ser exibido apenas uma vez.

---

# 116. Token Introspection

Endpoint:

```http
POST /tokens/introspect
```

Resposta:

```json
{
  "active": true,
  "identity": "agent:athos",
  "type": "AGENT",
  "scopes": [
    "credential.request",
    "access.request"
  ],
  "expires_at": "ISO_DATE"
}
```

---

# 117. Token Fingerprint

Exemplo visual:

```text
fv_agent_ATHOS_****8F2A
```

Permite identificar um token sem expor seu valor.

---

# 118. Session Management

O ForgeVault deve permitir:

```text
listar sessões
consultar sessão
revogar sessão
encerrar todas as sessões de uma identidade
```

Opcionalmente:

```text
device binding
IP binding
network binding
```

---

# 119. Network Policies

Exemplo:

```text
database:production
allow:
  - private-network
  - vpn-darckware

deny:
  - public-internet
```

Aplicável a:

```text
identity
resource
environment
risk_level
```

---

# 120. Rate Limiting por Identidade

Aplicar por:

```text
identity_id
token_id
IP
resource
action
```

Exemplo:

```text
credential.request:
100/min

credential.reveal:
10/min

login:
5/min
```

---

# 121. Anomaly Detection

Detectar:

```text
volume anormal de requests
acesso a recursos incomuns
tentativas repetidas negadas
mudança abrupta de origem
uso fora do contexto esperado
uso após revogação
```

Exemplo:

```text
agent:lara
→ solicita Proxmox root
→ risco elevado
→ alerta
```

---

# 122. Notifications

Eventos notificáveis:

```text
credencial expirando
rotação falhou
credential revoked
acesso negado
atividade anômala
token expirando
backup falhou
health check falhou
```

Canais futuros:

```text
ForgeHub
Email
Webhook
Slack
Teams
```

---

# 123. Webhooks

Exemplos:

```text
credential.created
credential.rotated
credential.revoked
credential.expiring
access.denied
token.revoked
session.revoked
```

---

# 124. Event Outbox

Para garantir entrega confiável de eventos, utilizar padrão Transactional Outbox.

```text
Database transaction
├── state change
└── outbox event
```

Worker publica posteriormente.

Evita inconsistência entre banco e mensageria.

---

# 125. Idempotência

Operações críticas devem suportar:

```text
Idempotency-Key
```

Aplicável a:

```text
create
rotate
revoke
grant
approval
```

---

# 126. Concurrency Control

Impedir operações simultâneas conflitantes.

Exemplo:

```text
rotate CRED_123
rotate CRED_123
```

Apenas uma deve adquirir o lock lógico.

Opções:

```text
PostgreSQL advisory locks
optimistic concurrency
distributed locks
```

---

# 127. Templates de Credenciais

Templates iniciais:

```text
PostgreSQL
MySQL
SQL Server
SSH
OpenAI
Anthropic
OpenRouter
Google
GitHub
Cloudflare
Proxmox
Docker Registry
OAuth2
Generic API
```

---

# 128. Custom Credential Schema

Permitir novos tipos sem alteração estrutural do backend.

Exemplo:

```json
{
  "type": "CUSTOM",
  "schema": {
    "tenant": "string",
    "client_id": "string",
    "client_secret": "secret",
    "region": "string"
  }
}
```

Campos sensíveis devem ser identificados explicitamente.

---

# 129. Secure Attachments

Suportar:

```text
.pem
.key
.p12
.pfx
.json service account
certificates
SSH private keys
```

Arquivos devem ser criptografados antes de armazenamento.

---

# 130. TOTP

Quando necessário:

```text
TOTP_SEED
```

O seed deve ser tratado como secret crítico.

Gerar OTP somente mediante policy apropriada.

---

# 131. Certificate Lifecycle

Suportar:

```text
certificate issue
renewal
expiration
revoke
private key protection
```

---

# 132. Workload Identity e mTLS

Tokens estáticos devem ser apenas uma opção.

Roadmap:

```text
mTLS
Workload Identity
OIDC federation
short-lived certificates
```

Especialmente para:

```text
ForgeHub
ForgeRouter
CI/CD
workers
servers
```

---

# 133. Secret Discovery

Futuro módulo para detectar secrets em:

```text
.env
Git repositories
Docker Compose
CI/CD
scripts
configuration files
```

Ferramentas possíveis:

```text
Gitleaks
TruffleHog
Semgrep
```

O objetivo é migrar secrets dispersos para o ForgeVault.

---

# 134. Importação

Suportar importação controlada de:

```text
JSON
CSV
.env
KeePass export
Bitwarden export
outros vaults
```

Sempre com preview e validação.

---

# 135. Exportação Controlada

Exportação deve:

```text
exigir permissão específica
exigir MFA para operações críticas
ser auditada
permitir criptografia do arquivo exportado
ter expiração quando aplicável
```

---

# 136. Break-Glass

Acesso emergencial deve exigir:

```text
MFA
justificativa
TTL curto
auditoria
notificação
```

Opcionalmente:

```text
dual approval
```

---

# 137. Quorum e Multi-Approval

Para operações L4:

```text
2 de 2
2 de 3
```

Exemplos:

```text
revoke master credential
reveal root production
rotate critical database admin
break-glass
```

---

# 138. Policy Simulation

Permitir testar:

```text
"agent:athos conseguiria acessar database:postgres-forgehub?"
```

Sem liberar a credencial.

Endpoint conceitual:

```http
POST /policies/simulate
```

---

# 139. Policy Inheritance

Hierarquia:

```text
Tenant
  ↓
Workspace
  ↓
Project
  ↓
Environment
  ↓
Resource
```

Policies específicas podem restringir policies superiores.

---

# 140. Environment Segregation

Ambientes:

```text
development
staging
production
shared
```

Regra:

> Credenciais de produção não devem ser disponibilizadas automaticamente em development ou staging.

---

# 141. Secure Bootstrap

O ForgeVault precisa de fluxo seguro de bootstrap.

Etapas:

```text
1. gerar Master Key
2. inicializar banco
3. criar primeiro Tenant
4. criar primeiro administrador
5. registrar recovery material
6. habilitar MFA
7. invalidar bootstrap token
```

Nunca depender de um secret que já precise estar dentro do ForgeVault para inicializar o próprio ForgeVault.

---

# 142. Master Key Lifecycle

A Master Key / KEK também deve possuir lifecycle.

```text
CREATE
ACTIVATE
ROTATE
RETIRE
RECOVER
DESTROY
```

Hierarquia:

```text
Master Key / KEK
        │
        ▼
Encrypted DEK
        │
        ▼
Secret Ciphertext
```

---

# 143. KMS Abstraction

Implementar interface abstrata:

```text
IKeyManagementProvider
```

Providers futuros:

```text
Local Key Provider
HashiCorp Vault Transit
AWS KMS
Azure Key Vault
Google Cloud KMS
HSM
```

---

# 144. Backup e PITR

PostgreSQL:

```text
base backup
WAL archiving
Point-In-Time Recovery
```

Backup deve incluir:

```text
database
encrypted credentials
configuração
audit metadata
```

Master Key deve ter backup separado.

---

# 145. Restore Tests

Não basta gerar backup.

Executar periodicamente:

```text
restore
integrity check
credential decrypt validation
audit validation
```

Sem expor secrets.

---

# 146. Resiliência e Disponibilidade

O ForgeVault se torna infraestrutura crítica.

Evitar:

```text
ForgeVault caiu
      ↓
ForgeHub parou
ForgeRouter parou
Hermes parou
Agentes pararam
Sites pararam
```

Estratégia:

```text
credential lease/cache seguro
TTL curto
renovação periódica
```

Exemplo:

```text
Sistema
   │
   ▼
ForgeVault
   │
   ▼
credencial + lease
TTL = 15 minutos
```

Se o ForgeVault ficar indisponível temporariamente, leases válidos podem continuar funcionando até expirar.

---

# 147. HA

Roadmap de produção:

```text
Load Balancer
   │
   ├── ForgeVault API 1
   └── ForgeVault API 2

PostgreSQL Primary
   │
   └── Replica

Redis HA opcional
```

Workers devem ser stateless sempre que possível.

---

# 148. Health Endpoints

```http
GET /health/live
GET /health/ready
GET /health/dependencies
```

Verificar:

```text
API
PostgreSQL
Redis
KMS
Event Bus
Worker
```

Nunca incluir secrets nas respostas de health.

---

# 149. Maintenance Mode

Permitir:

```text
READ_ONLY
NO_ROTATION
NO_ADMIN_CHANGES
FULL_MAINTENANCE
```

Uso durante:

```text
migration
database maintenance
key rotation
incident response
```

---

# 150. Soft Delete e Purge

Distinguir:

```text
DELETE
ARCHIVE
PURGE
```

`PURGE` deve ser operação administrativa especial.

Auditoria deve permanecer conforme política de retenção.

---

# 151. Data Retention

Definir separadamente:

```text
audit logs
credential versions
events
sessions
leases
notifications
deleted resources
```

---

# 152. Compliance Reports

Relatórios:

```text
quem acessou
o que acessou
quando
por qual identidade
por qual tarefa
de qual origem
qual policy autorizou
qual credencial foi utilizada
qual resultado
```

Sem revelar valor do secret.

---

# 153. SDK Oficial

SDKs prioritários:

```text
C#
TypeScript
Python
```

Funções:

```text
authenticate
requestCredential
requestAccess
getMetadata
releaseCredential
renewLease
revokeSession
```

---

# 154. CLI Oficial

Nome:

```text
fv
```

Comandos:

```bash
fv login
fv whoami

fv credential list
fv credential metadata
fv credential request
fv credential rotate
fv credential revoke

fv access request
fv access release

fv session list
fv session revoke

fv token rotate
fv token revoke

fv audit search
```

---

# 155. API Versioning

Base:

```text
/api/v1
```

MCP também deve possuir versionamento de schema.

Evitar breaking changes sem estratégia de compatibilidade.

---

# 156. Feature Flags

Permitir habilitação progressiva de:

```text
MCP
Broker
Session Broker
Dynamic Credentials
Auto Rotation
Anomaly Detection
SSO
RLS
Multi-Approval
```

---

# 157. PostgreSQL Row-Level Security

Roadmap para reforçar isolamento multi-tenant.

Exemplo conceitual:

```sql
ALTER TABLE credentials ENABLE ROW LEVEL SECURITY;
```

Policies baseadas em:

```text
tenant_id
workspace_id
project_id
```

---

# 158. Auditoria Particionada

Quando necessário:

```text
audit_logs_2026_09
audit_logs_2026_10
audit_logs_2026_11
```

Benefícios:

```text
performance
retenção
manutenção
arquivamento
```

---

# 159. Critérios Adicionais de Aceite

Antes de produção:

- cada agente possui identidade e token próprios;
- cada sistema pode possuir identidade própria;
- credenciais de bancos e serviços externos são recursos independentes;
- tokens são armazenados apenas como hash;
- credenciais são armazenadas criptografadas;
- retorno JSON segue contrato padronizado;
- revoke de acesso não revoga necessariamente a credencial;
- revoke de credencial suporta cascade;
- rotate preserva continuidade operacional;
- toda chamada MCP é auditada;
- correlation ID é propagado;
- dependency mapping funciona;
- impact analysis funciona;
- backup e restore foram testados;
- expiração e rotação são monitoradas;
- ForgeVault suporta indisponibilidade curta via leases válidos;
- nenhum secret aparece em logs;
- nenhum token administrativo universal é compartilhado.

---

# 160. Princípios Arquiteturais Consolidados

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

---

# 161. Arquitetura Final Recomendada

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

Consumidores finais:

```text
APIs
Bancos
Servidores
Sites
Cloud Providers
LLM Providers
GitHub
Cloudflare
Proxmox
Docker
Aplicações de clientes
```

---

# 162. Definição Final do Produto

> **ForgeVault é o Security Plane da Darckware: uma plataforma centralizada de identidade, secrets, credenciais, políticas, sessões, leases, revogação, rotação, auditoria e intermediação de acesso para humanos, agentes, sistemas e máquinas.**

O ForgeVault deve permitir que cada componente do ecossistema opere com identidade própria e privilégio mínimo, mantendo credenciais centralizadas, criptografadas, versionadas, rotacionáveis e auditáveis.
