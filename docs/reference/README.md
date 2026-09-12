# ForgeVault — Reference (estado atualmente implementado)

Esta pasta ficará vazia (exceto este arquivo) até que o primeiro módulo de `docs/modules/` ganhe migrations e código real.

Quando o módulo `01_FOUNDATION_AND_TENANCY.md` (ou o primeiro módulo implementado, o que vier antes) tiver código executando, esta pasta passa a conter, seguindo o mesmo padrão do ForgeHub (`/root/project/forgehub/docs/reference/`):

- `DATA_MODEL.md` — dicionário de dados e diagrama entidade-relacionamento do que **está de fato implementado**, com ground truth = código (`src/ForgeVault.Domain`, migrations do EF Core).
- `BUSINESS_RULES.md` — regras de negócio efetivamente aplicadas, com referência a onde são impostas no código (`arquivo:linha`), incluindo lacunas conhecidas.
- `TECHNOLOGY.md` — stack realmente utilizada (versões, bibliotecas, organização de código).

Regra herdada do `docs/README.md` do ForgeHub, aplicada aqui: documentos desta pasta descrevem o estado atual e podem revelar uma limitação real, mas nunca revogam a arquitetura-alvo definida em `docs/architecture/TARGET_ARCHITECTURE.md`. Documentos de arquitetura/módulo podem exigir uma mudança, mas não provam que ela já existe — só o código e estes documentos de referência (quando escritos a partir dele) provam.

Não escreva conteúdo nesta pasta antecipadamente descrevendo tabelas ou endpoints que ainda não existem — isso é exatamente o tipo de alucinação documental que este padrão existe para evitar.
