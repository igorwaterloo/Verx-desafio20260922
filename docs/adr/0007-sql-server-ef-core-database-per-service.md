# ADR-0007: SQL Server + EF Core com database-per-service

- **Status:** Aceita — complementada por [ADR-0015](0015-multi-tenancy-banco-compartilhado.md), [ADR-0017](0017-contexto-plataforma-onboarding-planos.md)
- **Data:** 2026-09-22
- **Decisores:** Igor Waterloo
- **Relacionadas:** [ADR-0002](0002-microsservicos-por-bounded-context.md), [ADR-0005](0005-outbox-e-consumidor-idempotente.md), [ADR-0012](0012-estrategia-de-testes.md)

## Contexto e problema

Precisamos persistir lançamentos (fonte da verdade, transacional, auditável) e a projeção de saldos. Também é preciso decidir **quantos bancos**, **qual tecnologia de acesso** e **como testar** a persistência.

## Requisitos da decisão

- **Transações ACID:** lançamento + outbox + idempotência numa única transação ([ADR-0005](0005-outbox-e-consumidor-idempotente.md)).
- Precisão monetária exata (`decimal`).
- Isolamento de falhas entre os contextos (RNF-01).
- Stack definida para o desafio: C#/.NET, SQL Server, EF Core.
- Execução local em Docker e testes de integração realistas.

## Opções consideradas

**Banco:**
1. **SQL Server 2022** (relacional)
2. PostgreSQL
3. NoSQL (MongoDB / Cosmos DB)

**Topologia:**
1. Banco compartilhado entre os serviços
2. **Um banco por serviço (database-per-service)**

**Acesso a dados:**
1. **EF Core**
2. Dapper
3. ADO.NET puro

**Banco nos testes:**
1. EF Core InMemory em todos os testes
2. **InMemory/SQLite só em unitários pontuais + SQL Server real via Testcontainers na integração**

## Decisão

- **SQL Server 2022** em container Docker (`mcr.microsoft.com/mssql/server:2022-latest`).
- **Database-per-service:** `LancamentosDb` (Lancamentos.Api) e `ConsolidadoDb` (Consolidado.Api/Worker). Localmente, as duas bases ficam na **mesma instância** por economia de recursos. Em produção, instâncias separadas. **Nenhum serviço acessa o banco do outro.**
- **EF Core** como ORM, com:
  - configurações via `IEntityTypeConfiguration<T>` (Fluent API), sem data annotations no domínio;
  - value objects mapeados como owned types/conversions (`Dinheiro` → `decimal(18,2)`);
  - **migrations** versionadas, aplicadas na inicialização em ambiente de desenvolvimento;
  - `EnableRetryOnFailure` (resiliência a falhas transitórias);
  - `DbContext pooling` e `AsNoTracking` nas queries;
  - `rowversion` para concorrência otimista no `SaldoDiario`.
- **Testes:** testes de integração usam **SQL Server real via Testcontainers**. O provider InMemory **não** é usado para validar comportamento transacional, pois não suporta transações nem constraints. Testes unitários não tocam em banco.

**Por que não banco compartilhado:** acopla os serviços pelo schema. Uma migration ou um lock num contexto afetaria o outro, o que quebra o isolamento do RNF-01 e cria dependência de deploy.

**Por que EF Core e não Dapper:** o lado de escrita se beneficia de unidade de trabalho, change tracking, concorrência otimista, migrations e da integração com o outbox do MassTransit. As leituras são simples e o EF Core com `AsNoTracking` + projeção tem desempenho suficiente. Dapper segue como opção pontual se algum relatório exigir SQL otimizado à mão.

**Por que relacional e não NoSQL:** o domínio é naturalmente relacional e transacional; ACID entre lançamento e outbox é essencial.

## Consequências

### Positivas
- ACID onde importa; `decimal(18,2)` exato para dinheiro.
- Isolamento de schema e de deploy entre serviços.
- Testes de integração fiéis à produção (mesmo motor, mesmas constraints).
- Produtividade do EF Core com migrations versionadas.

### Negativas / trade-offs aceitos
- Sem joins entre contextos; dados compartilhados só via eventos.
- A imagem do SQL Server é pesada (~1,5 GB, 2 GB de RAM mínimo) para rodar localmente.
- Testcontainers exige Docker nos testes de integração, inclusive no CI.

### Mitigações
- Os eventos carregam os dados que o consumidor precisa (sem consultas cruzadas).
- Documentação dos requisitos de Docker no README; o CI do GitHub Actions já tem Docker disponível.

## Atualizações

- **2026-09-22 — SaaS:** um terceiro banco, **`TenantsDb`**, pertence ao `Tenants.Api` ([ADR-0017](0017-contexto-plataforma-onboarding-planos.md)). Todas as tabelas de negócio ganham a coluna **`TenantId`**, com filtro global do EF Core e índices/PKs iniciados por `TenantId` ([ADR-0015](0015-multi-tenancy-banco-compartilhado.md)).

## Referências
- Chris Richardson — *Database per service pattern*
- Microsoft Docs — *Testing EF Core applications: choosing a testing strategy*
