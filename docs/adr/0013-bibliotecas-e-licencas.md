# ADR-0013: Escolha de bibliotecas considerando licenciamento

- **Status:** Aceita
- **Data:** 2026-09-22
- **Decisores:** Igor Waterloo
- **Relacionadas:** [ADR-0003](0003-clean-architecture-cqrs.md), [ADR-0004](0004-comunicacao-assincrona-rabbitmq.md), [ADR-0012](0012-estrategia-de-testes.md)

## Contexto e problema

Entre 2025 e 2026, várias bibliotecas populares do ecossistema .NET **mudaram para licenças comerciais** em suas novas versões principais:
- **MediatR** e **AutoMapper** (a partir das versões lançadas em 2025);
- **FluentAssertions** (a partir da v8);
- **MassTransit** (a partir da v9; a v8 continua open source, Apache 2.0).

Adotar essas versões sem avaliação cria **risco jurídico e de custo** para a empresa. Escolher bibliotecas é uma decisão de arquitetura, não só de conveniência.

## Requisitos da decisão

- Licenças permissivas (MIT / Apache 2.0) ou custo explicitamente aceito.
- Baixo acoplamento à biblioteca (trocar sem reescrever o domínio).
- Manutenção ativa e compatibilidade com .NET 10.

## Decisão

| Necessidade | Escolha | Licença | Alternativa descartada e motivo |
|---|---|---|---|
| Dispatcher CQRS / pipeline | **Implementação própria** (~100 linhas: `ICommandHandler`, `IQueryHandler`, `IDispatcher` + decorators via DI) | Código do projeto | MediatR: licença comercial nas versões novas; a necessidade é simples |
| Mapeamento de objetos | **Mapeamento manual** (métodos de extensão / construtores) | — | AutoMapper: licença comercial; mapeamento explícito é mais legível e seguro |
| Validação | **FluentValidation** | Apache 2.0 | DataAnnotations: menos expressivo para regras compostas |
| Versionamento de API / OpenAPI | **Asp.Versioning** (Mvc, ApiExplorer, OpenApi) + **Scalar.AspNetCore** | MIT | Swashbuckle: substituído pela geração OpenAPI nativa do .NET |
| Mensageria | **MassTransit 8.x**, fixado em versão, atrás de uma abstração | Apache 2.0 | MassTransit 9 (comercial); RabbitMQ.Client puro (exigiria implementar outbox, retry e topologia à mão) |
| Assertions em testes | **Shouldly** | BSD-3 | FluentAssertions 8 (comercial); a alternativa comunitária AwesomeAssertions também seria válida |
| Mocks | **NSubstitute** | BSD-3 | Moq: controvérsia de privacidade (SponsorLink, 2023) |
| Testes | **xUnit v3**, **Testcontainers**, **NetArchTest**, **Bogus**, **Microsoft.Testing.Extensions.CodeCoverage** | Apache 2.0 / MIT | coverlet.collector: só funciona com VSTest (ver ADR-0012) |
| Gateway | **YARP** | MIT | — |
| Cache | **StackExchange.Redis** (cliente) | MIT | — |
| Servidor de cache | **Redis 8** (imagem oficial, usado sem modificação) | AGPLv3 (opção open source do Redis 8) | Redis 7.4–7.x: RSALv2/SSPL, não open source. **Valkey** (BSD, fork da Linux Foundation) é a alternativa compatível caso a AGPL seja restrição |
| Resiliência | **Microsoft.Extensions.Resilience** (Polly v8) | BSD-3 | — |
| Observabilidade | **OpenTelemetry .NET** (somente pacotes estáveis) | Apache 2.0 | Serilog: dispensado na implementação, o `ILogger` exporta direto via OTLP ([ADR-0011](0011-observabilidade-opentelemetry.md)) |
| Frontend | **Angular**, **Angular Material**, **angular-auth-oidc-client**, **material-symbols** (ícones servidos localmente) | MIT / Apache 2.0 | Bibliotecas de gráfico: gráfico próprio em SVG ([ADR-0018](0018-frontend-angular-spa.md)) |
| Testes do frontend e E2E | **Vitest**, **Playwright** | MIT / Apache 2.0 | Karma/Jasmine: substituídos pelo Vitest no Angular atual |
| Carga | **k6** (ferramenta executada em container) | AGPLv3 | A AGPL não afeta o produto: o k6 não é distribuído nem linkado, só executa os scripts de teste |
| Relatório de cobertura | **ReportGenerator** (ferramenta de CI) | Apache 2.0 | — |

**Isolamento do MassTransit:** a Application publica via uma porta própria (`IEventPublisher`), e o consumer é um adaptador fino que chama o command handler. Se a v8 deixar de ser suportada, as saídas são migrar para a v9 comercial, para **Wolverine** ou para **RabbitMQ.Client** com o outbox próprio, **sem tocar em Domain e Application**.

**Controle:** as versões ficam centralizadas em `Directory.Packages.props` (Central Package Management) e são revisadas a cada atualização.

## Consequências

### Positivas
- Nenhum custo de licença nem risco jurídico.
- Dispatcher próprio sem mágica: fácil de entender, depurar e testar.
- Dependências externas isoladas atrás de portas (Clean Architecture).

### Negativas / trade-offs aceitos
- Código próprio para manter (dispatcher e decorators).
- A MassTransit 8.x tem horizonte de suporte limitado.

### Mitigações
- O dispatcher é pequeno e coberto por testes unitários.
- Monitorar o fim de suporte da MassTransit 8 e registrar um novo ADR na migração.

## Referências
- Anúncios oficiais de licenciamento de MediatR/AutoMapper (2025), FluentAssertions 8 (2025) e MassTransit v9.
