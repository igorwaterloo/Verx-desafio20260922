import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { authInterceptor, provideAuth, withAppInitializerAuthCheck } from 'angular-auth-oidc-client';

import { routes } from './app.routes';
import { erroApiInterceptor } from './core/api/erro-api';
import { CONFIGURACAO, Configuracao } from './core/config/configuracao';
import { provideLocalizacaoBr } from './core/config/localizacao';

export function criarAppConfig(configuracao: Configuracao): ApplicationConfig {
  return {
    providers: [
      provideBrowserGlobalErrorListeners(),
      provideRouter(routes, withComponentInputBinding()),
      { provide: CONFIGURACAO, useValue: configuracao },

      // OIDC com Authorization Code + PKCE no Keycloak (ADR-0008); renovação por refresh token.
      provideAuth(
        {
          config: {
            authority: configuracao.oidc.authority,
            clientId: configuracao.oidc.clientId,
            redirectUrl: window.location.origin,
            postLogoutRedirectUri: window.location.origin,
            scope: 'openid profile email',
            responseType: 'code',
            silentRenew: true,
            useRefreshToken: true,
            renewTimeBeforeTokenExpiresInSeconds: 30,
            // O token só é anexado às chamadas ao gateway — nunca a outras origens.
            secureRoutes: [configuracao.apiUrl],
            ignoreNonceAfterRefresh: true,
          },
        },
        withAppInitializerAuthCheck(),
      ),
      provideHttpClient(withInterceptors([authInterceptor(), erroApiInterceptor])),

      provideLocalizacaoBr(),
    ],
  };
}
