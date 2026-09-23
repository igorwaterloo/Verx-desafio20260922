# Documentação do Projeto

Índice de toda a documentação de arquitetura e de projeto da plataforma **SaaS multi-tenant** de fluxo de caixa.

## Arquitetura

| Documento | Conteúdo |
|---|---|
| [Modelo de domínio](dominio.md) | Linguagem ubíqua, bounded contexts (Plataforma, Lançamentos, Consolidado), regras de multi-tenancy, agregados e eventos |
| [C4 — Nível 1: Contexto](architecture/c4-1-contexto.md) | Usuários e sistemas externos |
| [C4 — Nível 2: Containers](architecture/c4-2-containers.md) | Aplicações, bancos, broker, cache e protocolos |
| [C4 — Nível 3: Componentes](architecture/c4-3-componentes.md) | Clean Architecture + CQRS em cada serviço |
| [Fluxos de dados](architecture/fluxos.md) | Diagramas de sequência: cenários de falha, onboarding, quota, isolamento e rate limit por tenant |
| [Implantação](architecture/deployment.md) | Docker Compose local e visão alvo em produção (Azure) |
| [Modelo Structurizr DSL](architecture/workspace.dsl) | Fonte formal do modelo C4 |

## Decisões e requisitos

| Documento | Conteúdo |
|---|---|
| [Requisitos não funcionais](requisitos-nao-funcionais.md) | SLOs, métricas, capacidade, RPO/RTO, requisitos de SaaS (isolamento, noisy neighbor, LGPD) |
| [ADRs](adr/README.md) | Registro de decisões arquiteturais |
| [Segurança](seguranca.md) | Superfície exposta, ameaças (STRIDE), controles, evidências e riscos residuais |

## Qualidade e evolução

| Documento | Conteúdo |
|---|---|
| [Estratégia de testes](testes.md) | Pirâmide de testes, TDD e resultados de carga |
| [Evolução futura](evolucao-futura.md) | Melhorias planejadas e próximos passos |

## Como visualizar os diagramas

- **No GitHub:** os diagramas Mermaid são renderizados automaticamente nos arquivos `.md`.
- **Structurizr local** (modelo completo e interativo, requer Docker):
  ```bash
  docker run -it --rm -p 8090:8080 -v "$(pwd)/docs/architecture:/usr/local/structurizr" structurizr/structurizr local
  ```
  Para validar o DSL: `docker run --rm -v "$(pwd)/docs/architecture:/usr/local/structurizr" structurizr/structurizr validate -workspace /usr/local/structurizr/workspace.dsl`
  Depois acesse http://localhost:8090.
