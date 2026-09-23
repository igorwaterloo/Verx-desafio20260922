import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { Component, effect, inject, OnInit, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { Router, RouterLink } from '@angular/router';

import { TenantsApi } from '../../core/api/apis';
import { Plano } from '../../core/api/modelos';
import { AuthService } from '../../core/auth/auth';

/** Página pública: apresentação, catálogo de planos, cadastro e login. */
@Component({
  selector: 'app-inicio',
  imports: [RouterLink, MatButtonModule, MatCardModule, MatIconModule, CurrencyPipe, DecimalPipe],
  template: `
    <section class="hero">
      <h1><mat-icon class="icone">account_balance_wallet</mat-icon> Fluxo de Caixa</h1>
      <p>Registre créditos e débitos do seu negócio e acompanhe o saldo diário consolidado.</p>
      <div class="acoes">
        <a mat-flat-button routerLink="/cadastro">Cadastrar minha empresa</a>
        <button mat-stroked-button (click)="auth.entrar()">Entrar</button>
      </div>
    </section>

    <section class="planos">
      <h2>Planos</h2>
      <div class="grade">
        @for (plano of planos(); track plano.codigo) {
          <mat-card appearance="outlined">
            <mat-card-header>
              <mat-card-title>{{ plano.nome }}</mat-card-title>
              <mat-card-subtitle>
                {{ plano.precoMensal === 0 ? 'Gratuito' : (plano.precoMensal | currency) + '/mês' }}
              </mat-card-subtitle>
            </mat-card-header>
            <mat-card-content>
              <ul>
                <li>{{ plano.limiteLancamentosMes | number }} lançamentos por mês</li>
                <li>Até {{ plano.limiteUsuarios }} usuários</li>
                <li>{{ plano.requisicoesPorSegundo }} requisições por segundo</li>
              </ul>
            </mat-card-content>
          </mat-card>
        } @empty {
          <p class="vazio">{{ erroPlanos() ?? 'Carregando planos…' }}</p>
        }
      </div>
    </section>
  `,
  styles: `
    :host { display: block; max-width: 960px; margin: 48px auto; padding: 0 16px; }
    .hero { text-align: center; }
    .hero h1 { display: flex; align-items: center; justify-content: center; gap: 12px; font-size: 40px; margin: 0; }
    .icone { font-size: 44px; width: 44px; height: 44px; }
    .hero p { font-size: 18px; color: var(--mat-sys-on-surface-variant); }
    .acoes { display: flex; gap: 12px; justify-content: center; margin-top: 24px; }
    .planos { margin-top: 48px; }
    .grade { display: grid; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); gap: 16px; }
    ul { padding-left: 18px; line-height: 1.8; }
    .vazio { color: var(--mat-sys-on-surface-variant); }
  `,
})
export class InicioPage implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly tenants = inject(TenantsApi);
  private readonly router = inject(Router);

  protected readonly planos = signal<Plano[]>([]);
  protected readonly erroPlanos = signal<string | null>(null);

  constructor() {
    // Após o retorno do login, segue direto para a área autenticada.
    effect(() => {
      if (this.auth.autenticado()) {
        void this.router.navigate(['/app']);
      }
    });
  }

  async ngOnInit(): Promise<void> {
    void this.auth.carregar();
    try {
      this.planos.set(await this.tenants.planos());
    } catch {
      this.erroPlanos.set('Não foi possível carregar os planos agora.');
    }
  }
}
