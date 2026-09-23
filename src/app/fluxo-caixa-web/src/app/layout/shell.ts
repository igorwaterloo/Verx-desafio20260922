import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { AuthService } from '../core/auth/auth';

/** Estrutura das páginas autenticadas: navegação, usuário, plano e saída. */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatToolbarModule, MatButtonModule, MatIconModule, MatTooltipModule],
  template: `
    <mat-toolbar class="barra">
      <a routerLink="/app" class="marca"><mat-icon>account_balance_wallet</mat-icon> Fluxo de Caixa</a>

      <nav class="navegacao">
        <a mat-button routerLink="lancamentos" routerLinkActive="ativo"><mat-icon>receipt_long</mat-icon> Lançamentos</a>
        <a mat-button routerLink="consolidado" routerLinkActive="ativo"><mat-icon>monitoring</mat-icon> Consolidado</a>
        @if (auth.admin()) {
          <a mat-button routerLink="empresa" routerLinkActive="ativo"><mat-icon>storefront</mat-icon> Minha empresa</a>
        }
      </nav>

      <span class="espaco"></span>

      @if (auth.usuario(); as usuario) {
        <span class="usuario" [matTooltip]="usuario.email">
          {{ usuario.nome }}
          <span class="plano">{{ usuario.plano }}</span>
        </span>
      }
      <button mat-icon-button (click)="auth.sair()" aria-label="Sair" matTooltip="Sair">
        <mat-icon>logout</mat-icon>
      </button>
    </mat-toolbar>

    <main class="conteudo">
      <router-outlet />
    </main>
  `,
  styles: `
    .barra { gap: 8px; position: sticky; top: 0; z-index: 10; }
    .marca { display: flex; align-items: center; gap: 8px; color: inherit; text-decoration: none; font-weight: 600; margin-right: 16px; }
    .navegacao { display: flex; gap: 4px; }
    .navegacao a.ativo { background: var(--mat-sys-secondary-container); }
    .espaco { flex: 1; }
    .usuario { font-size: 14px; display: flex; align-items: center; gap: 8px; }
    .plano { text-transform: uppercase; font-size: 11px; font-weight: 600; padding: 2px 8px; border-radius: 12px;
      background: var(--mat-sys-primary-container); color: var(--mat-sys-on-primary-container); }
    .conteudo { max-width: 1100px; margin: 24px auto; padding: 0 16px; }
    @media (max-width: 720px) {
      .navegacao a { min-width: 0; padding: 0 8px; }
      .usuario { display: none; }
    }
  `,
})
export class Shell {
  protected readonly auth = inject(AuthService);
}
