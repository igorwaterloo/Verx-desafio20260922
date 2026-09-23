import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

import { ErroApi, erroApiInterceptor } from './erro-api';

describe('erroApiInterceptor', () => {
  let http: HttpClient;
  let controlador: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([erroApiInterceptor])), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpClient);
    controlador = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controlador.verify());

  async function falhar(status: number, corpo: object | null = null, headers: Record<string, string> = {}): Promise<ErroApi> {
    const resposta = firstValueFrom(http.get('/api/teste'));
    controlador.expectOne('/api/teste').flush(corpo, { status, statusText: 'erro', headers });
    return resposta.then(
      () => {
        throw new Error('a requisição deveria falhar');
      },
      (erro: unknown) => erro as ErroApi,
    );
  }

  it('converte o ProblemDetails de validação em mensagens por campo', async () => {
    const erro = await falhar(400, {
      title: 'Dados inválidos',
      codigo: 'validacao',
      errors: { Valor: ['O valor deve ser maior que zero.'], Descricao: ['A descrição é obrigatória.'] },
    });

    expect(erro).toBeInstanceOf(ErroApi);
    expect(erro.status).toBe(400);
    expect(erro.codigo).toBe('validacao');
    expect(erro.mensagem).toBe('O valor deve ser maior que zero. A descrição é obrigatória.');
    expect(erro.indisponivel).toBe(false);
  });

  it('usa o título da regra de negócio (422)', async () => {
    const erro = await falhar(422, { title: 'Cota mensal de lançamentos do plano atingida.', codigo: 'lancamento.quota_excedida' });

    expect(erro.mensagem).toBe('Cota mensal de lançamentos do plano atingida.');
    expect(erro.codigo).toBe('lancamento.quota_excedida');
  });

  it('informa a espera do Retry-After no 429 do gateway', async () => {
    const erro = await falhar(429, null, { 'Retry-After': '3' });

    expect(erro.aguardarSegundos).toBe(3);
    expect(erro.mensagem).toContain('3 s');
  });

  it.each([502, 503, 504])('marca %i como indisponível', async (status) => {
    const erro = await falhar(status);

    expect(erro.indisponivel).toBe(true);
    expect(erro.mensagem).toBe('Serviço temporariamente indisponível.');
  });

  it('trata falha de rede (status 0) como indisponível', async () => {
    const resposta = firstValueFrom(http.get('/api/teste'));
    controlador.expectOne('/api/teste').error(new ProgressEvent('error'));

    const erro = (await resposta.catch((e: unknown) => e)) as ErroApi;
    expect(erro.status).toBe(0);
    expect(erro.indisponivel).toBe(true);
  });

  it('pede novo login no 401', async () => {
    const erro = await falhar(401);

    expect(erro.mensagem).toBe('Sua sessão expirou. Entre novamente.');
  });
});
