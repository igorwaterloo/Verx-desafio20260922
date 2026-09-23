#!/bin/sh
# Gera o /config.json da SPA e a Content-Security-Policy a partir das variáveis de ambiente.
set -eu

origem() { echo "$1" | sed -E 's#^(https?://[^/]+).*#\1#'; }

API_ORIGEM=$(origem "$API_URL")
IDP_ORIGEM=$(origem "$OIDC_AUTHORITY")

cat > /usr/share/nginx/html/config.json <<JSON
{
  "apiUrl": "$API_URL",
  "oidc": { "authority": "$OIDC_AUTHORITY", "clientId": "$OIDC_CLIENT_ID" }
}
JSON

# Estilos inline são exigidos pelo Angular/Material (estilos de componentes); scripts, não.
cat > /etc/nginx/seguranca/csp.conf <<CSP
add_header Content-Security-Policy "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self' $API_ORIGEM $IDP_ORIGEM; frame-src $IDP_ORIGEM; form-action 'self' $IDP_ORIGEM; frame-ancestors 'none'; base-uri 'self'; object-src 'none'" always;
CSP

echo "configuracao: API=$API_URL IdP=$OIDC_AUTHORITY"
