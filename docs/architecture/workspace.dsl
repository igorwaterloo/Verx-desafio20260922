/*
 * Modelo C4 — Fluxo de Caixa Diário (SaaS multi-tenant) — fonte formal da arquitetura
 *
 * Visualizar localmente (requer Docker):
 *   docker run -it --rm -p 8090:8080 -v "${PWD}/docs/architecture:/usr/local/structurizr" structurizr/structurizr local
 *   http://localhost:8090
 *
 * Validar:
 *   docker run --rm -v "${PWD}/docs/architecture:/usr/local/structurizr" structurizr/structurizr validate -workspace /usr/local/structurizr/workspace.dsl
 *
 * Os diagramas em Mermaid (c4-*.md) são derivados deste modelo para renderização direta no GitHub.
 */
workspace "Fluxo de Caixa Diário (SaaS)" "Plataforma SaaS multi-tenant para controle de lançamentos e saldo diário consolidado de comerciantes." {

    !identifiers hierarchical

    model {
        admin = person "Administrador do Tenant" "Cadastra a empresa (onboarding), gerencia usuários e plano, registra e estorna lançamentos."
        operador = person "Operador" "Usuário da empresa que registra lançamentos e consulta o consolidado."

        keycloak = softwareSystem "Keycloak" "Provedor de identidade (OIDC). Organizations = tenants. Emite JWT com tenant_id, plano e roles." "Externo"

        fluxoCaixa = softwareSystem "Fluxo de Caixa (SaaS)" "Plataforma multi-tenant: onboarding, planos, lançamentos e saldo diário consolidado." {

            spa = container "Web App" "Onboarding, lançamentos, consolidado e gestão de usuários." "Angular (SPA) servida por nginx" "Browser"

            gateway = container "API Gateway" "Ponto único de entrada: roteamento, JWT, rate limiting por tenant/plano, CORS, balanceamento entre réplicas." "ASP.NET Core + YARP"

            group "Contexto Plataforma" {
                tenantsApi = container "Tenants.Api" "Onboarding de tenants (saga com compensação), catálogo de planos, quotas e usuários do tenant." "ASP.NET Core Web API (.NET 10)" {
                    tControllers = component "Controllers /api/v1/tenants, /api/v1/planos" "Onboarding público, tenant atual, plano e usuários (admin)." "ASP.NET Core Controllers"
                    tHandlers = component "Command/Query Handlers" "ProvisionarTenant, AlterarPlano, AdicionarUsuario, ListarPlanos." "Application"
                    tDominio = component "Domínio" "Agregado Tenant, value object Cnpj, catálogo de Planos, regras RP-01..RP-05." "Domain"
                    tKeycloak = component "Keycloak Admin Client" "Cria Organization e usuários; retry e compensação." "Infrastructure / HttpClient + Polly"
                    tProvisionamentoJob = component "Job de Provisionamento" "Reprocessa tenants Pendentes com backoff." "BackgroundService"
                    tRepo = component "Repositório + Outbox" "Persistência EF Core e publicação de eventos." "Infrastructure / EF Core + MassTransit"
                }
                tenantsDb = container "TenantsDb" "Tenants, planos e outbox." "SQL Server 2022" "Database"
            }

            group "Contexto Lançamentos" {
                lancamentosApi = container "Lancamentos.Api" "Registra, estorna e consulta lançamentos do tenant; aplica quota do plano. Publica eventos via Transactional Outbox." "ASP.NET Core Web API (.NET 10)" {
                    controllers = component "LancamentosController" "HTTP → command/query; filtros de tenant e Idempotency-Key; ProblemDetails." "ASP.NET Core Controllers"
                    tenantContext = component "Tenant Context" "Resolve TenantId e usuário a partir do JWT (middleware + ITenantContext scoped)." "Infrastructure / ASP.NET Core"
                    dispatcher = component "Dispatcher CQRS" "Resolve o handler e aplica decorators de validação e logging." "Application"
                    commandHandlers = component "Command Handlers" "RegistrarLancamento (com verificação de quota), EstornarLancamento." "Application"
                    queryHandlers = component "Query Handlers" "ObterLancamentoPorId, ListarLancamentos. Leitura sem tracking, projetada em DTOs." "Application"
                    dominio = component "Domínio" "Agregado Lancamento, value object Dinheiro, regras RN-01..RN-10." "Domain"
                    repositorio = component "Repositório + DbContext" "EF Core com filtro global por TenantId." "Infrastructure / EF Core"
                    outbox = component "Outbox Publisher" "Grava o evento na mesma transação e publica no broker." "Infrastructure / MassTransit"
                    planoConsumer = component "TenantPlanoConsumer" "Atualiza a projeção local TenantPlano a partir dos eventos da Plataforma." "MassTransit Consumer"
                }
                lancamentosDb = container "LancamentosDb" "Lançamentos, projeção TenantPlano, outbox/inbox e chaves de idempotência (todas as tabelas com TenantId)." "SQL Server 2022" "Database"
            }

            group "Contexto Consolidado" {
                consolidadoApi = container "Consolidado.Api" "Consulta o saldo diário e por período do tenant. Leitura com cache-aside. Executa com 2+ réplicas." "ASP.NET Core Web API (.NET 10)" {
                    cControllers = component "ConsolidadoController" "Consulta de saldo por dia e por período." "ASP.NET Core Controllers"
                    cTenantContext = component "Tenant Context" "Resolve TenantId a partir do JWT." "Infrastructure / ASP.NET Core"
                    cQueryHandlers = component "Query Handlers" "ObterSaldoDiario, ObterConsolidadoPeriodo." "Application"
                    cache = component "Cache Service" "Cache-aside no Redis (chaves por tenant), TTL e fallback para o banco." "Infrastructure / StackExchange.Redis"
                    cReadRepo = component "Repositório de leitura" "Consultas EF Core sem tracking, filtro global por TenantId." "Infrastructure / EF Core"
                }
                consolidadoWorker = container "Consolidado.Worker" "Consome eventos de lançamento e atualiza a projeção de saldos por tenant." ".NET 10 Worker Service" {
                    consumer = component "LancamentoRegistradoConsumer" "Recebe o evento, com retry exponencial e DLQ; define o TenantContext a partir da mensagem." "MassTransit Consumer"
                    aplicarHandler = component "AplicarLancamentoNoSaldo" "Aplica o lançamento no SaldoDiario de forma idempotente (inbox)." "Application"
                    cDominio = component "Domínio" "Agregado SaldoDiario, regras RC-01..RC-05." "Domain"
                    cWriteRepo = component "Repositório + Inbox" "Upsert do saldo e registro do EventId na mesma transação." "Infrastructure / EF Core"
                    invalidador = component "Invalidador de cache" "Remove as chaves do tenant/dia afetados após o commit." "Infrastructure / Redis"
                }
                consolidadoDb = container "ConsolidadoDb" "Projeção de saldos diários por tenant e inbox." "SQL Server 2022" "Database"
                redis = container "Redis" "Cache de leitura do consolidado (chaves por tenant)." "Redis 8" "Cache"
            }

            broker = container "RabbitMQ" "Transporte de eventos entre os contextos. Filas duráveis e DLQ." "RabbitMQ 4 (AMQP 0-9-1)" "Queue"
            observabilidade = container "Aspire Dashboard" "Coleta e exibe traces, métricas e logs (com tenant.id)." "OpenTelemetry (OTLP)" "Observability"
        }

        # Contexto
        admin -> fluxoCaixa "Cadastra a empresa, gerencia usuários/plano, registra e estorna lançamentos" "HTTPS"
        operador -> fluxoCaixa "Registra lançamentos e consulta o consolidado" "HTTPS"
        admin -> keycloak "Autentica-se" "HTTPS / OIDC"
        operador -> keycloak "Autentica-se" "HTTPS / OIDC"
        fluxoCaixa -> keycloak "Valida tokens (JWKS) e provisiona organizações/usuários (Admin API)" "HTTPS"

        # Containers
        admin -> fluxoCaixa.spa "Usa" "HTTPS"
        operador -> fluxoCaixa.spa "Usa" "HTTPS"
        fluxoCaixa.spa -> keycloak "Login (Authorization Code + PKCE)" "HTTPS / OIDC"
        fluxoCaixa.spa -> fluxoCaixa.gateway "Chama APIs com Bearer token" "HTTPS / JSON"
        fluxoCaixa.gateway -> fluxoCaixa.tenantsApi "Encaminha /api/v1/tenants/*, /api/v1/planos/*" "HTTP / JSON"
        fluxoCaixa.gateway -> fluxoCaixa.lancamentosApi "Encaminha /api/v1/lancamentos/*" "HTTP / JSON"
        fluxoCaixa.gateway -> fluxoCaixa.consolidadoApi "Encaminha /api/v1/consolidado/* (round-robin + health check)" "HTTP / JSON"
        fluxoCaixa.gateway -> keycloak "Obtém chaves públicas (JWKS)" "HTTPS"
        fluxoCaixa.tenantsApi -> keycloak "Cria Organization e usuários" "HTTPS / Admin REST API"
        fluxoCaixa.tenantsApi -> fluxoCaixa.tenantsDb "Lê e grava tenants e outbox" "TDS / EF Core"
        fluxoCaixa.tenantsApi -> fluxoCaixa.broker "Publica TenantProvisionado, PlanoDoTenantAlterado" "AMQP"
        fluxoCaixa.broker -> fluxoCaixa.lancamentosApi "Entrega eventos de tenant/plano" "AMQP"
        fluxoCaixa.lancamentosApi -> fluxoCaixa.lancamentosDb "Lê e grava lançamentos, projeção de plano e outbox" "TDS / EF Core"
        fluxoCaixa.lancamentosApi -> fluxoCaixa.broker "Publica LancamentoRegistrado" "AMQP"
        fluxoCaixa.broker -> fluxoCaixa.consolidadoWorker "Entrega LancamentoRegistrado" "AMQP"
        fluxoCaixa.consolidadoWorker -> fluxoCaixa.consolidadoDb "Upsert de SaldoDiario + inbox" "TDS / EF Core"
        fluxoCaixa.consolidadoWorker -> fluxoCaixa.redis "Invalida cache" "RESP"
        fluxoCaixa.consolidadoApi -> fluxoCaixa.redis "Lê/grava cache (cache-aside)" "RESP"
        fluxoCaixa.consolidadoApi -> fluxoCaixa.consolidadoDb "Lê saldos (cache miss)" "TDS / EF Core"
        fluxoCaixa.gateway -> fluxoCaixa.observabilidade "Telemetria" "OTLP"
        fluxoCaixa.tenantsApi -> fluxoCaixa.observabilidade "Telemetria" "OTLP"
        fluxoCaixa.lancamentosApi -> fluxoCaixa.observabilidade "Telemetria" "OTLP"
        fluxoCaixa.consolidadoApi -> fluxoCaixa.observabilidade "Telemetria" "OTLP"
        fluxoCaixa.consolidadoWorker -> fluxoCaixa.observabilidade "Telemetria" "OTLP"

        # Componentes — Plataforma
        fluxoCaixa.gateway -> fluxoCaixa.tenantsApi.tControllers "Encaminha requisições" "HTTP / JSON"
        fluxoCaixa.tenantsApi.tControllers -> fluxoCaixa.tenantsApi.tHandlers "Despacha commands/queries"
        fluxoCaixa.tenantsApi.tHandlers -> fluxoCaixa.tenantsApi.tDominio "Executa regras"
        fluxoCaixa.tenantsApi.tHandlers -> fluxoCaixa.tenantsApi.tKeycloak "Provisiona identidade"
        fluxoCaixa.tenantsApi.tHandlers -> fluxoCaixa.tenantsApi.tRepo "Persiste + outbox"
        fluxoCaixa.tenantsApi.tProvisionamentoJob -> fluxoCaixa.tenantsApi.tHandlers "Reprocessa pendentes"
        fluxoCaixa.tenantsApi.tKeycloak -> keycloak "Admin REST API" "HTTPS"
        fluxoCaixa.tenantsApi.tRepo -> fluxoCaixa.tenantsDb "SQL" "TDS"
        fluxoCaixa.tenantsApi.tRepo -> fluxoCaixa.broker "Publica" "AMQP"

        # Componentes — Lançamentos
        fluxoCaixa.gateway -> fluxoCaixa.lancamentosApi.controllers "Encaminha requisições" "HTTP / JSON"
        fluxoCaixa.lancamentosApi.controllers -> fluxoCaixa.lancamentosApi.tenantContext "Obtém tenant/usuário"
        fluxoCaixa.lancamentosApi.controllers -> fluxoCaixa.lancamentosApi.dispatcher "Envia command/query"
        fluxoCaixa.lancamentosApi.dispatcher -> fluxoCaixa.lancamentosApi.commandHandlers "Despacha commands"
        fluxoCaixa.lancamentosApi.dispatcher -> fluxoCaixa.lancamentosApi.queryHandlers "Despacha queries"
        fluxoCaixa.lancamentosApi.commandHandlers -> fluxoCaixa.lancamentosApi.dominio "Executa regras"
        fluxoCaixa.lancamentosApi.commandHandlers -> fluxoCaixa.lancamentosApi.repositorio "Persiste; consulta quota"
        fluxoCaixa.lancamentosApi.commandHandlers -> fluxoCaixa.lancamentosApi.outbox "Registra evento de integração"
        fluxoCaixa.lancamentosApi.queryHandlers -> fluxoCaixa.lancamentosApi.repositorio "Consulta"
        fluxoCaixa.lancamentosApi.repositorio -> fluxoCaixa.lancamentosApi.tenantContext "Filtro global por TenantId"
        fluxoCaixa.lancamentosApi.repositorio -> fluxoCaixa.lancamentosDb "SQL" "TDS"
        fluxoCaixa.lancamentosApi.outbox -> fluxoCaixa.lancamentosDb "Tabela OutboxMessage" "TDS"
        fluxoCaixa.lancamentosApi.outbox -> fluxoCaixa.broker "Publica" "AMQP"
        fluxoCaixa.broker -> fluxoCaixa.lancamentosApi.planoConsumer "Entrega eventos de plano" "AMQP"
        fluxoCaixa.lancamentosApi.planoConsumer -> fluxoCaixa.lancamentosApi.repositorio "Atualiza TenantPlano"

        # Componentes — Consolidado
        fluxoCaixa.gateway -> fluxoCaixa.consolidadoApi.cControllers "Encaminha requisições" "HTTP / JSON"
        fluxoCaixa.consolidadoApi.cControllers -> fluxoCaixa.consolidadoApi.cTenantContext "Obtém tenant"
        fluxoCaixa.consolidadoApi.cControllers -> fluxoCaixa.consolidadoApi.cQueryHandlers "Envia query"
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
                    deploymentNode "keycloak" "" "quay.io/keycloak/keycloak:26.7.4" {
                        softwareSystemInstance keycloak
                    }
                    deploymentNode "gateway" "" "container .NET 10" {
                        containerInstance fluxoCaixa.gateway
                    }
                    deploymentNode "tenants-api" "" "container .NET 10" {
                        containerInstance fluxoCaixa.tenantsApi
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
                    deploymentNode "sqlserver" "" "mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04" {
                        containerInstance fluxoCaixa.tenantsDb
                        containerInstance fluxoCaixa.lancamentosDb
                        containerInstance fluxoCaixa.consolidadoDb
                    }
                    deploymentNode "rabbitmq" "" "rabbitmq:4.3-management-alpine" {
                        containerInstance fluxoCaixa.broker
                    }
                    deploymentNode "redis" "" "redis:8.8-alpine" {
                        containerInstance fluxoCaixa.redis
                    }
                    deploymentNode "aspire-dashboard" "" "mcr.microsoft.com/dotnet/aspire-dashboard:13.5" {
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
                    deploymentNode "tenants-api" "" "pods" "" 2 {
                        containerInstance fluxoCaixa.tenantsApi
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
                deploymentNode "Azure SQL Database" "" "Business Critical, zone-redundant (elastic pool)" {
                    containerInstance fluxoCaixa.tenantsDb
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

        component fluxoCaixa.tenantsApi "C3-Componentes-Tenants" {
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
