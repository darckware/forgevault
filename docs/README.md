# ForgeVault — Mapa e Governança da Documentação

> Padrão adotado a partir do `docs/README.md` do ForgeHub (`/root/project/forgehub/docs/README.md`), projeto irmão no ecossistema Darckware, para evitar que uma LLM combine uma visão futura com um estado implementado, trate uma proposta como já existente, ou escolha arbitrariamente entre documentos divergentes.

## Estrutura da pasta `docs/`

```
docs/
├── README.md                      ← este arquivo (índice)
├── ForgeVault.md                  ← fonte histórica original (baseline, não editada)
├── specs/                         ← visão e especificação original de produto (baseline)
│   ├── PRD.md
│   └── SPEC.md
├── architecture/                  ← arquitetura-alvo canônica e ordem de implementação
│   ├── TARGET_ARCHITECTURE.md
│   ├── IMPLEMENTATION_READINESS.md
│   └── INTEGRATION_CONTRACT_MVP.md
├── modules/                       ← specs de módulos implementáveis, em ordem de dependência
│   ├── README.md
│   ├── 01_FOUNDATION_AND_TENANCY.md
│   ├── 02_IDENTITY_AND_AUTHENTICATION.md
│   ├── 03_SECRETS_AND_ENCRYPTION.md
│   ├── 04_AUTHORIZATION_AND_POLICY.md
│   ├── 05_ACCESS_BROKER_AND_API.md
│   ├── 06_AUDIT_AND_GOVERNANCE.md
│   ├── 07_LIFECYCLE_ROTATION_REVOCATION.md
│   ├── 08_CLI_SDK.md
│   ├── 09_FORGEHUB_FORGEROUTER_MCP_INTEGRATION.md
│   └── 10_RESILIENCE_HA_OPERATIONS.md
├── reference/                     ← estado ATUALMENTE implementado (vazio até o 1º módulo virar código)
│   └── README.md
├── templates/                     ← template canônico de module spec
│   └── MODULE_SPEC_TEMPLATE.md
└── assets/
    └── forgevault-icon.svg
```

## Finalidade

Este arquivo define como humanos e agentes devem interpretar a documentação do ForgeVault. Ele existe para impedir que uma LLM implemente algo que só está descrito como intenção, ou que decida sozinha entre duas fontes divergentes sem registrar o conflito.

## Hierarquia de autoridade

Quando houver conflito, use esta ordem:

1. **Código, migrations e testes executados** — comportamento implementado hoje. (A partir do M7, a Onda 1/MVP tem implementação real em `src/`/`tests/`; ver `README.md` da raiz do repositório para o estado corrente por marco. `docs/reference/*` ainda não foi escrito a partir desse código — ver item 2.)
2. **`reference/DATA_MODEL.md`, `reference/BUSINESS_RULES.md` e `reference/TECHNOLOGY.md`** (quando existirem) — descrição do estado implementado, desde que confirmada pelo código.
3. **`architecture/TARGET_ARCHITECTURE.md`** — direção canônica do produto e arquitetura-alvo.
4. **`architecture/IMPLEMENTATION_READINESS.md`** — ordem de implementação e fronteira autorizada da onda atual.
5. **`modules/*.md`** — contratos de implementação por fatia; só autorizam código quando `status` sai de `draft`.
6. **`specs/PRD.md`, `specs/SPEC.md` e `ForgeVault.md`** — baseline original; não substituem decisões posteriores, mas nunca são apagados.

Regra: documento de direção pode exigir uma mudança, mas não prova que ela já existe. Documento de estado atual pode descrever uma limitação, mas não revoga a arquitetura-alvo.

## Classificação dos documentos

