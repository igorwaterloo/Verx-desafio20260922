// RNF-02 / SLO-03..06: o Consolidado atende 50 req/s com no máximo 5% de perda (meta interna: < 1%)
// e p95 < 200 ms. Depois do regime sustentado, um pico de 100 req/s distribuído entre dois tenants Pro
// (o limite do gateway é por tenant: 100 req/s no Pro).
//
// Variáveis: DURACAO (padrão 5m), DURACAO_PICO (padrão 1m), GATEWAY_URL, KEYCLOAK_URL.
import http from 'k6/http';
import { check } from 'k6';
import execucao from 'k6/execution';
import { cabecalhos, criarTenant, dataIso, GATEWAY, resumo } from './lib/comum.js';
import { registrar } from './lib/lancamentos.js';

const DURACAO = __ENV.DURACAO || '5m';
const DURACAO_PICO = __ENV.DURACAO_PICO || '1m';

export const options = {
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
  scenarios: {
    sustentado: {
      executor: 'constant-arrival-rate',
      rate: 50,
      timeUnit: '1s',
      duration: DURACAO,
      preAllocatedVUs: 20,
      maxVUs: 100,
      exec: 'consultar',
      tags: { cenario: 'sustentado' },
    },
    pico: {
      executor: 'constant-arrival-rate',
      rate: 100,
      timeUnit: '1s',
      duration: DURACAO_PICO,
      startTime: DURACAO,
      preAllocatedVUs: 40,
      maxVUs: 200,
      exec: 'consultar',
      tags: { cenario: 'pico' },
    },
  },
  thresholds: {
    // Requisito do desafio (≤ 5% de perda) e SLO de latência com cache.
    'http_req_failed{api:consolidado,cenario:sustentado}': ['rate<0.05'],
    'http_req_duration{api:consolidado,cenario:sustentado}': ['p(95)<200', 'p(99)<500'],
    'http_req_failed{api:consolidado,cenario:pico}': ['rate<0.05'],
    'http_req_duration{api:consolidado,cenario:pico}': ['p(95)<200'],
    // Meta interna, mais rígida (SLO-04).
    'checks{cenario:sustentado}': ['rate>0.99'],
    // Iterações não executadas por falta de VUs (respostas lentas) também são perda: < 1% de 50 req/s.
    dropped_iterations: ['rate<0.5'],
  },
};

export function setup() {
  const tenants = [criarTenant('consolidado-a', 'pro'), criarTenant('consolidado-b', 'pro')];
  // Massa de dados: 30 dias com lançamentos, para as consultas de dia e de período terem conteúdo.
  for (const tenant of tenants) {
    for (let dia = 0; dia < 30; dia++) {
      registrar(tenant, { tipo: 'Credito', valor: 100 + dia, dataCompetencia: dataIso(-dia), descricao: 'Massa de carga' }, { fase: 'setup' });
      registrar(tenant, { tipo: 'Debito', valor: 40, dataCompetencia: dataIso(-dia), descricao: 'Massa de carga' }, { fase: 'setup' });
    }
  }
  return { tenants };
}

export function consultar(dados) {
  // Sustentado: um único tenant a 50 req/s. Pico: 100 req/s alternando entre os dois tenants (50 cada).
  const tenant = execucao.scenario.name === 'pico' ? dados.tenants[execucao.scenario.iterationInTest % 2] : dados.tenants[0];
  const sorteio = Math.random();
  let url;
  let nome;
  if (sorteio < 0.7) {
    url = `${GATEWAY}/api/v1/consolidado/${dataIso(-Math.floor(Math.random() * 30))}`;
    nome = 'GET /consolidado/{data}';
  } else {
    const dias = sorteio < 0.9 ? 7 : 30;
    url = `${GATEWAY}/api/v1/consolidado?inicio=${dataIso(-dias + 1)}&fim=${dataIso(0)}`;
    nome = 'GET /consolidado?inicio&fim';
  }

  const resposta = http.get(url, { headers: cabecalhos(tenant), tags: { api: 'consolidado', name: nome } });
  check(resposta, { 'consolidado 200': (r) => r.status === 200 });
}

export const handleSummary = resumo('consolidado-50rps');
