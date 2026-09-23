import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';

import { TenantsApi } from '../../core/api/apis';
import { ErroApi } from '../../core/api/erro-api';
import { Papel, Plano, Tenant, UsuarioDoTenant } from '../../core/api/modelos';
import { AuthService } from '../../core/auth/auth';
import { senhaForteValidator } from '../../core/util/validadores';
import { confirmar } from '../../shared/confirmacao';
import { Notificacao } from '../../shared/notificacao';

/** Administração do tenant (papel admin — RN-10): dados da empresa, plano e usuários (RP-04 a RP-07). */
@Component({
  selector: 'app-empresa',
  imports: [
    ReactiveFormsModule, CurrencyPipe, DatePipe, MatButtonModule, MatCardModule, MatFormFieldModule,
    MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, MatTableModule,
  ],
  templateUrl: './empresa.html',
  styleUrl: './empresa.scss',
})
export class EmpresaPage implements OnInit {
  private readonly api = inject(TenantsApi);
  private readonly auth = inject(AuthService);
  private readonly notificacao = inject(Notificacao);
  private readonly dialog = inject(MatDialog);

  protected readonly tenant = signal<Tenant | null>(null);
  protected readonly planos = signal<Plano[]>([]);
  protected readonly usuarios = signal<UsuarioDoTenant[]>([]);
  protected readonly carregando = signal(false);
  protected readonly salvando = signal(false);
  protected readonly erro = signal<string | null>(null);
  protected readonly colunas = ['nome', 'email', 'situacao'];

  protected readonly limiteAtingido = computed(() => {
    const limite = this.tenant()?.plano.limiteUsuarios;
    return limite !== undefined && this.usuarios().length >= limite;
  });

  protected readonly formulario = inject(FormBuilder).nonNullable.group({
    nome: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email]],
    senha: ['', [Validators.required, senhaForteValidator]],
    papel: ['operador' as Papel, Validators.required],
  });

  async ngOnInit(): Promise<void> {
    this.carregando.set(true);
    try {
      const [tenant, planos, usuarios] = await Promise.all([this.api.atual(), this.api.planos(), this.api.usuarios()]);
      this.tenant.set(tenant);
      this.planos.set(planos);
      this.usuarios.set(usuarios);
    } catch (erro) {
      this.erro.set(erro instanceof ErroApi ? erro.mensagem : 'Não foi possível carregar os dados da empresa.');
    } finally {
      this.carregando.set(false);
    }
  }

  protected async alterarPlano(plano: Plano): Promise<void> {
    const confirmado = await confirmar(this.dialog, {
      titulo: `Mudar para o plano ${plano.nome}`,
      mensagem: `Limites: ${plano.limiteLancamentosMes} lançamentos/mês, ${plano.limiteUsuarios} usuários e ${plano.requisicoesPorSegundo} requisições/s.`,
      confirmar: 'Mudar de plano',
    });
    if (!confirmado) {
      return;
    }

    this.salvando.set(true);
    try {
      this.tenant.set(await this.api.alterarPlano(plano.codigo));
      await this.auth.renovarSessao();
      this.notificacao.sucesso(`Plano alterado para ${plano.nome}.`);
    } catch (erro) {
      this.notificacao.erro(erro);
    } finally {
      this.salvando.set(false);
    }
  }

  protected async adicionarUsuario(): Promise<void> {
    if (this.formulario.invalid) {
      this.formulario.markAllAsTouched();
      return;
    }

    this.salvando.set(true);
    try {
      const usuario = await this.api.adicionarUsuario(this.formulario.getRawValue());
      this.usuarios.update((lista) => [...lista, usuario]);
      this.formulario.reset({ nome: '', email: '', senha: '', papel: 'operador' });
      this.notificacao.sucesso(`Usuário ${usuario.email} adicionado.`);
    } catch (erro) {
      this.notificacao.erro(erro);
    } finally {
      this.salvando.set(false);
    }
  }
}
