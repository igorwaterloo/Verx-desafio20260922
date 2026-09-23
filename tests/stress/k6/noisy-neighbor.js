// SLO-10 / RNF-04 (vizinho barulhento): um tenant Free enviando 3x o seu limite (60 req/s contra 20)
// recebe 429, enquanto um tenant Pro consultando a 50 req/s ao mesmo tempo não é afetado.
//
// Variáveis: DURACAO (padrão 2m), GATEWAY_URL, KEYCLOAK_URL.
import http from 'k6/http';
import { check } from 'k6';
import { Rate } from 'k6/metrics';
import { cabecalhos, criarTenant, dataIso, GATEWAY, resumo } from './lib/comum.js';

const DURACAO = __ENV.DURACAO || '2m';

const barulhentoLimitado = new Rate('barulhento_429');

// Para o barulhento, 429 é a resposta esperada: não conta como falha de requisição.
const esperadoBarulhento = http.expectedStatuses(200, 429);

export const options = {
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
  scenarios: {
    vitima: {
      executor: 'constant-arrival-rate',
      rate: 50,
      timeUnit: '1s',
      duration: DURACAO,
      preAllocatedVUs: 20,
      maxVUs: 100,
      exec: 'vitima',
      tags: { tenant: 'vitima' },
    },
    barulhento: {
      executor: 'constant-arrival-rate',
      rate: 60,
      timeUnit: '1s',
      duration: DURACAO,
      preAllocatedVUs: 20,
      maxVUs: 100,
      exec: 'barulhento',
      tags: { tenant: 'barulhento' },
    },
  },
  thresholds: {
    // A vítima não percebe o vizinho.
    'http_req_failed{tenant:vitima,api:consolidado}': ['rate<0.01'],
    'http_req_duration{tenant:vitima,api:consolidado}': ['p(95)<200'],
    // O barulhento é contido: ~2/3 das requisições acima do limite recebem 429, nenhuma falha de outro tipo.
    barulhento_429: ['rate>0.5'],
    'http_req_failed{tenant:barulhento,api:consolidado}': ['rate<0.01'],
  },
};

export function setup() {
  return { vitima: criarTenant('vitima', 'pro'), barulhento: criarTenant('barulhento', 'free') };
}

function consultar(sessao, parametros) {
  return http.get(
    `${GATEWAY}/api/v1/consolidado/${dataIso(-Math.floor(Math.random() * 7))}`,
    Object.assign({ headers: cabecalhos(sessao), tags: { api: 'consolidado', name: 'GET /consolidado/{data}' } }, parametros),
  );
}

export function vitima(dados) {
  check(consultar(dados.vitima), { 'vítima 200': (r) => r.status === 200 });
}

export function barulhento(dados) {
  const resposta = consultar(dados.barulhento, { responseCallback: esperadoBarulhento });
  barulhentoLimitado.add(resposta.status === 429);
  check(resposta, {
    'barulhento 200 ou 429': (r) => r.status === 200 || r.status === 429,
    '429 com Retry-After': (r) => r.status !== 429 || r.headers['Retry-After'] !== undefined,
  });
}

export const handleSummary = resumo('noisy-neighbor');
