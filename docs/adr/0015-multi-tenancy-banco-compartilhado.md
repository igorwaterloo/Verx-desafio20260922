# ADR-0015: SaaS multi-tenant com banco compartilhado e discriminador `TenantId`

- **Status:** Aceita
- **Data:** 2026-09-22
- **Decisores:** Igor Waterloo
- **Relacionadas:** [ADR-0006](0006-cache-aside-redis.md), [ADR-0007](0007-sql-server-ef-core-database-per-service.md), [ADR-0008](0008-autenticacao-keycloak-oidc-jwt.md), [ADR-0009](0009-api-gateway-yarp.md), [ADR-0017](0017-contexto-plataforma-onboarding-planos.md)

## Contexto e problema

A solução será oferecida como **SaaS**: vários comerciantes (empresas) usam a mesma plataforma, cada um com vários usuários. Cada empresa é um **tenant**. É preciso decidir:
1. como **isolar os dados** de cada tenant;
2. como o sistema **identifica o tenant** de cada requisição e de cada mensagem;
3. como evitar que um tenant degrade o serviço dos outros (**noisy neighbor**).

Um vazamento de dados entre tenants (um comerciante vendo o caixa de outro) é o **pior incidente possível** para o produto: é falha de confidencialidade e violação da LGPD.

## Requisitos da decisão

- **Isolamento lógico garantido** entre tenants (SLO-09: zero vazamento).
- Custo por tenant baixo: o público-alvo inclui pequenos comerciantes, com muitos tenants pequenos.
- Onboarding **instantâneo**, sem provisionar infraestrutura por tenant.
- Operação simples: uma migration por serviço, não uma por tenant.
- Caminho de evolução para clientes que exijam isolamento físico.

## Opções consideradas

1. **Banco compartilhado, schema compartilhado, coluna `TenantId`** (pool)
2. **Schema por tenant** no mesmo banco (bridge)
3. **Banco por tenant** (silo)
4. Híbrido: pool para a maioria + silo para tenants enterprise

## Decisão

**Opção escolhida:** **banco e schema compartilhados, com a coluna `TenantId`** em todas as tabelas de negócio de cada serviço, mantendo o database-per-service da [ADR-0007](0007-sql-server-ef-core-database-per-service.md).

### Identificação do tenant

| Onde | Como |
|---|---|
| Requisição HTTP | Claim **`tenant_id`** do JWT emitido pelo Keycloak (**Organizations**: cada tenant é uma organização). **Nunca** por header, query string ou corpo |
| Aplicação | `ITenantContext` (scoped), preenchido por middleware a partir do token. Requisição autenticada sem `tenant_id` recebe **403** |
| Mensagens | Todo evento de integração carrega `tenantId`. O consumidor cria o `ITenantContext` a partir da mensagem |
| Logs, traces, métricas | `tenant.id` como atributo (troubleshooting e métricas por tenant) |

### Isolamento em profundidade (várias camadas)

1. **Filtro global do EF Core** (`HasQueryFilter(e => e.TenantId == _tenant.Id)`) em todas as entidades que implementam `ITenantEntity`. O filtro vale para toda consulta, sem depender da disciplina do desenvolvedor.
2. **Preenchimento automático** do `TenantId` na gravação (interceptor do `SaveChanges`). Gravar entidade de outro tenant lança exceção.
3. **Chaves e índices** começam por `TenantId` (ex.: PK `(TenantId, Id)`, índice `(TenantId, DataCompetencia)`). Isso dá desempenho e impede colisões.
4. **Cache** com prefixo do tenant: `consolidado:{tenantId}:{data}`.
5. **Recurso de outro tenant responde 404** (não 403), para não revelar a existência do recurso.
6. **Testes automatizados de isolamento** em cada serviço: dados criados pelo tenant A nunca aparecem para o tenant B (leitura, listagem, estorno, consolidado, cache).
7. **Teste de arquitetura:** toda entidade persistida do domínio implementa `ITenantEntity` (exceto catálogos globais, como planos).

**Evolução prevista:** SQL Server **Row-Level Security** (`SESSION_CONTEXT('TenantId')`) como camada adicional no banco, contra consultas SQL cruas.

### Noisy neighbor

- **Rate limiting por tenant** no gateway, com limites definidos pelo **plano** ([ADR-0017](0017-contexto-plataforma-onboarding-planos.md)).
- **Quotas** de uso por plano (lançamentos/mês, usuários).
- Métricas por tenant para identificar abusos.

## Consequências

### Positivas
- Custo marginal de um tenant novo ≈ zero; onboarding instantâneo.
- Uma migration por serviço; operação e deploy simples.
- Uso eficiente de recursos (pool compartilhado).
- Consultas por tenant eficientes graças aos índices compostos.

### Negativas / trade-offs aceitos
- Isolamento **lógico**, não físico: um bug no filtro pode vazar dados. É o maior risco desta opção.
- Backup, restore e exclusão (LGPD) **por tenant** exigem operações por `TenantId`, não por banco.
- Um tenant muito grande afeta o desempenho do banco compartilhado.
- Não atende clientes que exijam contratualmente um banco dedicado.

### Mitigações
- Isolamento em 7 camadas (acima), com **testes de isolamento obrigatórios** no pipeline.
- Rotina de exportação e exclusão por tenant (LGPD) na evolução futura.
- Monitoramento de volume por tenant; **evolução para modelo híbrido**: tenants enterprise em banco dedicado, resolvido por um catálogo `TenantId → connection string`. A abstração `ITenantContext` já prepara essa mudança.

## Comparativo das opções

| Critério | **Pool (TenantId)** | Schema por tenant | Banco por tenant |
|---|---|---|---|
| Isolamento | Lógico | Lógico+ | Físico |
| Custo por tenant | Muito baixo | Baixo | Alto |
| Onboarding | Instantâneo | Cria schema | Provisiona banco |
| Migrations | 1× | N× | N× |
| Noisy neighbor | Mitigado por quotas | Mitigado | Isolado |
| Restore por tenant | Difícil | Médio | Fácil |
| Escala até | Milhares de tenants | Centenas | Dezenas / centenas |

## Referências
- Microsoft — *Multitenant SaaS database tenancy patterns* (Azure Architecture Center)
- AWS — *SaaS Tenant Isolation Strategies* (whitepaper)
- Keycloak — *Organizations* (multi-tenancy nativo a partir da versão 26)
