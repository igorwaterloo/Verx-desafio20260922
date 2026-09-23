// SLO-02 / RNF-01 (caos): carga constante de lançamentos enquanto o script externo
// (scripts/caos.sh ou .ps1) derruba o Consolidado — ou o RabbitMQ — no meio da execução.
// Os lançamentos não podem falhar, e o saldo tem que convergir depois que tudo volta.
//
// Variáveis: TAXA (padrão 20 req/s), DURACAO (padrão 3m), GATEWAY_URL, KEYCLOAK_URL.
import { check } from 'k6';
import { Trend } from 'k6/metrics';
import { criarTenant, dataPassadaExclusiva, resumo } from './lib/comum.js';
import { aguardarConvergencia, registrar } from './lib/lancamentos.js';

const TAXA = Number(__ENV.TAXA || 20);
const DURACAO = __ENV.DURACAO || '3m';

const convergencia = new Trend('convergencia_consolidado_segundos');

export const options = {
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
  scenarios: {
    escrita: {
      executor: 'constant-arrival-rate',
      rate: TAXA,
      timeUnit: '1s',
      duration: DURACAO,
      preAllocatedVUs: 10,
      maxVUs: 60,
      exec: 'escrever',
    },
  },
  thresholds: {
    // Zero perda de lançamentos com o Consolidado (ou o broker) fora do ar.
    'http_req_failed{api:lancamentos}': ['rate==0'],
    'http_req_duration{api:lancamentos}': ['p(95)<300'],
    'checks{verificacao:convergencia}': ['rate==1'],
  },
  teardownTimeout: '4m',
};

export function setup() {
  return { tenant: criarTenant('caos', 'pro'), data: dataPassadaExclusiva() };
}

export function escrever(dados) {
  const credito = Math.random() < 0.6;
  const resposta = registrar(dados.tenant, {
    tipo: credito ? 'Credito' : 'Debito',
    valor: Math.round((1 + Math.random() * 499) * 100) / 100,
    dataCompetencia: dados.data,
    descricao: 'Carga k6 durante falha',
  });
  check(resposta, { 'lançamento 201': (r) => r.status === 201 });
}

export function teardown(dados) {
  // Após religar, o worker drena a fila acumulada durante a queda.
  const resultado = aguardarConvergencia(dados.tenant, dados.data, 180);
  convergencia.add(resultado.segundos);
  check(resultado, { 'saldo converge após a recuperação': (r) => r.convergiu }, { verificacao: 'convergencia' });
  console.log(`convergência: ${JSON.stringify(resultado)}`);
}

export const handleSummary = resumo('caos-lancamentos');
