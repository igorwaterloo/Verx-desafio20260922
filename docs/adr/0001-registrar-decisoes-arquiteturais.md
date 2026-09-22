# ADR-0001: Registrar decisões arquiteturais com ADR

- **Status:** Aceita
- **Data:** 2026-09-22
- **Decisores:** Igor Waterloo (Arquiteto de Software)
- **Relacionadas:** todas as demais ADRs

## Contexto e problema

O desafio avalia a **capacidade de tomada de decisão** do arquiteto, não apenas o código. Decisões arquiteturais costumam se perder em conversas, e-mails ou na memória de quem as tomou. Com isso, novos membros não entendem **por que** o sistema é como é, e decisões são revertidas sem avaliar as consequências.

## Requisitos da decisão

- Documentação versionada **junto ao código** (requisito do desafio: "todas as documentações de projeto devem estar no repositório").
- Formato leve, legível no GitHub, fácil de revisar em pull request.
- Registrar **alternativas descartadas** e **trade-offs**, não apenas a escolha.

## Opções consideradas

1. **ADRs em Markdown no repositório (formato MADR)**
2. Wiki externa (Confluence / GitHub Wiki)
3. Documento único de arquitetura (SAD)
4. Não documentar formalmente

## Decisão

**Opção escolhida:** ADRs em Markdown no formato **MADR**, em `docs/adr/`, numeradas sequencialmente. Cada decisão significativa (difícil de reverter, com impacto transversal ou com trade-off relevante) ganha um ADR.

**Regras:**
- Um ADR aceito é **imutável**. Mudar a decisão exige um novo ADR que **substitui** o anterior (o antigo muda o status para "Substituída por ADR-XXXX").
- ADRs são revisados no mesmo pull request que implementa a decisão.
- Template: [`template.md`](template.md).

## Consequências

### Positivas
- O histórico das decisões evolui junto com o código (git blame, PRs).
- Facilita a entrada de novos membros e auditorias técnicas.
- Explicita trade-offs, o que reduz a revisão de decisões já tomadas.

### Negativas / trade-offs aceitos
- Esforço de escrita a cada decisão relevante.
- ADRs podem ficar desatualizados se o processo não for seguido.

### Mitigações
- Template curto; checklist de PR pergunta: "esta mudança exige ADR?".

## Referências
- Michael Nygard — *Documenting Architecture Decisions* (2011)
- [MADR — Markdown Architectural Decision Records](https://adr.github.io/madr/)
