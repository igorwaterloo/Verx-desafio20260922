import { CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { SaldoDiario } from '../../core/api/modelos';

const LARGURA = 720;
const ALTURA = 220;
const MARGEM = { topo: 12, base: 24, esquerda: 8, direita: 8 };

interface Barra {
  x: number;
  largura: number;
  yCredito: number;
  alturaCredito: number;
  yDebito: number;
  alturaDebito: number;
  dia: SaldoDiario;
}

/**
 * Gráfico de barras em SVG puro (créditos acima, débitos abaixo do eixo) com a linha do saldo diário.
 * Sem biblioteca de gráficos: mantém o bundle pequeno e compatível com a CSP estrita do nginx.
 */
@Component({
  selector: 'app-grafico-saldo',
  imports: [CurrencyPipe, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg [attr.viewBox]="'0 0 ' + largura + ' ' + altura" role="img" [attr.aria-label]="descricao()">
      <line class="eixo" [attr.x1]="margem.esquerda" [attr.x2]="largura - margem.direita" [attr.y1]="zero()" [attr.y2]="zero()" />
      @for (barra of barras(); track barra.dia.data) {
        <g>
          <title>{{ barra.dia.data | date: 'dd/MM' }} — créditos {{ barra.dia.totalCreditos | currency }}, débitos {{ barra.dia.totalDebitos | currency }}, saldo {{ barra.dia.saldo | currency }}</title>
          <rect class="credito" [attr.x]="barra.x" [attr.y]="barra.yCredito" [attr.width]="barra.largura" [attr.height]="barra.alturaCredito" />
          <rect class="debito" [attr.x]="barra.x" [attr.y]="barra.yDebito" [attr.width]="barra.largura" [attr.height]="barra.alturaDebito" />
        </g>
      }
      <polyline class="saldo" [attr.points]="linhaSaldo()" />
      @for (rotulo of rotulos(); track rotulo.texto) {
        <text class="rotulo" [attr.x]="rotulo.x" [attr.y]="altura - 6">{{ rotulo.texto }}</text>
      }
    </svg>
    <div class="legenda">
      <span><i class="credito"></i> Créditos</span>
      <span><i class="debito"></i> Débitos</span>
      <span><i class="saldo"></i> Saldo do dia</span>
    </div>
  `,
  styles: `
    :host { display: block; }
    svg { width: 100%; height: auto; }
    .eixo { stroke: var(--mat-sys-outline); stroke-width: 1; }
    rect.credito, i.credito { fill: #4caf50; background: #4caf50; }
    rect.debito, i.debito { fill: #e57373; background: #e57373; }
    .saldo { fill: none; stroke: var(--mat-sys-primary); stroke-width: 2; }
    i.saldo { background: var(--mat-sys-primary); height: 3px !important; }
    .rotulo { font-size: 11px; fill: var(--mat-sys-on-surface-variant); text-anchor: middle; }
    .legenda { display: flex; gap: 16px; font-size: 12px; justify-content: center; }
    .legenda span { display: inline-flex; align-items: center; gap: 6px; }
    .legenda i { display: inline-block; width: 12px; height: 12px; border-radius: 2px; }
  `,
})
export class GraficoSaldo {
  readonly dias = input.required<SaldoDiario[]>();

  protected readonly largura = LARGURA;
  protected readonly altura = ALTURA;
  protected readonly margem = MARGEM;

  private readonly escala = computed(() => {
    const dias = this.dias();
    const maximo = Math.max(1, ...dias.map((d) => d.totalCreditos), ...dias.map((d) => Math.abs(d.saldo)));
    const minimo = Math.max(1, ...dias.map((d) => d.totalDebitos), ...dias.map((d) => Math.abs(Math.min(0, d.saldo))));
    const util = ALTURA - MARGEM.topo - MARGEM.base;
    const porUnidade = util / (maximo + minimo);
    return { porUnidade, zero: MARGEM.topo + maximo * porUnidade };
  });

  protected readonly zero = computed(() => this.escala().zero);

  protected readonly barras = computed<Barra[]>(() => {
    const dias = this.dias();
    const { porUnidade, zero } = this.escala();
    const passo = (LARGURA - MARGEM.esquerda - MARGEM.direita) / Math.max(1, dias.length);
    const largura = Math.max(1, passo * 0.7);
    return dias.map((dia, i) => {
      const alturaCredito = dia.totalCreditos * porUnidade;
      return {
        x: MARGEM.esquerda + i * passo + (passo - largura) / 2,
        largura,
        yCredito: zero - alturaCredito,
        alturaCredito,
        yDebito: zero,
        alturaDebito: dia.totalDebitos * porUnidade,
        dia,
      };
    });
  });

  protected readonly linhaSaldo = computed(() => {
    const { porUnidade, zero } = this.escala();
    return this.barras()
      .map((b) => `${(b.x + b.largura / 2).toFixed(1)},${(zero - b.dia.saldo * porUnidade).toFixed(1)}`)
      .join(' ');
  });

  /** No máximo ~8 rótulos de data no eixo, para não sobrepor em períodos longos. */
  protected readonly rotulos = computed(() => {
    const barras = this.barras();
    const intervalo = Math.max(1, Math.ceil(barras.length / 8));
    return barras
      .filter((_, i) => i % intervalo === 0)
      .map((b) => ({ x: b.x + b.largura / 2, texto: `${b.dia.data.slice(8, 10)}/${b.dia.data.slice(5, 7)}` }));
  });

  protected readonly descricao = computed(() => `Gráfico de créditos, débitos e saldo de ${this.dias().length} dias`);
}
