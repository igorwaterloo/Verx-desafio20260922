# syntax=docker/dockerfile:1
# Imagem genérica dos serviços .NET (APIs, Worker e Gateway). Contexto de build: raiz do repositório.
#   docker build -f src/api/Dockerfile \
#     --build-arg PROJETO=src/api/Lancamentos/Lancamentos.Api/Lancamentos.Api.csproj \
#     --build-arg ASSEMBLY=Lancamentos.Api -t fluxo-caixa/lancamentos-api .

ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG PROJETO
WORKDIR /src

# Arquivos de build compartilhados (versões centralizadas, SDK fixado, analisadores).
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/api/ src/api/

RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore "$PROJETO"
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish "$PROJETO" -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
ARG ASSEMBLY
ENV ASSEMBLY=${ASSEMBLY} \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
WORKDIR /app
COPY --from=build /app .

# Executa sem privilégios de root (usuário "app" das imagens oficiais).
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet \"$ASSEMBLY.dll\""]
