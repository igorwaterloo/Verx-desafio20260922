import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideLocalizacaoBr } from '../../core/config/localizacao';
import { vi } from 'vitest';

import { LancamentosApi } from '../../core/api/apis';
import { ErroApi } from '../../core/api/erro-api';
import { Lancamento } from '../../core/api/modelos';
import { AuthService } from '../../core/auth/auth';
import { hojeEmSaoPaulo } from '../../core/util/validadores';
import { LancamentosPage } from './lancamentos';

/** Aguarda as chamadas (promises) das APIs simuladas e a renderização resultante. */
async function estabilizar(fixture: ComponentFixture<unknown>): Promise<void> {
  await new Promise((resolver) => setTimeout(resolver));
  await fixture.whenStable();
}

const lancamento = (parcial: Partial<Lancamento> = {}): Lancamento => ({
  id: 'l1',
  tipo: 'Credito',
  valor: 100,
  dataCompetencia: hojeEmSaoPaulo(),
  descricao: 'Venda balcão',
  lancamentoOriginalId: null,
  estornado: false,
  criadoPor: 'u1',
  criadoEm: new Date().toISOString(),
  ...parcial,
});

interface Pagina {
  formulario: { patchValue(v: unknown): void };
  registrar(): Promise<void>;
}

describe('LancamentosPage', () => {
  let api: { listar: ReturnType<typeof vi.fn>; registrar: ReturnType<typeof vi.fn>; estornar: ReturnType<typeof vi.fn> };

  async function criar(admin: boolean, itens: Lancamento[] = []) {
    api = {
      listar: vi.fn().mockResolvedValue({ itens, numeroPagina: 1, tamanhoPagina: 20, total: itens.length }),
      registrar: vi.fn(),
      estornar: vi.fn(),
    };
    await TestBed.configureTestingModule({
      imports: [LancamentosPage],
      providers: [
        provideLocalizacaoBr(),
        { provide: LancamentosApi, useValue: api },
        { provide: AuthService, useValue: { admin: signal(admin) } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(LancamentosPage);
    fixture.autoDetectChanges();
    await estabilizar(fixture);
    return { fixture, pagina: fixture.componentInstance as unknown as Pagina, elemento: fixture.nativeElement as HTMLElement };
  }

  it('lista os lançamentos do dia', async () => {
    const { elemento } = await criar(false, [lancamento(), lancamento({ id: 'l2', tipo: 'Debito', valor: 30 })]);

    expect(api.listar).toHaveBeenCalledWith(hojeEmSaoPaulo(), 1, 20);
    expect(elemento.querySelectorAll('tr.mat-mdc-row')).toHaveLength(2);
  });

  it('reutiliza a chave de idempotência ao reenviar o mesmo formulário após falha', async () => {
    const { pagina } = await criar(false);
    api.registrar.mockRejectedValueOnce(new ErroApi(0, 'Sem conexão')).mockResolvedValue(lancamento());

    pagina.formulario.patchValue({ tipo: 'Credito', valor: 100, descricao: 'Venda balcão' });
    await pagina.registrar();
    await pagina.registrar();

    expect(api.registrar).toHaveBeenCalledTimes(2);
    const [primeira, segunda] = api.registrar.mock.calls.map((chamada) => chamada[1]);
    expect(segunda).toBe(primeira);
    expect(api.registrar.mock.calls[0][0]).toEqual({
      tipo: 'Credito',
      valor: 100,
      dataCompetencia: hojeEmSaoPaulo(),
      descricao: 'Venda balcão',
    });
  });

  it('usa uma nova chave para o próximo lançamento após o sucesso', async () => {
    const { pagina } = await criar(false);
    api.registrar.mockResolvedValue(lancamento());

    pagina.formulario.patchValue({ valor: 100, descricao: 'Primeiro' });
    await pagina.registrar();
    pagina.formulario.patchValue({ valor: 100, descricao: 'Segundo' });
    await pagina.registrar();

    const [primeira, segunda] = api.registrar.mock.calls.map((chamada) => chamada[1]);
    expect(segunda).not.toBe(primeira);
    expect(api.listar).toHaveBeenCalledTimes(3);
  });

  it('não envia formulário inválido', async () => {
    const { pagina } = await criar(false);

    pagina.formulario.patchValue({ valor: 0, descricao: 'x' });
    await pagina.registrar();

    expect(api.registrar).not.toHaveBeenCalled();
  });

  it('mostra o estorno só para administradores e só em lançamentos não estornados', async () => {
    const itens = [lancamento(), lancamento({ id: 'l2', estornado: true }), lancamento({ id: 'l3', lancamentoOriginalId: 'l2' })];

    const comoOperador = await criar(false, itens);
    expect(comoOperador.elemento.querySelectorAll('button[aria-label="Estornar lançamento"]')).toHaveLength(0);

    TestBed.resetTestingModule();
    const comoAdmin = await criar(true, itens);
    expect(comoAdmin.elemento.querySelectorAll('button[aria-label="Estornar lançamento"]')).toHaveLength(1);
  });

  it('mostra o erro da listagem', async () => {
    const { fixture, elemento } = await criar(false);
    api.listar.mockRejectedValue(new ErroApi(503, 'Serviço temporariamente indisponível.'));

    await (fixture.componentInstance as unknown as { trocarData(d: Date): Promise<void> }).trocarData(new Date());
    await estabilizar(fixture);

    expect(elemento.querySelector('.erro')?.textContent).toContain('Serviço temporariamente indisponível.');
  });
});
