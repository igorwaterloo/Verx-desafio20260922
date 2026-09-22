# ADR-0016: Web APIs com Controllers (ASP.NET Core Web API)

- **Status:** Aceita
- **Data:** 2026-09-22
- **Decisores:** Igor Waterloo
- **Relacionadas:** [ADR-0003](0003-clean-architecture-cqrs.md), [ADR-0015](0015-multi-tenancy-banco-compartilhado.md)

## Contexto e problema

Os serviços expõem **Web APIs REST** do produto SaaS. O ASP.NET Core oferece dois modelos: **Controllers** (MVC) e **Minimal APIs**. A escolha afeta organização do código, reuso de preocupações transversais (tenant, autorização, validação), versionamento e documentação OpenAPI.

## Requisitos da decisão

- Organização clara e familiar para equipes .NET (manutenibilidade de um produto que vai crescer).
- Filtros reutilizáveis: resolução de tenant, idempotência, mapeamento de `Result` para HTTP.
- Versionamento de API (`/api/v1`) e documentação OpenAPI completa.
- Autorização por papel e política (`admin`, `operador`).

## Opções consideradas

1. **Controllers** (`[ApiController]`)
2. **Minimal APIs** com `MapGroup` e endpoint filters

## Decisão

**Opção escolhida:** **Controllers** com `[ApiController]`.

| Aspecto | Convenção |
|---|---|
| Rotas | `api/v{version:apiVersion}/[recurso]` com **Asp.Versioning** (versão na URL) |
| Controllers | **Finos**: recebem o request, montam o command/query, chamam o `IDispatcher` e mapeiam o `Result` para a resposta. Sem regra de negócio |
| Base | `ApiControllerBase` com o helper `ToActionResult(Result)`, que devolve 200/201/204/400/404/409/422/429 + ProblemDetails (RFC 9457) |
| Filtros | `IdempotencyFilter` (header `Idempotency-Key`), `TenantRequiredFilter`, validação automática do `[ApiController]` |
| Autorização | `[Authorize(Policy = "Operador")]` / `"Admin"` por action |
| Contratos HTTP | Records de request/response próprios da Api (não expõem entidades de domínio) |
| OpenAPI | Geração nativa do .NET (`Microsoft.AspNetCore.OpenApi`) + UI **Scalar**, com `[ProducesResponseType]` em cada action |

**Por que não Minimal APIs:** são mais enxutas e ligeiramente mais rápidas. Mas, com muitos endpoints, políticas e filtros, a organização por controller dá **descoberta, padronização e convenções** mais fortes, que é o modelo mais difundido no mercado e em equipes corporativas. A diferença de desempenho é irrelevante perto de I/O (banco, cache) na escala do problema.

## Consequências

### Positivas
- Estrutura padrão e reconhecível; convenções MVC maduras (model binding, filtros, `ProblemDetails`).
- Preocupações transversais centralizadas em filtros e na classe base.
- Documentação OpenAPI rica.

### Negativas / trade-offs aceitos
- Um pouco mais de cerimônia e alocação por requisição que Minimal APIs.
- Risco de controllers "gordos".

### Mitigações
- Regra: o controller só despacha para a Application (testes de arquitetura verificam que controllers não dependem de Infrastructure nem de `DbContext`).

## Referências
- Microsoft Docs — *Choose between controller-based APIs and minimal APIs*
- [Asp.Versioning](https://github.com/dotnet/aspnet-api-versioning)
