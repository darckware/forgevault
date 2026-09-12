# Module Specification — CLI and SDK

## 1. Controle

```yaml
spec_id: MOD-08-CLI-SDK
revision: 1
status: draft
owner: unassigned
approvers: []
target_release: Fase 2
architecture_refs:
  - docs/architecture/TARGET_ARCHITECTURE.md
decisions: []
open_blocking_questions: []
```

## 2. Objetivo e limite

- **Problema resolvido:** desenvolvedores e pipelines de CI/CD não têm hoje uma forma ergonômica de consumir a API do ForgeVault sem chamar HTTP cru.
- **Resultado esperado:** CLI `fv` cobre login, CRUD de secrets, rotate/revoke, gestão de sessão/token; SDKs oficiais (C#, TypeScript, Python) cobrem as mesmas operações programaticamente.
- **Atores:** desenvolvedores, pipelines de CI/CD, scripts operacionais.
- **Dentro do escopo:** CLI `fv` (§154), SDKs (§153), injeção de secrets em processo (`fv exec --`, §34), exportação temporária (`fv export`, §35).
- **Fora do escopo:** qualquer nova capacidade de servidor — este módulo é puramente cliente sobre a API já existente dos módulos `01`-`07`.

## 3. Casos de uso e processos

| ID | Ator | Gatilho | Fluxo principal | Resultado |
|---|---|---|---|---|
| UC-01 | Desenvolvedor | Login local | `fv login` | sessão local autenticada, token armazenado com segurança (keychain do SO quando disponível) |
| UC-02 | Pipeline CI/CD | Precisa de credenciais em runtime | `fv exec -- <comando>` | secrets injetados apenas no processo filho, nunca gravados em disco (§34) |
| UC-03 | Operador | Debug local | `fv export --format env --project X --environment Y` | arquivo `.env` temporário gerado, com aviso de que deve ser evitado/desabilitado em produção (§35) |

## 4. Modelo de domínio e dados

Nenhuma entidade de servidor nova. CLI/SDK consomem os contratos já definidos nos módulos `02` (auth), `03` (secrets), `05` (value), `07` (rotate/revoke).

### Invariantes

1. `fv export` nunca deve ser o caminho padrão recomendado em produção — a documentação e a própria CLI devem alertar isso a cada uso.
2. `fv exec --` nunca grava o valor do secret em disco; injeção é feita apenas nas variáveis de ambiente do processo filho.

## 5. Máquinas de estados

Não aplicável — módulo cliente, sem estado de servidor próprio além de uma sessão local de CLI (token armazenado localmente, com expiração herdada do módulo `02`).

## 6. Policies e permissões

Idênticas às do recurso subjacente (módulos `02`-`07`) — a CLI/SDK não introduz nenhum novo nível de permissão, apenas consome os existentes.

## 7. Contratos de API

Este módulo não introduz endpoints novos — é inteiramente cliente. Comandos de CLI mapeados (§154):

```text
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

## 8. Eventos e auditoria

Nenhum evento novo — toda ação da CLI/SDK gera os eventos já definidos pelo endpoint de servidor correspondente.

## 9. Interface

Não aplicável (interface de linha de comando, não UI web). Requisitos de UX de CLI: mensagens de erro claras, `--help` completo, saída em JSON opcional para scripting.

## 10. Integrações e falhas

| Integração | Timeout | Retry | Falha visível |
|---|---|---|---|
| API REST do ForgeVault | configurável | retry com backoff em erros de rede (nunca em 401/403) | código de saída não-zero + mensagem clara |

## 11. Observabilidade

- CLI deve reportar erros de forma acionável; não há telemetria de uso da CLI planejada para o MVP deste módulo.

## 12. Segurança e privacidade

- Token de sessão local armazenado com a proteção do SO disponível (keychain/credential manager); nunca em texto puro em arquivo de configuração legível por outros usuários.

## 13. Critérios de aceite

| ID | Given | When | Then | Nível |
|---|---|---|---|---|
| AC-01 | usuário autenticado via `fv login` | executa `fv credential list` | lista de secrets do escopo atual é exibida (metadados, nunca valor) | E2E |
| AC-02 | `fv exec -- env` | executado com um secret vinculado | variável aparece no ambiente do processo filho e em nenhum outro lugar | E2E |

## 14. Plano de entrega

- ordem de implementação: depois da API estável dos módulos `05`/`06` (ver `IMPLEMENTATION_READINESS.md`).

## 15. Definition Gate

- [ ] limites e casos de uso aprovados
- [ ] entidades/constraints aprovadas
- [ ] estados/comandos aprovados
- [ ] Policies/permissões aprovadas
- [ ] API/eventos aprovados
- [ ] UI e estados aprovados
- [ ] migration/rollback aprovados
- [ ] testes e evidências definidos
- [ ] observabilidade definida
- [ ] nenhuma questão bloqueante aberta