| Documento | Papel | Estado |
|---|---|---|
| `ForgeVault.md` | especificação original completa (162 seções) | histórico, válido como origem de todo o resto |
| `specs/PRD.md` | visão e requisitos, derivados de `ForgeVault.md` | baseline |
| `specs/SPEC.md` | especificação técnica, derivada de `ForgeVault.md` | baseline |
| `architecture/TARGET_ARCHITECTURE.md` | arquitetura-alvo consolidada | canônico para direção |
| `architecture/IMPLEMENTATION_READINESS.md` | ordem de implementação, marcos de engenharia, fronteira da Onda 1 | canônico para planejamento técnico |
| `architecture/INTEGRATION_CONTRACT_MVP.md` | contrato de integração REST realmente implementado (M7) para ForgeHub/ForgeRouter | estado atual da integração — não confundir com a visão MCP completa do módulo 09 |
| `modules/README.md` e `modules/01_...` a `10_...` | contratos implementáveis por fatia | specs-alvo; nascem `status: draft`, exigem aprovação antes do código |
| `reference/*` | dicionário do estado implementado | ainda não existe (ver `reference/README.md`) |
| `templates/MODULE_SPEC_TEMPLATE.md` | contrato mínimo antes da implementação de um módulo | template canônico |

## Leitura obrigatória por tipo de trabalho

### Para analisar ou planejar o produto

1. este arquivo;
2. `architecture/TARGET_ARCHITECTURE.md`;
3. `architecture/IMPLEMENTATION_READINESS.md`;
4. o(s) módulo(s) afetado(s) em `modules/`.

### Para implementar um módulo

1. identificar o módulo em `architecture/IMPLEMENTATION_READINESS.md` e confirmar que suas dependências já estão implementadas;
2. confirmar que o Definition Gate (seção 15) do módulo está completo e `status` não é mais `draft`;
3. ler o módulo por completo, inclusive `open_blocking_questions`;
4. seguir a ordem de entrega descrita na seção 14 do módulo;
5. não implementar campo, endpoint ou estado ainda não decidido no módulo sem registrar a decisão nele.

### Para operar o sistema (quando houver código)

1. `reference/DATA_MODEL.md` e `reference/BUSINESS_RULES.md` (quando existirem);
2. código/rotas atuais;
3. nunca presumir que um item descrito em `modules/` ou `architecture/` já está disponível em produção.

## Definition Gate de documentação

Um módulo só está pronto para implementação quando possui, no mínimo (ver seção 15 de cada spec de módulo):

- objetivo, atores e limites de responsabilidade;
- entidades, atributos, relações, constraints e estratégia de migração;
- estados e transições, incluindo comandos inválidos;
- regras e policies aplicáveis;
- contratos de API, erros, idempotência e autorização;
- telas, ações, estados vazios/loading/error e acessibilidade (quando aplicável);
- eventos e registros de auditoria, sem nunca incluir valor de secret;
- integrações e comportamento em falha;
- critérios de aceite e testes de contrato/domínio/segurança;
- observabilidade, rollout e rollback;
- indicação explícita do que permanece fora do escopo.

## Regras contra alucinação documental

1. Não inferir que uma entidade ou endpoint proposto em `modules/`/`architecture/` já existe em código.
2. Não inventar UUID, aprovação, evidência, policy, estado de gate ou resultado de teste.
3. Não escolher silenciosamente entre nomes/estados divergentes entre `ForgeVault.md` e os documentos derivados; registrar o conflito na spec do módulo afetado (`open_blocking_questions`).
4. Nenhum documento — código incluído — pode registrar o valor de um secret, senha ou token bruto (regra transversal de todos os módulos, ver `ForgeVault.md` §21).
5. Toda decisão nova precisa de registro durável (no `decisions:` do módulo afetado), não apenas de memória de conversa.
6. Ausência de contexto obrigatório em uma spec produz `open_blocking_questions`, não preenchimento criativo.

## Controle de revisão

Cada alteração arquitetural deve atualizar, conforme aplicável:

1. `architecture/TARGET_ARCHITECTURE.md`;
2. `architecture/IMPLEMENTATION_READINESS.md`;
3. o(s) `modules/*.md` afetado(s);
4. `reference/*` quando a mudança já estiver implementada;
5. este índice, se a estrutura de pastas mudar.

Não se apaga uma decisão já utilizada por um módulo aprovado ou por código em produção — ela é superseded por uma revisão posterior, preservando motivo, autor, data e elementos impactados (mesma convenção do ForgeHub).
