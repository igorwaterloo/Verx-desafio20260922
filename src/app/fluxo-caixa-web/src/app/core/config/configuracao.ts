import { InjectionToken } from '@angular/core';

export interface Configuracao {
  /** URL pública do API Gateway (entrada única das APIs). */
  apiUrl: string;
  oidc: {
    authority: string;
    clientId: string;
  };
}

export const CONFIGURACAO = new InjectionToken<Configuracao>('CONFIGURACAO');

export async function carregarConfiguracao(url = '/config.json'): Promise<Configuracao> {
  const resposta = await fetch(url, { cache: 'no-store' });
  if (!resposta.ok) {
    throw new Error(`Não foi possível carregar ${url} (${resposta.status}).`);
  }
  return (await resposta.json()) as Configuracao;
}
