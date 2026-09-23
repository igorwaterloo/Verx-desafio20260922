import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, provideRouter, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { of } from 'rxjs';
import { vi } from 'vitest';

import { adminGuard, AuthService, autenticadoGuard } from './auth';

const claims = {
  name: 'Ana Admin',
  email: 'ana@empresa.com',
  tenant_id: 'tenant-1',
  plano: 'pro',
  roles: ['admin', 'default-roles-fluxo-caixa'],
};

function configurar(autenticado: boolean, roles = claims.roles) {
  const oidc = {
    isAuthenticated$: of({ isAuthenticated: autenticado }),
    getPayloadFromAccessToken: () => of({ ...claims, roles }),
    authorize: vi.fn(),
    logoff: vi.fn(() => of(null)),
    forceRefreshSession: vi.fn(() => of({})),
  };
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: OidcSecurityService, useValue: oidc }],
  });
  return oidc;
}

const executar = (guard: typeof autenticadoGuard) =>
  TestBed.runInInjectionContext(() => guard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot));

describe('AuthService', () => {
  it('lê o usuário, o tenant, o plano e os papéis do access token', async () => {
    configurar(true);
    const auth = TestBed.inject(AuthService);

    const usuario = await auth.carregar();

    expect(usuario).toEqual({
      nome: 'Ana Admin',
      email: 'ana@empresa.com',
      tenantId: 'tenant-1',
      plano: 'pro',
      papeis: ['admin'],
    });
    expect(auth.autenticado()).toBe(true);
    expect(auth.admin()).toBe(true);
  });

  it('renova a sessão para refletir o novo plano', async () => {
    const oidc = configurar(true);

    await TestBed.inject(AuthService).renovarSessao();

    expect(oidc.forceRefreshSession).toHaveBeenCalled();
  });
});

describe('autenticadoGuard', () => {
  it('libera usuários autenticados', async () => {
    configurar(true);

    expect(await executar(autenticadoGuard)).toBe(true);
  });

  it('inicia o login quando não há sessão', async () => {
    const oidc = configurar(false);

    expect(await executar(autenticadoGuard)).toBe(false);
    expect(oidc.authorize).toHaveBeenCalled();
  });
});

describe('adminGuard', () => {
  it('libera administradores', async () => {
    configurar(true);

    expect(await executar(adminGuard)).toBe(true);
  });

  it('redireciona operadores para os lançamentos', async () => {
    configurar(true, ['operador']);

    const resultado = await executar(adminGuard);

    expect(resultado).toBeInstanceOf(UrlTree);
    expect(TestBed.inject(Router).serializeUrl(resultado as UrlTree)).toBe('/app/lancamentos');
  });
});
