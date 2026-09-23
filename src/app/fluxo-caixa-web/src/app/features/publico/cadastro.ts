import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';

import { TenantsApi } from '../../core/api/apis';
import { ErroApi } from '../../core/api/erro-api';
import { Plano, Tenant } from '../../core/api/modelos';
import { AuthService } from '../../core/auth/auth';
import { cnpjValidator, senhaForteValidator } from '../../core/util/validadores';

/**
 * Cadastro da empresa em autoatendimento (onboarding — ADR-0017). Com o provedor de identidade
 * indisponível (503), o cadastro fica pendente e o reenvio do mesmo formulário o conclui.
 */
@Component({
  selector: 'app-cadastro',
  imports: [
    ReactiveFormsModule, RouterLink, MatButtonModule, MatCardModule, MatFormFieldModule,
    MatIconModule, MatInputModule, MatSelectModule, MatProgressBarModule,
  ],
  template: `
    <mat-card appearance="outlined">
      @if (enviando()) {
        <mat-progress-bar mode="indeterminate" />
      }
      <mat-card-header>
        <mat-card-title>Cadastrar minha empresa</mat-card-title>
        <mat-card-subtitle>Crie o acesso da sua empresa e do primeiro administrador.</mat-card-subtitle>
      </mat-card-header>

      <mat-card-content>
        @if (cadastrado(); as tenant) {
          <div class="sucesso" role="status">
            <mat-icon>check_circle</mat-icon>
            <div>
              <strong>{{ tenant.nomeFantasia ?? tenant.razaoSocial }}</strong> cadastrada no plano {{ tenant.plano.nome }}.
              <p>Entre com o e-mail e a senha do administrador.</p>
              <button mat-flat-button (click)="auth.entrar()">Entrar</button>
            </div>
          </div>
        } @else {
          <form [formGroup]="formulario" (ngSubmit)="enviar()">
            <h3>Empresa</h3>
            <mat-form-field>
              <mat-label>Razão social</mat-label>
              <input matInput formControlName="razaoSocial" autocomplete="organization" />
              <mat-error>Informe a razão social (3 a 150 caracteres).</mat-error>
            </mat-form-field>
            <mat-form-field>
              <mat-label>Nome fantasia (opcional)</mat-label>
              <input matInput formControlName="nomeFantasia" />
            </mat-form-field>
            <mat-form-field>
              <mat-label>CNPJ</mat-label>
              <input matInput formControlName="cnpj" placeholder="00.000.000/0000-00" inputmode="numeric" />
              <mat-error>CNPJ inválido.</mat-error>
            </mat-form-field>
            <mat-form-field>
              <mat-label>Plano</mat-label>
              <mat-select formControlName="plano">
                @for (plano of planos(); track plano.codigo) {
                  <mat-option [value]="plano.codigo">
                    {{ plano.nome }} — {{ plano.limiteLancamentosMes }} lançamentos/mês, {{ plano.limiteUsuarios }} usuários
                  </mat-option>
                }
              </mat-select>
            </mat-form-field>

            <h3>Administrador</h3>
            <div formGroupName="administrador">
              <mat-form-field>
                <mat-label>Nome</mat-label>
                <input matInput formControlName="nome" autocomplete="name" />
                <mat-error>Informe o nome (3 a 100 caracteres).</mat-error>
              </mat-form-field>
              <mat-form-field>
                <mat-label>E-mail</mat-label>
                <input matInput type="email" formControlName="email" autocomplete="email" />
                <mat-error>E-mail inválido.</mat-error>
              </mat-form-field>
              <mat-form-field>
                <mat-label>Senha</mat-label>
                <input matInput type="password" formControlName="senha" autocomplete="new-password" />
                <mat-hint>Mínimo de 8 caracteres, com letras e números.</mat-hint>
                <mat-error>A senha deve ter 8 a 128 caracteres, com letras e números.</mat-error>
              </mat-form-field>
            </div>

            @if (erro(); as erro) {
              <p class="erro" role="alert">{{ erro }}</p>
            }

            <div class="acoes">
              <a mat-button routerLink="/">Voltar</a>
              <button mat-flat-button type="submit" [disabled]="enviando()">
                {{ pendente() ? 'Tentar concluir o cadastro' : 'Cadastrar' }}
              </button>
            </div>
          </form>
        }
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    :host { display: block; max-width: 560px; margin: 40px auto; padding: 0 16px; }
    form, [formGroupName] { display: flex; flex-direction: column; }
    h3 { margin: 16px 0 8px; font-weight: 500; }
    .acoes { display: flex; justify-content: space-between; margin-top: 16px; }
    .erro { color: var(--mat-sys-error); }
    .sucesso { display: flex; gap: 12px; align-items: flex-start; padding: 16px 0; }
    .sucesso mat-icon { color: #2e7d32; }
  `,
})
export class CadastroPage implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly tenants = inject(TenantsApi);

  protected readonly planos = signal<Plano[]>([]);
  protected readonly enviando = signal(false);
  protected readonly erro = signal<string | null>(null);
  protected readonly pendente = signal(false);
  protected readonly cadastrado = signal<Tenant | null>(null);

  protected readonly formulario = inject(FormBuilder).nonNullable.group({
    razaoSocial: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(150)]],
    nomeFantasia: [''],
    cnpj: ['', [Validators.required, cnpjValidator]],
    plano: ['free', Validators.required],
    administrador: inject(FormBuilder).nonNullable.group({
      nome: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(100)]],
      email: ['', [Validators.required, Validators.email]],
      senha: ['', [Validators.required, senhaForteValidator]],
    }),
  });

  async ngOnInit(): Promise<void> {
    try {
      this.planos.set(await this.tenants.planos());
    } catch {
      this.planos.set([]);
    }
  }

  async enviar(): Promise<void> {
    if (this.formulario.invalid) {
      this.formulario.markAllAsTouched();
      return;
    }

    this.enviando.set(true);
    this.erro.set(null);
    const valor = this.formulario.getRawValue();
    try {
      const tenant = await this.tenants.cadastrar({ ...valor, nomeFantasia: valor.nomeFantasia || null });
      this.cadastrado.set(tenant);
    } catch (erro) {
      const erroApi = erro instanceof ErroApi ? erro : null;
      this.pendente.set(erroApi?.status === 503);
      this.erro.set(
        erroApi?.status === 503
          ? 'O serviço de identidade está indisponível no momento. Seu cadastro foi guardado: tente concluir novamente em instantes.'
          : (erroApi?.mensagem ?? 'Não foi possível concluir o cadastro.'),
      );
    } finally {
      this.enviando.set(false);
    }
  }
}
