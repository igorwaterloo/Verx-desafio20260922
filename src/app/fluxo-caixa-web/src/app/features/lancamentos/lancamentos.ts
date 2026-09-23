import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';

import { LancamentosApi } from '../../core/api/apis';
import { ErroApi } from '../../core/api/erro-api';
import { Lancamento, Pagina, TipoLancamento } from '../../core/api/modelos';
import { AuthService } from '../../core/auth/auth';
import { deIso, hojeEmSaoPaulo, novaChaveIdempotencia, paraIso } from '../../core/util/validadores';
import { confirmar } from '../../shared/confirmacao';
import { Notificacao } from '../../shared/notificacao';

/**
 * Lançamentos do dia (RN-01 a RN-10). O registro não depende do Consolidado (RNF-01): esta página
 * continua funcionando mesmo com o serviço de consolidado fora do ar.
 */
@Component({
  selector: 'app-lancamentos',
  imports: [
    ReactiveFormsModule, CurrencyPipe, DatePipe, MatButtonModule, MatButtonToggleModule, MatCardModule,
    MatDatepickerModule, MatFormFieldModule, MatIconModule, MatInputModule, MatPaginatorModule,
    MatProgressBarModule, MatTableModule, MatTooltipModule,
  ],
  templateUrl: './lancamentos.html',
  styleUrl: './lancamentos.scss',
})
export class LancamentosPage implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly api = inject(LancamentosApi);
  private readonly notificacao = inject(Notificacao);
  private readonly dialog = inject(MatDialog);

  protected readonly hoje = deIso(hojeEmSaoPaulo());
  protected readonly data = signal(hojeEmSaoPaulo());
  protected readonly pagina = signal<Pagina<Lancamento> | null>(null);
  protected readonly carregando = signal(false);
  protected readonly salvando = signal(false);
  protected readonly erroLista = signal<string | null>(null);
  protected readonly colunas = ['tipo', 'descricao', 'valor', 'criadoEm', 'situacao', 'acoes'];

  /** Reutilizada em reenvios do mesmo formulário; renovada quando os dados mudam ou após o sucesso (RN-08). */
  private chaveIdempotencia = novaChaveIdempotencia();

  protected readonly formulario = inject(FormBuilder).nonNullable.group({
    tipo: ['Credito' as TipoLancamento, Validators.required],
    valor: [null as number | null, [Validators.required, Validators.min(0.01), Validators.max(999_999_999.99)]],
    dataCompetencia: [this.hoje, Validators.required],
    descricao: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(200)]],
  });

  constructor() {
    this.formulario.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this.chaveIdempotencia = novaChaveIdempotencia();
    });
  }

  ngOnInit(): Promise<void> {
    return this.carregar();
  }

  protected async trocarData(data: Date | null): Promise<void> {
    if (data) {
      this.data.set(paraIso(data));
      await this.carregar(1);
    }
  }

  protected async trocarPagina(evento: PageEvent): Promise<void> {
    await this.carregar(evento.pageIndex + 1, evento.pageSize);
  }

  protected async registrar(): Promise<void> {
    if (this.formulario.invalid) {
      this.formulario.markAllAsTouched();
      return;
    }

    const valor = this.formulario.getRawValue();
    this.salvando.set(true);
    try {
      const lancamento = await this.api.registrar(
        {
          tipo: valor.tipo,
          valor: Number(valor.valor),
          dataCompetencia: paraIso(valor.dataCompetencia),
          descricao: valor.descricao.trim(),
        },
        this.chaveIdempotencia,
      );
      this.notificacao.sucesso(`${lancamento.tipo === 'Credito' ? 'Crédito' : 'Débito'} registrado.`);
      this.formulario.reset({ tipo: valor.tipo, valor: null, dataCompetencia: valor.dataCompetencia, descricao: '' });
      this.chaveIdempotencia = novaChaveIdempotencia();
      if (lancamento.dataCompetencia === this.data()) {
        await this.carregar(this.pagina()?.numeroPagina ?? 1);
      }
    } catch (erro) {
      this.notificacao.erro(erro);
    } finally {
      this.salvando.set(false);
    }
  }

  protected async estornar(lancamento: Lancamento): Promise<void> {
    const confirmado = await confirmar(this.dialog, {
      titulo: 'Estornar lançamento',
      mensagem: `Será registrado um ${lancamento.tipo === 'Credito' ? 'débito' : 'crédito'} de mesmo valor na mesma data. Lançamentos não podem ser editados nem excluídos.`,
      confirmar: 'Estornar',
    });
    if (!confirmado) {
      return;
    }

    try {
      await this.api.estornar(lancamento.id);
      this.notificacao.sucesso('Estorno registrado.');
      await this.carregar(this.pagina()?.numeroPagina ?? 1);
    } catch (erro) {
      this.notificacao.erro(erro);
    }
  }

  protected podeEstornar(lancamento: Lancamento): boolean {
    return this.auth.admin() && !lancamento.estornado && !lancamento.lancamentoOriginalId;
  }

  private async carregar(pagina = 1, tamanho = this.pagina()?.tamanhoPagina ?? 20): Promise<void> {
    this.carregando.set(true);
    this.erroLista.set(null);
    try {
      this.pagina.set(await this.api.listar(this.data(), pagina, tamanho));
    } catch (erro) {
      this.erroLista.set(erro instanceof ErroApi ? erro.mensagem : 'Não foi possível carregar os lançamentos.');
    } finally {
      this.carregando.set(false);
    }
  }
}
