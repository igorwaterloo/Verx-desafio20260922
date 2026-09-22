/*
 * Modelo C4 — Fluxo de Caixa Diário (fonte formal da arquitetura)
 *
 * Visualizar localmente (requer Docker):
 *   docker run -it --rm -p 8090:8080 -v "${PWD}/docs/architecture:/usr/local/structurizr" structurizr/lite
 *   http://localhost:8090
 *
 * Os diagramas em Mermaid (c4-*.md) são derivados deste modelo para renderização direta no GitHub.
 */
workspace "Fluxo de Caixa Diário" "Controle de lançamentos e saldo diário consolidado de um comerciante." {

    !identifiers hierarchical

    model {
        properties {
            "structurizr.groupSeparator" "/"
        }

        comerciante = person "Comerciante" "Registra créditos e débitos do caixa e consulta o saldo diário consolidado."

        keycloak = softwareSystem "Keycloak" "Provedor de identidade (OIDC). Autentica usuários e emite tokens JWT." "Externo"

        fluxoCaixa = softwareSystem "Fluxo de Caixa" "Permite registrar lançamentos e consultar o saldo diário consolidado." {

            spa = container "Web App" "Interface do comerciante: lançamentos e relatório de consolidado." "Angular (SPA) servida por nginx" "Browser"

            gateway = container "API Gateway" "Ponto único de entrada: roteamento, validação de JWT, rate limiting, CORS e balanceamento entre réplicas." "ASP.NET Core + YARP"

            group "Contexto Lançamentos" {
                lancamentosApi = container "Lancamentos.Api" "Registra, estorna e consulta lançamentos. Publica eventos via Transactional Outbox." "ASP.NET Core Minimal API (.NET 10)" {
                    endpoints = component "Endpoints /api/v1/lancamentos" "Mapeia HTTP para commands/queries, valida JWT e Idempotency-Key e retorna ProblemDetails." "Minimal API"
                    dispatcher = component "Dispatcher CQRS" "Resolve o handler do command/query e aplica decorators de validação e logging." "Application"
                    commandHandlers = component "Command Handlers" "RegistrarLancamento, EstornarLancamento. Orquestram o domínio e a unidade de trabalho." "Application"
                    queryHandlers = component "Query Handlers" "ObterLancamentoPorId, ListarLancamentos. Leitura sem tracking, projetada em DTOs." "Application"
                    dominio = component "Domínio" "Agregado Lancamento, value object Dinheiro, regras RN-01..RN-08." "Domain"
                    repositorio = component "Repositório + DbContext" "Persistência EF Core e mapeamentos." "Infrastructure / EF Core"
                    outbox = component "Outbox Publisher" "Grava o evento na mesma transação e publica no broker de forma assíncrona e confiável." "Infrastructure / MassTransit"
                }
                lancamentosDb = container "LancamentosDb" "Lançamentos, outbox e chaves de idempotência." "SQL Server 2022" "Database"
            }

            group "Contexto Consolidado" {
                consolidadoApi = container "Consolidado.Api" "Consulta o saldo diário e por período. Leitura com cache-aside. Executa com 2+ réplicas." "ASP.NET Core Minimal API (.NET 10)" {
                    cEndpoints = component "Endpoints /api/v1/consolidado" "Consulta de saldo por dia e por período." "Minimal API"
                    cQueryHandlers = component "Query Handlers" "ObterSaldoDiario, ObterConsolidadoPeriodo." "Application"
                    cache = component "Cache Service" "Cache-aside no Redis com TTL e fallback para o banco se o Redis falhar." "Infrastructure / StackExchange.Redis"
                    cReadRepo = component "Repositório de leitura" "Consultas EF Core sem tracking." "Infrastructure / EF Core"
                }
                consolidadoWorker = container "Consolidado.Worker" "Consome eventos de lançamento e atualiza a projeção de saldos." ".NET 10 Worker Service" {
                    consumer = component "LancamentoRegistradoConsumer" "Recebe o evento, com retry exponencial e DLQ." "MassTransit Consumer"
                    aplicarHandler = component "AplicarLancamentoNoSaldo" "Aplica o lançamento no SaldoDiario de forma idempotente (inbox)." "Application"
                    cDominio = component "Domínio" "Agregado SaldoDiario, regras RC-01..RC-04." "Domain"
                    cWriteRepo = component "Repositório + Inbox" "Upsert do saldo e registro do EventId na mesma transação." "Infrastructure / EF Core"
                    invalidador = component "Invalidador de cache" "Remove as chaves do dia/período afetados após o commit." "Infrastructure / Redis"
                }
                consolidadoDb = container "ConsolidadoDb" "Projeção de saldos diários e inbox de mensagens processadas." "SQL Server 2022" "Database"
                redis = container "Redis" "Cache de leitura do consolidado." "Redis 7" "Cache"
            }

            broker = container "RabbitMQ" "Transporte de eventos entre os contextos. Filas duráveis e DLQ." "RabbitMQ 4 (AMQP 0-9-1)" "Queue"
            observabilidade = container "Aspire Dashboard" "Coleta e exibe traces, métricas e logs." "OpenTelemetry (OTLP)" "Observability"
        }

        # Contexto
        comerciante -> fluxoCaixa "Registra lançamentos e consulta consolidado" "HTTPS"
        comerciante -> keycloak "Autentica-se" "HTTPS / OIDC"
        fluxoCaixa -> keycloak "Valida tokens (JWKS)" "HTTPS"

        # Containers
        comerciante -> fluxoCaixa.spa "Usa" "HTTPS"
        fluxoCaixa.spa -> keycloak "Login (Authorization Code + PKCE)" "HTTPS / OIDC"
        fluxoCaixa.spa -> fluxoCaixa.gateway "Chama APIs com Bearer token" "HTTPS / JSON"
        fluxoCaixa.gateway -> fluxoCaixa.lancamentosApi "Encaminha /api/v1/lancamentos/*" "HTTP / JSON"
        fluxoCaixa.gateway -> fluxoCaixa.consolidadoApi "Encaminha /api/v1/consolidado/* (round-robin + health check)" "HTTP / JSON"
        fluxoCaixa.gateway -> keycloak "Obtém chaves públicas (JWKS)" "HTTPS"
        fluxoCaixa.lancamentosApi -> fluxoCaixa.lancamentosDb "Lê e grava lançamentos e outbox" "TDS / EF Core"
        fluxoCaixa.lancamentosApi -> fluxoCaixa.broker "Publica LancamentoRegistrado" "AMQP"
        fluxoCaixa.broker -> fluxoCaixa.consolidadoWorker "Entrega LancamentoRegistrado" "AMQP"
        fluxoCaixa.consolidadoWorker -> fluxoCaixa.consolidadoDb "Upsert de SaldoDiario + inbox" "TDS / EF Core"
        fluxoCaixa.consolidadoWorker -> fluxoCaixa.redis "Invalida cache" "RESP"
        fluxoCaixa.consolidadoApi -> fluxoCaixa.redis "Lê/grava cache (cache-aside)" "RESP"
        fluxoCaixa.consolidadoApi -> fluxoCaixa.consolidadoDb "Lê saldos (cache miss)" "TDS / EF Core"
        fluxoCaixa.lancamentosApi -> fluxoCaixa.observabilidade "Telemetria" "OTLP"
        fluxoCaixa.consolidadoApi -> fluxoCaixa.observabilidade "Telemetria" "OTLP"
        fluxoCaixa.consolidadoWorker -> fluxoCaixa.observabilidade "Telemetria" "OTLP"
        fluxoCaixa.gateway -> fluxoCaixa.observabilidade "Telemetria" "OTLP"

        # Componentes — Lançamentos
        fluxoCaixa.gateway -> fluxoCaixa.lancamentosApi.endpoints "Encaminha requisições" "HTTP / JSON"
        fluxoCaixa.lancamentosApi.endpoints -> fluxoCaixa.lancamentosApi.dispatcher "Envia command/query"
        fluxoCaixa.lancamentosApi.dispatcher -> fluxoCaixa.lancamentosApi.commandHandlers "Despacha commands"
        fluxoCaixa.lancamentosApi.dispatcher -> fluxoCaixa.lancamentosApi.queryHandlers "Despacha queries"
        fluxoCaixa.lancamentosApi.commandHandlers -> fluxoCaixa.lancamentosApi.dominio "Executa regras"
        fluxoCaixa.lancamentosApi.commandHandlers -> fluxoCaixa.lancamentosApi.repositorio "Persiste (unidade de trabalho)"
        fluxoCaixa.lancamentosApi.commandHandlers -> fluxoCaixa.lancamentosApi.outbox "Registra evento de integração"
        fluxoCaixa.lancamentosApi.queryHandlers -> fluxoCaixa.lancamentosApi.repositorio "Consulta"
        fluxoCaixa.lancamentosApi.repositorio -> fluxoCaixa.lancamentosDb "SQL" "TDS"
        fluxoCaixa.lancamentosApi.outbox -> fluxoCaixa.lancamentosDb "Tabela OutboxMessage" "TDS"
        fluxoCaixa.lancamentosApi.outbox -> fluxoCaixa.broker "Publica" "AMQP"

        # Componentes — Consolidado
        fluxoCaixa.gateway -> fluxoCaixa.consolidadoApi.cEndpoints "Encaminha requisições" "HTTP / JSON"
        fluxoCaixa.consolidadoApi.cEndpoints -> fluxoCaixa.consolidadoApi.cQueryHandlers "Envia query"
        fluxoCaixa.consolidadoApi.cQueryHandlers -> fluxoCaixa.consolidadoApi.cache "Busca no cache"
        fluxoCaixa.consolidadoApi.cQueryHandlers -> fluxoCaixa.consolidadoApi.cReadRepo "Busca no banco (miss/falha do cache)"
        fluxoCaixa.consolidadoApi.cache -> fluxoCaixa.redis "GET/SET" "RESP"
        fluxoCaixa.consolidadoApi.cReadRepo -> fluxoCaixa.consolidadoDb "SQL" "TDS"
        fluxoCaixa.broker -> fluxoCaixa.consolidadoWorker.consumer "Entrega evento" "AMQP"
        fluxoCaixa.consolidadoWorker.consumer -> fluxoCaixa.consolidadoWorker.aplicarHandler "Executa command"
        fluxoCaixa.consolidadoWorker.aplicarHandler -> fluxoCaixa.consolidadoWorker.cDominio "Aplica lançamento"
        fluxoCaixa.consolidadoWorker.aplicarHandler -> fluxoCaixa.consolidadoWorker.cWriteRepo "Persiste saldo + inbox"
        fluxoCaixa.consolidadoWorker.aplicarHandler -> fluxoCaixa.consolidadoWorker.invalidador "Invalida cache após commit"
        fluxoCaixa.consolidadoWorker.cWriteRepo -> fluxoCaixa.consolidadoDb "SQL" "TDS"
        fluxoCaixa.consolidadoWorker.invalidador -> fluxoCaixa.redis "DEL" "RESP"

        # Implantação — ambiente local
        local = deploymentEnvironment "Local (Docker Compose)" {
            deploymentNode "Estação do desenvolvedor" "" "Windows / macOS / Linux + Docker Desktop" {
                deploymentNode "Docker Compose" "" "rede bridge fluxo-caixa" {
                    deploymentNode "web" "Serve os arquivos estáticos da SPA" "nginx:alpine" {
                        containerInstance fluxoCaixa.spa
                    }
                    deploymentNode "keycloak" "" "quay.io/keycloak/keycloak" {
                        softwareSystemInstance keycloak
                    }
                    deploymentNode "gateway" "" "container .NET 10" {
                        containerInstance fluxoCaixa.gateway
                    }
                    deploymentNode "lancamentos-api" "" "container .NET 10" {
                        containerInstance fluxoCaixa.lancamentosApi
                    }
                    deploymentNode "consolidado-api" "" "container .NET 10" "" 2 {
                        containerInstance fluxoCaixa.consolidadoApi
                    }
                    deploymentNode "consolidado-worker" "" "container .NET 10" {
                        containerInstance fluxoCaixa.consolidadoWorker
                    }
                    deploymentNode "sqlserver" "" "mcr.microsoft.com/mssql/server:2022-latest" {
                        containerInstance fluxoCaixa.lancamentosDb
                        containerInstance fluxoCaixa.consolidadoDb
                    }
                    deploymentNode "rabbitmq" "" "rabbitmq:4-management" {
                        containerInstance fluxoCaixa.broker
                    }
                    deploymentNode "redis" "" "redis:7-alpine" {
                        containerInstance fluxoCaixa.redis
                    }
                    deploymentNode "aspire-dashboard" "" "mcr.microsoft.com/dotnet/aspire-dashboard" {
                        containerInstance fluxoCaixa.observabilidade
                    }
                }
            }
        }

        # Implantação — produção (visão alvo)
        producao = deploymentEnvironment "Produção (Azure — visão alvo)" {
            deploymentNode "Azure Front Door + WAF" "CDN, TLS e proteção OWASP/DDoS" "Azure Front Door" {
                deploymentNode "Static Web App" "" "Azure Storage / SWA" {
                    containerInstance fluxoCaixa.spa
                }
            }
            deploymentNode "Azure Region (3 zonas de disponibilidade)" "" "Brazil South" {
                deploymentNode "AKS / Azure Container Apps" "Autoscaling horizontal (HPA/KEDA)" "Kubernetes" {
                    deploymentNode "gateway" "" "pods" "" 2 {
                        containerInstance fluxoCaixa.gateway
                    }
                    deploymentNode "lancamentos-api" "" "pods" "" 3 {
                        containerInstance fluxoCaixa.lancamentosApi
                    }
                    deploymentNode "consolidado-api" "" "pods (HPA por CPU/RPS)" "" 3 {
                        containerInstance fluxoCaixa.consolidadoApi
                    }
                    deploymentNode "consolidado-worker" "" "pods (KEDA por tamanho de fila)" "" 2 {
                        containerInstance fluxoCaixa.consolidadoWorker
                    }
                }
                deploymentNode "Azure SQL Database" "" "Business Critical, zone-redundant" {
                    containerInstance fluxoCaixa.lancamentosDb
                    containerInstance fluxoCaixa.consolidadoDb
                }
                deploymentNode "RabbitMQ cluster (quorum queues)" "" "3 nós" {
                    containerInstance fluxoCaixa.broker
                }
                deploymentNode "Azure Cache for Redis" "" "Premium, zone-redundant" {
                    containerInstance fluxoCaixa.redis
                }
                deploymentNode "Azure Monitor / App Insights" "" "OpenTelemetry" {
                    containerInstance fluxoCaixa.observabilidade
                }
            }
        }
    }

    views {
        systemContext fluxoCaixa "C1-Contexto" {
            include *
            autoLayout lr
        }

        container fluxoCaixa "C2-Containers" {
            include *
            autoLayout lr
        }

        component fluxoCaixa.lancamentosApi "C3-Componentes-Lancamentos" {
            include *
            autoLayout lr
        }

        component fluxoCaixa.consolidadoApi "C3-Componentes-Consolidado-Api" {
            include *
            autoLayout lr
        }

        component fluxoCaixa.consolidadoWorker "C3-Componentes-Consolidado-Worker" {
            include *
            autoLayout lr
        }

        deployment fluxoCaixa local "Deploy-Local" {
            include *
            autoLayout lr
        }

        deployment fluxoCaixa producao "Deploy-Producao" {
            include *
            autoLayout lr
        }

        styles {
            element "Element" {
                color #ffffff
            }
            element "Person" {
                background #08427b
                shape Person
            }
            element "Software System" {
                background #1168bd
            }
            element "Externo" {
                background #999999
            }
            element "Container" {
                background #438dd5
            }
            element "Component" {
                background #85bbf0
                color #000000
            }
            element "Browser" {
                shape WebBrowser
            }
            element "Database" {
                shape Cylinder
            }
            element "Cache" {
                shape Cylinder
                background #d6336c
            }
            element "Queue" {
                shape Pipe
                background #f08c00
            }
            element "Observability" {
                background #5f3dc4
            }
        }
    }
}
