# Documentação do Projeto

Índice de toda a documentação de arquitetura e de projeto.

## Arquitetura

| Documento | Conteúdo |
|---|---|
| [Modelo de domínio](dominio.md) | Linguagem ubíqua, bounded contexts, agregados, regras de negócio e eventos |
| [C4 — Nível 1: Contexto](architecture/c4-1-contexto.md) | Usuários e sistemas externos |
| [C4 — Nível 2: Containers](architecture/c4-2-containers.md) | Aplicações, bancos, broker, cache e protocolos |
| [C4 — Nível 3: Componentes](architecture/c4-3-componentes.md) | Clean Architecture + CQRS em cada serviço |
| [Fluxos de dados](architecture/fluxos.md) | Diagramas de sequência, incluindo os cenários de falha |
| [Implantação](architecture/deployment.md) | Docker Compose local e visão alvo em produção (Azure) |
| [Modelo Structurizr DSL](architecture/workspace.dsl) | Fonte formal do modelo C4 |

## Decisões e requisitos

| Documento | Conteúdo |
|---|---|
| [Requisitos não funcionais](requisitos-nao-funcionais.md) | SLOs, métricas, capacidade, RPO/RTO |
| [ADRs](adr/README.md) | Registro de decisões arquiteturais |

## Qualidade e evolução

| Documento | Conteúdo |
|---|---|
| [Estratégia de testes](testes.md) | Pirâmide de testes, TDD e resultados de carga |
| [Evolução futura](evolucao-futura.md) | Melhorias planejadas e próximos passos |

## Como visualizar os diagramas

- **No GitHub:** os diagramas Mermaid são renderizados automaticamente nos arquivos `.md`.
- **Structurizr Lite** (modelo completo e interativo, requer Docker):
  ```bash
  docker run -it --rm -p 8090:8080 -v "$(pwd)/docs/architecture:/usr/local/structurizr" structurizr/lite
  ```
  Depois acesse http://localhost:8090.
