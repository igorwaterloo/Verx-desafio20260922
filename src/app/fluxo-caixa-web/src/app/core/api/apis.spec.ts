import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { CONFIGURACAO } from '../config/configuracao';
import { ConsolidadoApi, LancamentosApi, TenantsApi } from './apis';

describe('clientes das APIs', () => {
  let controlador: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: CONFIGURACAO, useValue: { apiUrl: 'http://gateway/', oidc: { authority: '', clientId: '' } } },
      ],
    });
    controlador = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controlador.verify());

  it('registra o lançamento com o cabeçalho Idempotency-Key', async () => {
    const api = TestBed.inject(LancamentosApi);
    const novo = { tipo: 'Credito' as const, valor: 10, dataCompetencia: '2026-09-01', descricao: 'Venda' };

    const resposta = api.registrar(novo, 'chave-1');
    const requisicao = controlador.expectOne('http://gateway/api/v1/lancamentos');
    expect(requisicao.request.method).toBe('POST');
    expect(requisicao.request.headers.get('Idempotency-Key')).toBe('chave-1');
    expect(requisicao.request.body).toEqual(novo);
    requisicao.flush({ id: '1', ...novo });

    await expect(resposta).resolves.toMatchObject({ id: '1' });
  });

  it('lista os lançamentos do dia com paginação', async () => {
    const resposta = TestBed.inject(LancamentosApi).listar('2026-09-01', 2, 10);

    const requisicao = controlador.expectOne((r) => r.url === 'http://gateway/api/v1/lancamentos');
    expect(requisicao.request.params.get('data')).toBe('2026-09-01');
    expect(requisicao.request.params.get('pagina')).toBe('2');
    expect(requisicao.request.params.get('tamanhoPagina')).toBe('10');
    requisicao.flush({ itens: [], numeroPagina: 2, tamanhoPagina: 10, total: 0 });

    await expect(resposta).resolves.toMatchObject({ numeroPagina: 2 });
  });

  it('estorna pelo endpoint de estorno', async () => {
    const resposta = TestBed.inject(LancamentosApi).estornar('abc');

    const requisicao = controlador.expectOne('http://gateway/api/v1/lancamentos/abc/estorno');
    expect(requisicao.request.method).toBe('POST');
    requisicao.flush({ id: 'def' });
    await resposta;
  });

  it('consulta o consolidado do dia e do período', async () => {
    const api = TestBed.inject(ConsolidadoApi);

    const dia = api.dia('2026-09-01');
    controlador.expectOne('http://gateway/api/v1/consolidado/2026-09-01').flush({ saldo: 1 });
    await dia;

    const periodo = api.periodo('2026-09-01', '2026-09-30');
    const requisicao = controlador.expectOne((r) => r.url === 'http://gateway/api/v1/consolidado');
    expect(requisicao.request.params.get('inicio')).toBe('2026-09-01');
    expect(requisicao.request.params.get('fim')).toBe('2026-09-30');
    requisicao.flush({ dias: [] });
    await periodo;
  });

  it('altera o plano do tenant atual', async () => {
    const resposta = TestBed.inject(TenantsApi).alterarPlano('pro');

    const requisicao = controlador.expectOne('http://gateway/api/v1/tenants/atual/plano');
    expect(requisicao.request.method).toBe('PUT');
    expect(requisicao.request.body).toEqual({ plano: 'pro' });
    requisicao.flush({});
    await resposta;
  });
});
