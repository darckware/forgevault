# ForgeVault — Product Requirements Document (PRD)

> **Status documental:** baseline derivada de `docs/ForgeVault.md` (fonte original, não editada). Para a arquitetura-alvo consolidada, ver `docs/architecture/TARGET_ARCHITECTURE.md`; para a ordem de implementação, ver `docs/architecture/IMPLEMENTATION_READINESS.md`; para saber o que já existe de fato, confirme o código (ainda inexistente na data desta baseline).

## 1. Product Vision

O **ForgeVault** é o cofre central de credenciais e secrets do ecossistema Darckware: uma plataforma centralizada de identidade operacional, secrets, credenciais, autorização, intermediação de acesso e auditoria para humanos, agentes de IA, sistemas e máquinas (`ForgeVault.md` §1, §71).

O objetivo é eliminar a dispersão de credenciais em arquivos `.env`, planilhas, mensagens, documentos, scripts, repositórios Git, configurações locais e bancos sem criptografia adequada (§1-2).

O produto deve se comportar como o **Security Plane** transversal consumido por todo o ecossistema — hoje representado pelos projetos irmãos ForgeHub (orquestração) e ForgeRouter (roteamento de LLMs), além de Hermes, agentes, n8n, CI/CD e aplicações internas/de clientes (§62, §73).

## 2. Product Objective

Permitir que humanos, agentes, sistemas e máquinas usem credenciais de forma segura, contextual, temporária e auditável, eliminando a necessidade de qualquer consumidor conhecer ou persistir secrets permanentemente (§70, §71).

Regra central:
> Nenhum secret deve existir sem dono, ambiente, criptografia, controle de acesso e trilha de auditoria.

## 3. Target Scope

A primeira release é o **MVP — Fase 1** (`ForgeVault.md` §5, §62-63).

Incluído na Fase 1:
- autenticação com MFA;
- organizações, usuários, projetos, ambientes;
- cadastro e versionamento de secrets;
- criptografia (envelope encryption);
- controle de acesso (RBAC);
- auditoria de toda leitura/escrita;
- API REST;
- interface web;
- backup e restore;
- logs e observabilidade básica.

Fora do escopo da Fase 1 (roadmap §63, detalhado em `docs/architecture/IMPLEMENTATION_READINESS.md`):
- **Fase 2** — integração real com ForgeHub/ForgeRouter, Service Accounts, CLI, ABAC completo, rotação automatizada;
- **Fase 3** — dynamic secrets, leases/TTL, approvals, break-glass, notificações;
- **Fase 4** — KMS/HSM real, SSO, LDAP/Active Directory, multi-tenant avançado.

Nenhum destes itens deve ser implementado antecipadamente sem uma decisão registrada — ver `docs/architecture/IMPLEMENTATION_READINESS.md`.

## 4. Domain Model

Modelo adotado para a Fase 1 (decisão registrada em `docs/architecture/IMPLEMENTATION_READINESS.md`, com base em `ForgeVault.md` §11 — não o modelo multi-tenant completo de §81/§107, que é Fase 4):

```text
Organization
 └── Project
      └── Environment
           └── Secret
                └── SecretVersion

AuditLog (transversal, referencia qualquer entidade acima)
```

Evolução prevista para Fase 4 (§81, §107), sem impacto retroativo no modelo de `Secret`/`SecretVersion`:

```text
Tenant
└── Workspace
    └── Project
        └── Environment
            └── Resource
                └── Credential
```

## 5. Key Concepts

### 5.1 Organization
Entidade raiz de agrupamento (§11). Evolui para `Tenant`/`Workspace` na Fase 4.

### 5.2 Project
Iniciativa ou produto dentro de uma Organization (ex.: ForgeRouter, ForgeHub) (§8).

### 5.3 Environment
Segregação por ambiente (`development`, `staging`, `production`, `shared`) — credenciais de produção nunca são compartilhadas automaticamente com outros ambientes (§9, §140).

### 5.4 Secret / Credential
Qualquer informação sensível gerenciada pelo cofre: senha, API key, access/refresh token, token de LLM, credencial SSH, credencial de banco, service account, certificado, chave privada, webhook secret, token OAuth (§1, §10, §78).

### 5.5 SecretVersion
Cada alteração de valor gera uma nova versão imutável; nunca há sobrescrita silenciosa (§22, §106).

### 5.6 Identity
Toda entidade que executa ações possui identidade própria: `HumanIdentity`, `AgentIdentity`, `ServiceIdentity`, `MachineIdentity`, `McpClientIdentity` (§74).

### 5.7 AccessGrant / CredentialBinding
Vínculo explícito entre uma identidade e uma credencial, com permissão, escopo e expiração (§80, §98).

### 5.8 AuditLog
Registro append-only de toda operação relevante, nunca contendo o valor do secret (§20-21).

## 6. Primary User Journeys

### 6.1 Administrador cadastra e gerencia um secret
1. Administrador autentica com MFA.
2. Cria/seleciona Organization → Project → Environment.
3. Cadastra um Secret (tipo, provider, valor).
4. O valor é criptografado antes de persistir.
5. Consulta o secret (valor mascarado por padrão).
6. Revoga ou rotaciona quando necessário.
7. Toda ação gera um AuditLog.

### 6.2 Agente de IA solicita acesso a um secret
1. Agente (ex.: `agent:athos`) autentica no ForgeHub com identidade própria.
2. ForgeHub encaminha a solicitação ao ForgeVault com contexto (projeto, ambiente, ação).
3. ForgeVault valida identidade, projeto, ambiente e política.
4. Acesso é concedido (idealmente via BROKER/SESSION/LEASE/INJECT — REVEAL é exceção).
5. A operação é auditada.

### 6.3 ForgeRouter recupera credencial de provider
1. ForgeRouter precisa de uma API Key (OpenAI, Anthropic, etc.).
2. Solicita ao ForgeVault em vez de manter a chave persistida localmente.
3. ForgeVault autentica a identidade `service:forgerouter`, autoriza e retorna a credencial.
4. A operação é auditada com correlation ID propagado.

## 7. Delivery Principles

(`ForgeVault.md` §160, resumidos)

- Cada agente e cada sistema autônomo possui identidade e token próprios.
- Credenciais pertencem aos recursos, não aos agentes que as consomem.
- REVEAL (retorno do valor bruto) deve ser exceção, não o modo padrão.
- Toda operação crítica deve ser auditável; toda revogação deve ser imediata, idempotente e rastreável.
- A Master Key nunca reside junto dos dados criptografados.
- Secrets nunca são registrados em logs.
- Aprovação humana é exceção, não regra.

## 8. Definition of Done (MVP)

O MVP é considerado funcional quando (§62, verbatim):

- usuário autentica com MFA;
- consegue criar organization, projeto e ambiente;
- consegue criar secret;
- o valor fica criptografado;
- usuário autorizado consegue recuperar o valor;
- usuário não autorizado recebe 403;
- toda leitura gera auditoria;
- versões são mantidas;
- secrets podem expirar;
- ForgeHub consegue autenticar;
- ForgeRouter consegue recuperar credencial;
- backup pode ser restaurado.

## 9. Strategic Outcome

O ForgeVault deve se tornar a camada central de custódia, autorização, distribuição e auditoria de credenciais do ecossistema Darckware — permitindo que aplicações e agentes nunca precisem conhecer ou persistir secrets permanentemente, usando o ForgeVault como broker seguro entre identidades, aplicações e provedores externos (§70).
