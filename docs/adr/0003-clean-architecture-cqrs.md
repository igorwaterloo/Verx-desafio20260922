# ADR-0003: Clean Architecture + CQRS em cada serviço

- **Status:** Aceita
- **Data:** 2026-09-22
- **Decisores:** Igor Waterloo
- **Relacionadas:** [ADR-0002](0002-microsservicos-por-bounded-context.md), [ADR-0013](0013-bibliotecas-e-licencas.md)

## Contexto e problema

Cada serviço precisa de uma organização interna que:
- proteja as **regras de negócio** (RN-01..RN-08, RC-01..RC-04) de detalhes de framework;
- permita **TDD** com testes unitários rápidos, sem banco ou broker;
- separe claramente **escrita** (regras, transações, eventos) de **leitura** (consultas otimizadas, cache).

## Requisitos da decisão

- Testabilidade do domínio sem infraestrutura.
- Substituir a infraestrutura (banco, broker, cache) sem tocar nas regras.
- Leituras otimizadas independentes do modelo de escrita.
- Boas práticas pedidas no desafio: SOLID, design patterns, segregação de responsabilidades.

## Opções consideradas

1. **Camadas tradicionais** (Controller → Service → Repository)
2. **Clean Architecture** (Domain / Application / Infrastructure / Api)
3. **Vertical Slice Architecture** (uma pasta por feature, sem camadas)
4. Qualquer uma das anteriores **+ CQRS**, com ou sem event sourcing

## Decisão

**Opção escolhida:** **Clean Architecture** com **CQRS** (sem event sourcing).

**Estrutura por serviço:**

| Projeto | Conteúdo | Depende de |
|---|---|---|
| `*.Domain` | Agregados, value objects, eventos de domínio, regras | SharedKernel |
| `*.Application` | Commands, queries, handlers, validators, **portas** (interfaces de repositório, cache, publisher) | Domain, Contracts |
| `*.Infrastructure` | EF Core, MassTransit, Redis: **adaptadores** das portas | Application |
| `*.Api` / `*.Worker` | Endpoints/consumers, composition root (DI) | Todas |

**CQRS:**
- **Commands** passam pelo agregado (invariantes), persistem via unidade de trabalho e geram eventos.
- **Queries** não passam pelo domínio: consultas `AsNoTracking` projetadas diretamente em DTOs.
- No **Consolidado**, a separação é também **física**: o Worker escreve e a Api lê ([ADR-0010](0010-separacao-consolidado-api-worker.md)).
- **Sem event sourcing:** o estado atual em tabelas relacionais atende o requisito. Event sourcing traria complexidade (snapshots, versionamento de eventos, reconstrução) sem requisito que a justifique. O histórico imutável de lançamentos ([ADR-0014](0014-lancamentos-imutaveis-com-estorno.md)) já dá auditabilidade.

**Pipeline de handlers:** um dispatcher próprio aplica **decorators** (validação com FluentValidation, logging, métricas), resolvendo preocupações transversais sem poluir os handlers ([ADR-0013](0013-bibliotecas-e-licencas.md)).

**Erros de negócio:** retornados como `Result<T>` / `Error` (sem exceções para fluxo esperado). Exceções ficam para falhas inesperadas.

## Consequências

### Positivas
- Domínio 100% testável com testes unitários (base do TDD).
- **Dependency Inversion** (o "D" do SOLID): Application define portas e Infrastructure implementa.
- Leituras simples e rápidas, sem carregar agregados.
- Regras de dependência **verificáveis automaticamente** (NetArchTest).

### Negativas / trade-offs aceitos
- Mais projetos e mais cerimônia (DTOs, mapeamentos) do que em camadas simples.
- Para um domínio pequeno, parte da estrutura pode parecer excessiva.

### Mitigações
- Mapeamentos manuais e explícitos (sem AutoMapper), com poucos DTOs.
- Organização **por feature dentro da Application** (`Lancamentos/Registrar/...`), que traz o benefício de coesão da vertical slice.
- Testes de arquitetura impedem a erosão das camadas.

## Comparativo das opções

| Critério | Camadas | **Clean + CQRS** | Vertical Slice |
|---|---|---|---|
| Isolamento do domínio | ⚠️ | ✅ | ⚠️ depende da disciplina |
| Testabilidade unitária | ⚠️ | ✅ | ✅ |
| Leitura otimizada | ❌ | ✅ | ✅ |
| Cerimônia | Baixa | Média | Baixa |
| Regras verificáveis | ⚠️ | ✅ | ❌ |
| Familiaridade de mercado (.NET) | Alta | Alta | Média |

## Referências
- Robert C. Martin — *Clean Architecture*
- Greg Young — *CQRS Documents*
- [C4 — Componentes](../architecture/c4-3-componentes.md)
