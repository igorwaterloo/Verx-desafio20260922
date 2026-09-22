# ADR-0014: Lançamentos imutáveis, com correção por estorno

- **Status:** Aceita
- **Data:** 2026-09-22
- **Decisores:** Igor Waterloo
- **Relacionadas:** [ADR-0005](0005-outbox-e-consumidor-idempotente.md), [modelo de domínio](../dominio.md)

## Contexto e problema

O comerciante pode errar um lançamento (valor, tipo ou data). É preciso decidir **como corrigir** sem comprometer a integridade do saldo consolidado nem a rastreabilidade financeira.

Editar ou excluir um lançamento traz dois problemas:
1. **Auditoria:** o histórico do que foi registrado se perde. Em contexto financeiro, alterações silenciosas são inaceitáveis.
2. **Consolidação:** um update exigiria eventos de "delta" (valor antigo × novo, data antiga × nova), com risco de inconsistência e de ordem de eventos.

## Requisitos da decisão

- Trilha de auditoria completa.
- Consolidação simples e comutativa (RC-02), sem depender da ordem dos eventos.
- Regras claras para o usuário.

## Opções consideradas

1. Update e delete livres
2. Soft delete + novo lançamento
3. **Lançamentos imutáveis + estorno** (padrão contábil)

## Decisão

**Opção escolhida:** **lançamentos são imutáveis**. A correção é feita por **estorno**: um novo lançamento com o **tipo inverso**, **mesmo valor** e **mesma data de competência** do original (RN-03..RN-06). Se necessário, o usuário registra em seguida o lançamento correto.

- O estorno referencia o original (`LancamentoOriginalId`), e o original é marcado como `Estornado`.
- O Consolidado recebe um `LancamentoRegistrado` comum com o tipo inverso. Não há evento especial nem lógica de "desfazer".
- A única alteração de estado permitida é a marcação `Estornado` no original, feita na mesma transação do estorno.

**Data do estorno:** usa a data de competência do original, para corrigir o saldo **daquele dia**. Com um futuro **fechamento de caixa**, estornos de dias fechados passarão a usar a data corrente ([evolução futura](../evolucao-futura.md)).

## Consequências

### Positivas
- Histórico completo e auditável: nada é apagado.
- O Consolidado só soma (comutativo, idempotente), o que mantém o modelo de eventos simples.
- Alinhado à prática contábil (o usuário financeiro reconhece o conceito).

### Negativas / trade-offs aceitos
- Corrigir exige duas operações (estornar + lançar o correto).
- O volume de registros cresce com as correções.

### Mitigações
- Na UI, a ação "Corrigir" pode executar estorno + novo lançamento em sequência.
- O volume é irrelevante para a escala do problema; particionamento/arquivamento por data fica como evolução.

## Referências
- Martin Fowler — *Accounting Patterns* (Reversal Adjustment)
- [Fluxo 4 — Estorno](../architecture/fluxos.md#4-estorno)
