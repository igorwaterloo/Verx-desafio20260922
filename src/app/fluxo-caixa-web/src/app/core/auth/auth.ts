import { computed, inject, Injectable, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CanActivateFn, Router } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { firstValueFrom, switchMap } from 'rxjs';

import { Papel } from '../api/modelos';

export interface UsuarioLogado {
  nome: string;
  email: string;
  tenantId: string;
  plano: string;
  papeis: Papel[];
}

/** Estado de autenticação (claims do access token emitido pelo Keycloak — ADR-0008/0015). */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly oidc = inject(OidcSecurityService);

  readonly usuario = signal<UsuarioLogado | null>(null);
  readonly autenticado = computed(() => this.usuario() !== null);
  readonly admin = computed(() => this.usuario()?.papeis.includes('admin') ?? false);

  constructor() {
    this.oidc.isAuthenticated$
      .pipe(
        takeUntilDestroyed(),
        switchMap(async ({ isAuthenticated }) => (isAuthenticated ? await this.lerUsuario() : null)),
      )
      .subscribe((usuario) => this.usuario.set(usuario));
  }

  entrar(): void {
    this.oidc.authorize();
  }

  sair(): void {
    this.oidc.logoff().subscribe();
  }

  /** Lê a sessão atual (já verificada na inicialização) e o usuário do access token. */
  async carregar(): Promise<UsuarioLogado | null> {
    const { isAuthenticated } = await firstValueFrom(this.oidc.isAuthenticated$);
    const usuario = isAuthenticated ? await this.lerUsuario() : null;
    this.usuario.set(usuario);
    return usuario;
  }

  /**
   * Renova os tokens (refresh token) para refletir claims alteradas no Keycloak, como o plano após
   * um upgrade — a cota do gateway é aplicada a partir da claim `plano` (ADR-0016).
   */
  async renovarSessao(): Promise<UsuarioLogado | null> {
    await firstValueFrom(this.oidc.forceRefreshSession());
    return this.carregar();
  }

  private async lerUsuario(): Promise<UsuarioLogado> {
    const claims = await firstValueFrom(this.oidc.getPayloadFromAccessToken());
    return {
      nome: claims.name ?? claims.preferred_username ?? claims.email,
      email: claims.email,
      tenantId: claims.tenant_id,
      plano: claims.plano,
      papeis: (claims.roles ?? []).filter((papel: string): papel is Papel => papel === 'admin' || papel === 'operador'),
    };
  }
}

/** Rotas autenticadas: sem sessão, inicia o login no Keycloak. */
export const autenticadoGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  if (await auth.carregar()) {
    return true;
  }
  auth.entrar();
  return false;
};

/** Rotas de administração do tenant (papel admin — RN-10). */
export const adminGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const usuario = await auth.carregar();
  return usuario?.papeis.includes('admin') ? true : router.createUrlTree(['/app/lancamentos']);
};
