# syntax=docker/dockerfile:1

# SPA Angular: build com Node e publicação em nginx sem privilégios (porta 8080).
# O /config.json é gerado na inicialização a partir de variáveis de ambiente, então a mesma
# imagem serve qualquer ambiente (ADR-0018).

FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN --mount=type=cache,target=/root/.npm,sharing=locked npm ci --no-audit --no-fund
COPY . .
RUN npx ng build --configuration production

FROM nginxinc/nginx-unprivileged:1.29-alpine AS final
COPY docker/nginx.conf /etc/nginx/conf.d/default.conf
COPY docker/cabecalhos-seguranca.conf /etc/nginx/seguranca/cabecalhos.conf
COPY --chmod=755 docker/configuracao.sh /docker-entrypoint.d/15-configuracao.sh
COPY --from=build /app/dist/fluxo-caixa-web/browser /usr/share/nginx/html

USER root
RUN rm -f /usr/share/nginx/html/config.json \
 && touch /usr/share/nginx/html/config.json /etc/nginx/seguranca/csp.conf \
 && chown 101:101 /usr/share/nginx/html/config.json /etc/nginx/seguranca/csp.conf
USER 101

ENV API_URL=http://localhost:8080 \
    OIDC_AUTHORITY=http://localhost:8081/realms/fluxo-caixa \
    OIDC_CLIENT_ID=fluxo-caixa-web

EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --retries=3 CMD wget -q -O /dev/null http://127.0.0.1:8080/health || exit 1
