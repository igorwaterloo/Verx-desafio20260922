// SLO-01/SLO-07: escrita concorrente de lançamentos (p95 < 300 ms, sem erros), idempotência sob carga
// e convergência do consolidado ao final (consistência eventual, p95 < 5 s no regime normal).
//
// Os lançamentos usam uma data de competência passada exclusiva da execução: o teardown compara
// exatamente o que a API de Lançamentos registrou com o que o Consolidado somou.
//
// Variáveis: TAXA_MAXIMA (padrão 50 req/s), DURACAO (padrão 3m), GATEWAY_URL, KEYCLOAK_URL.
import { check } from 'k6';
import { Counter, Trend } from 'k6/metrics';
import { criarTenant, dataPassadaExclusiva, resumo, uuid } from './lib/comum.js';
import { aguardarConvergencia, registrar } from './lib/lancamentos.js';

const TAXA_MAXIMA = Number(__ENV.TAXA_MAXIMA || 50);
const DURACAO = __ENV.DURACAO || '3m';

const duplicadosDevolvidos = new Counter('idempotencia_reenvios_ok');
const convergencia = new Trend('convergencia_consolidado_segundos');

export const options = {
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
  scenarios: {
    escrita: {
      executor: 'ramping-arrival-rate',
      startRate: 5,
      timeUnit: '1s',
      preAllocatedVUs: 20,
      maxVUs: 150,
      stages: [
        { target: TAXA_MAXIMA, duration: '30s' },
        { target: TAXA_MAXIMA, duration: DURACAO },
        { target: 0, duration: '10s' },
      ],
      exec: 'escrever',
    },
  },
  thresholds: {
    'http_req_failed{api:lancamentos}': ['rate<0.01'],
    'http_req_duration{api:lancamentos}': ['p(95)<300'],
    'checks{api:lancamentos}': ['rate>0.99'],
    // SLO-07: o consolidado reflete a carga em menos de 5 s depois que ela termina.
    convergencia_consolidado_segundos: ['max<5'],
  },
  teardownTimeout: '3m',
};

export function setup() {
  return { tenant: criarTenant('lancamentos', 'pro'), data: dataPassadaExclusiva() };
}

export function escrever(dados) {
  const credito = Math.random() < 0.6;
  const lancamento = {
    tipo: credito ? 'Credito' : 'Debito',
    valor: Math.round((1 + Math.random() * 999) * 100) / 100,
    dataCompetencia: dados.data,
    descricao: `Carga k6 ${credito ? 'venda' : 'compra'}`,
  };
  const chave = uuid();
  const resposta = registrar(dados.tenant, lancamento, {}, chave);
  check(resposta, { 'lançamento 201': (r) => r.status === 201 }, { api: 'lancamentos' });

  // 5% das iterações reenviam a mesma requisição (ex.: timeout no cliente): deve devolver o mesmo lançamento.
  if (resposta.status === 201 && Math.random() < 0.05) {
    const reenvio = registrar(dados.tenant, lancamento, { name: 'POST /lancamentos (reenvio)' }, chave);
    const mesmo = check(
      reenvio,
      { 'reenvio devolve o mesmo lançamento': (r) => r.status < 300 && r.json('id') === resposta.json('id') },
      { api: 'lancamentos' },
    );
    if (mesmo) duplicadosDevolvidos.add(1);
  }
}

export function teardown(dados) {
  const resultado = aguardarConvergencia(dados.tenant, dados.data);
  convergencia.add(resultado.segundos);
  check(resultado, { 'consolidado converge com os lançamentos': (r) => r.convergiu });
  console.log(`convergência: ${JSON.stringify(resultado)}`);
}

export const handleSummary = resumo('lancamentos-carga');
