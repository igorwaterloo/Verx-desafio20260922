import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideLocalizacaoBr } from '../../core/config/localizacao';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';

import { ConsolidadoApi } from '../../core/api/apis';
import { ErroApi } from '../../core/api/erro-api';
import { SaldoDiario } from '../../core/api/modelos';
import { ConsolidadoPage } from './consolidado';

/** Aguarda as chamadas (promises) das APIs simuladas e a renderização resultante. */
async function estabilizar(fixture: ComponentFixture<unknown>): Promise<void> {
  await new Promise((resolver) => setTimeout(resolver));
  await fixture.whenStable();
}

const dia = (data: string, creditos: number, debitos: number): SaldoDiario => ({
  data,
  totalCreditos: creditos,
  totalDebitos: debitos,
  saldo: creditos - debitos,
  quantidadeLancamentos: creditos + debitos > 0 ? 2 : 0,
});

describe('ConsolidadoPage', () => {
  async function criar(api: Partial<Record<keyof ConsolidadoApi, unknown>>) {
    await TestBed.configureTestingModule({
      imports: [ConsolidadoPage],
      providers: [provideRouter([]), provideLocalizacaoBr(), { provide: ConsolidadoApi, useValue: api }],
    }).compileComponents();

    const fixture = TestBed.createComponent(ConsolidadoPage);
    fixture.autoDetectChanges();
    await estabilizar(fixture);
    return fixture.nativeElement as HTMLElement;
  }

  it('mostra o saldo do dia, o gráfico e só os dias com movimento na tabela', async () => {
    const elemento = await criar({
      dia: vi.fn().mockResolvedValue(dia('2026-09-02', 150, 40)),
      periodo: vi.fn().mockResolvedValue({
        inicio: '2026-09-01', fim: '2026-09-02', totalCreditos: 150, totalDebitos: 40, saldo: 110, quantidadeLancamentos: 2,
        dias: [dia('2026-09-01', 0, 0), dia('2026-09-02', 150, 40)],
      }),
    });

    expect(elemento.querySelector('[data-testid=saldo-dia]')?.textContent).toContain('110,00');
    expect(elemento.querySelector('app-grafico-saldo svg')).not.toBeNull();
    expect(elemento.querySelectorAll('tr.mat-mdc-row')).toHaveLength(1);
    expect(elemento.querySelector('.banner')).toBeNull();
  });

  it('com o consolidado fora do ar, avisa que os lançamentos continuam funcionando (RNF-01)', async () => {
    const indisponivel = new ErroApi(503, 'Serviço temporariamente indisponível.');
    const elemento = await criar({
      dia: vi.fn().mockRejectedValue(indisponivel),
      periodo: vi.fn().mockRejectedValue(indisponivel),
    });

    const banner = elemento.querySelector('.banner');
    expect(banner?.textContent).toContain('temporariamente indisponível');
    expect(banner?.textContent).toContain('lançamentos continuam sendo registrados');
    expect(banner?.querySelector('a')?.getAttribute('href')).toBe('/app/lancamentos');
  });

  it('mostra erros que não são de indisponibilidade sem o banner', async () => {
    const elemento = await criar({
      dia: vi.fn().mockRejectedValue(new ErroApi(400, 'Dados inválidos', undefined, { Data: ['Data inválida.'] })),
      periodo: vi.fn().mockResolvedValue({ dias: [] }),
    });

    expect(elemento.querySelector('.banner')).toBeNull();
    expect(elemento.querySelector('.erro')?.textContent).toContain('Data inválida.');
  });
});
