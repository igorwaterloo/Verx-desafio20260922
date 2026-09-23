import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';

import { ConsolidadoApi } from '../../core/api/apis';
import { ErroApi } from '../../core/api/erro-api';
import { ConsolidadoPeriodo, SaldoDiario } from '../../core/api/modelos';
import { deIso, hojeEmSaoPaulo, paraIso, somarDias } from '../../core/util/validadores';
import { GraficoSaldo } from './grafico-saldo';

/** Mesmo limite do backend (ObterConsolidadoPeriodoValidator.DiasMaximo). */
export const DIAS_MAXIMOS_PERIODO = 93;

/**
 * Saldo diário consolidado e período. O consolidado é eventualmente consistente (RNF-03): um aviso
 * explica o atraso de alguns segundos. Com o serviço indisponível, a página mostra um banner e
 * lembra que os lançamentos continuam sendo aceitos (RNF-01).
 */
@Component({
  selector: 'app-consolidado',
  imports: [
    ReactiveFormsModule, CurrencyPipe, DatePipe, RouterLink, MatButtonModule, MatCardModule, MatDatepickerModule,
    MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatTableModule, GraficoSaldo,
  ],
  templateUrl: './consolidado.html',
  styleUrl: './consolidado.scss',
})
export class ConsolidadoPage implements OnInit {
  private readonly api = inject(ConsolidadoApi);

  protected readonly hoje = deIso(hojeEmSaoPaulo());
  protected readonly data = signal(hojeEmSaoPaulo());
  protected readonly inicio = signal(somarDias(hojeEmSaoPaulo(), -29));
  protected readonly fim = signal(hojeEmSaoPaulo());

  protected readonly saldoDia = signal<SaldoDiario | null>(null);
  protected readonly periodo = signal<ConsolidadoPeriodo | null>(null);
  protected readonly carregando = signal(false);
  protected readonly indisponivel = signal(false);
  protected readonly erro = signal<string | null>(null);
  protected readonly diasMaximos = DIAS_MAXIMOS_PERIODO;
  protected readonly intervalo = new FormGroup({
    inicio: new FormControl<Date | null>(deIso(this.inicio())),
    fim: new FormControl<Date | null>(deIso(this.fim())),
  });
  protected readonly colunas = ['data', 'creditos', 'debitos', 'saldo', 'quantidade'];

  /** Tabela do período do dia mais recente para o mais antigo, só com dias que tiveram movimento. */
  protected readonly diasComMovimento = computed(() =>
    (this.periodo()?.dias ?? []).filter((d) => d.quantidadeLancamentos > 0).reverse(),
  );

  ngOnInit(): Promise<void> {
    return this.atualizar();
  }

  protected async trocarData(data: Date | null): Promise<void> {
    if (data) {
      this.data.set(paraIso(data));
      await this.atualizar();
    }
  }

  protected async trocarPeriodo(): Promise<void> {
    const { inicio, fim } = this.intervalo.getRawValue();
    if (!inicio || !fim) {
      return;
    }
    this.inicio.set(paraIso(inicio));
    this.fim.set(paraIso(fim));
    await this.atualizar();
  }

  protected deIso(data: string): Date {
    return deIso(data);
  }

  async atualizar(): Promise<void> {
    const dias = deIso(this.fim()).getTime() - deIso(this.inicio()).getTime();
    if (Math.round(dias / 86_400_000) >= DIAS_MAXIMOS_PERIODO) {
      this.erro.set(`O período deve ter no máximo ${DIAS_MAXIMOS_PERIODO} dias.`);
      return;
    }

    this.carregando.set(true);
    this.erro.set(null);
    try {
      const [saldo, periodo] = await Promise.all([
        this.api.dia(this.data()),
        this.api.periodo(this.inicio(), this.fim()),
      ]);
      this.saldoDia.set(saldo);
      this.periodo.set(periodo);
      this.indisponivel.set(false);
    } catch (erro) {
      const erroApi = erro instanceof ErroApi ? erro : null;
      this.indisponivel.set(erroApi?.indisponivel ?? false);
      if (!erroApi?.indisponivel) {
        this.erro.set(erroApi?.mensagem ?? 'Não foi possível carregar o consolidado.');
      }
    } finally {
      this.carregando.set(false);
    }
  }
}
