// Operações de lançamentos e verificação de convergência do consolidado.
import http from 'k6/http';
import { sleep } from 'k6';
import { cabecalhos, GATEWAY, uuid } from './comum.js';

export function registrar(sessao, lancamento, tags = {}, chave = uuid()) {
  return http.post(`${GATEWAY}/api/v1/lancamentos`, JSON.stringify(lancamento), {
    headers: cabecalhos(sessao, { 'Idempotency-Key': chave }),
    tags: Object.assign({ api: 'lancamentos', name: 'POST /lancamentos' }, tags),
  });
}

/** Soma os lançamentos de uma data pela API de Lançamentos (fonte da verdade), página a página. */
export function somarLancamentos(sessao, data) {
  let pagina = 1;
  let quantidade = 0;
  let saldo = 0;
  for (;;) {
    const resposta = http.get(`${GATEWAY}/api/v1/lancamentos?data=${data}&pagina=${pagina}&tamanhoPagina=100`, {
      headers: cabecalhos(sessao),
      tags: { fase: 'verificacao' },
    });
    if (resposta.status === 429) {
      sleep(1);
      continue;
    }
    const corpo = resposta.json();
    for (const l of corpo.itens) {
      quantidade++;
      saldo += l.tipo === 'Credito' ? l.valor : -l.valor;
    }
    if (pagina * corpo.tamanhoPagina >= corpo.total) break;
    pagina++;
  }
  return { quantidade, saldo: Math.round(saldo * 100) / 100 };
}

/**
 * Aguarda o consolidado da data refletir todos os lançamentos (consistência eventual).
 * Retorna o resultado e quanto tempo levou para convergir depois do fim da carga.
 */
export function aguardarConvergencia(sessao, data, limiteSegundos = 120) {
  const esperado = somarLancamentos(sessao, data);
  const inicio = Date.now();
  let obtido = null;
  while ((Date.now() - inicio) / 1000 < limiteSegundos) {
    const resposta = http.get(`${GATEWAY}/api/v1/consolidado/${data}`, {
      headers: cabecalhos(sessao),
      tags: { fase: 'verificacao' },
    });
    if (resposta.status === 200) {
      obtido = resposta.json();
      if (obtido.quantidadeLancamentos === esperado.quantidade && Math.abs(obtido.saldo - esperado.saldo) < 0.005) {
        return { convergiu: true, segundos: (Date.now() - inicio) / 1000, esperado, obtido };
      }
    }
    sleep(1);
  }
  return { convergiu: false, segundos: limiteSegundos, esperado, obtido };
}
