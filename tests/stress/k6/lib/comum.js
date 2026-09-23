// Funções comuns dos testes de carga: configuração, tenants exclusivos da execução, tokens e datas.
import http from 'k6/http';
import { check, fail } from 'k6';
import { textSummary } from 'https://jslib.k6.io/k6-summary/0.1.0/index.js';

export const GATEWAY = __ENV.GATEWAY_URL || 'http://localhost:8080';
export const KEYCLOAK_TOKEN =
  (__ENV.KEYCLOAK_URL || 'http://localhost:8081') + '/realms/fluxo-caixa/protocol/openid-connect/token';
const CLIENT_ID = 'fluxo-caixa-testes';

/** CNPJ válido e único (o cadastro rejeita CNPJ repetido). */
export function gerarCnpj() {
  const base = [];
  for (let i = 0; i < 8; i++) base.push(Math.floor(Math.random() * 10));
  base.push(0, 0, 0, 1);
  const digito = (numeros, pesos) => {
    const resto = numeros.reduce((soma, n, i) => soma + n * pesos[i], 0) % 11;
    return resto < 2 ? 0 : 11 - resto;
  };
  const primeiro = digito(base, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
  const segundo = digito(base.concat(primeiro), [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
  return base.concat(primeiro, segundo).join('');
}

/**
 * Cadastra um tenant novo pelo onboarding público (usado no setup). Cada execução usa os próprios
 * tenants: os resultados não se misturam com dados de demonstração nem com execuções anteriores.
 * Devolve a sessão (tokens) do administrador do tenant criado.
 */
export function criarTenant(prefixo, plano) {
  const sufixo = `${Date.now().toString(36)}${Math.floor(Math.random() * 1e4)}`;
  const credenciais = { usuario: `${prefixo}.${sufixo}@carga.local`, senha: 'CargaK6senha1' };
  const resposta = http.post(
    `${GATEWAY}/api/v1/tenants`,
    JSON.stringify({
      razaoSocial: `Carga ${prefixo} ${sufixo} LTDA`,
      nomeFantasia: null,
      cnpj: gerarCnpj(),
      plano,
      administrador: { nome: `Admin ${prefixo}`, email: credenciais.usuario, senha: credenciais.senha },
    }),
    { headers: { 'Content-Type': 'application/json' }, tags: { fase: 'setup' } },
  );
  if (!check(resposta, { 'tenant cadastrado (201)': (r) => r.status === 201 })) {
    fail(`onboarding falhou: ${resposta.status} ${resposta.body}`);
  }
  return login(credenciais);
}

function pedirToken(parametros, usuario) {
  const resposta = http.post(KEYCLOAK_TOKEN, Object.assign({ client_id: CLIENT_ID }, parametros), { tags: { fase: 'auth' } });
  if (resposta.status !== 200) {
    fail(`token falhou para ${usuario}: ${resposta.status} ${resposta.body}`);
  }
  const corpo = resposta.json();
  return { usuario, access: corpo.access_token, refresh: corpo.refresh_token, expiraEm: Date.now() + corpo.expires_in * 1000 };
}

/**
 * Login com senha — uma única vez por usuário, no setup. Muitos logins simultâneos do mesmo usuário
 * disparam a proteção contra força bruta do Keycloak (e não representam o uso real).
 */
export function login(credenciais) {
  return pedirToken({ grant_type: 'password', username: credenciais.usuario, password: credenciais.senha }, credenciais.usuario);
}

// Sessão por VU (cada VU é um interpretador isolado): parte do token do setup e renova com o
// refresh token 30 s antes de expirar, como a SPA faz.
const sessoes = {};

export function token(sessao) {
  let atual = sessoes[sessao.usuario] || sessao;
  if (atual.expiraEm - Date.now() < 30_000) {
    atual = pedirToken({ grant_type: 'refresh_token', refresh_token: atual.refresh }, sessao.usuario);
  }
  sessoes[sessao.usuario] = atual;
  return atual.access;
}

export function cabecalhos(sessao, extras = {}) {
  return Object.assign(
    { Authorization: `Bearer ${token(sessao)}`, 'Content-Type': 'application/json' },
    extras,
  );
}

/** AAAA-MM-DD de hoje em São Paulo, deslocado em `dias`. */
export function dataIso(dias = 0) {
  const agora = new Date(Date.now() - 3 * 3600_000 + dias * 86_400_000);
  return agora.toISOString().slice(0, 10);
}

/** Data passada aleatória (2020–2024), exclusiva da execução — isola a verificação de convergência. */
export function dataPassadaExclusiva() {
  const inicio = Date.UTC(2020, 0, 1);
  const fim = Date.UTC(2024, 11, 31);
  return new Date(inicio + Math.random() * (fim - inicio)).toISOString().slice(0, 10);
}

export function uuid() {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    return (c === 'x' ? r : (r & 0x3) | 0x8).toString(16);
  });
}

/** Resumo no console e em arquivos (resultados/<nome>.json e .txt). */
export function resumo(nome) {
  return (dados) => ({
    stdout: textSummary(dados, { indent: ' ', enableColors: true }),
    [`resultados/${nome}.json`]: JSON.stringify(dados, null, 2),
    [`resultados/${nome}.txt`]: textSummary(dados, { indent: ' ', enableColors: false }),
  });
}
