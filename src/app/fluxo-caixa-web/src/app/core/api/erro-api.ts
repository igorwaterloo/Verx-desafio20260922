import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';

/**
 * Erro das APIs normalizado a partir do ProblemDetails (RFC 9457) devolvido pelos serviços e pelo
 * gateway. `codigo` é o código estável do erro (ex.: `lancamento.quota_excedida`).
 */
export class ErroApi extends Error {
  constructor(
    readonly status: number,
    readonly titulo: string,
    readonly codigo?: string,
    readonly errosPorCampo: Record<string, string[]> = {},
    readonly aguardarSegundos?: number,
  ) {
    super(titulo);
    this.name = 'ErroApi';
  }

  /** Serviço fora do ar ou inalcançável: a operação pode ser repetida mais tarde. */
  get indisponivel(): boolean {
    return this.status === 0 || this.status === 502 || this.status === 503 || this.status === 504;
  }

  /** Mensagem pronta para exibir ao usuário. */
  get mensagem(): string {
    const detalhesPorCampo = Object.values(this.errosPorCampo).flat();
    return detalhesPorCampo.length > 0 ? detalhesPorCampo.join(' ') : this.titulo;
  }

  static de(resposta: HttpErrorResponse): ErroApi {
    const corpo = (typeof resposta.error === 'object' && resposta.error) || {};
    const aguardar = Number(resposta.headers?.get('Retry-After')) || undefined;

    switch (resposta.status) {
      case 0:
        return new ErroApi(0, 'Não foi possível conectar ao servidor. Verifique sua conexão.');
      case 401:
        return new ErroApi(401, 'Sua sessão expirou. Entre novamente.', corpo.codigo);
      case 403:
        return new ErroApi(403, corpo.title ?? 'Você não tem permissão para esta operação.', corpo.codigo);
      case 429:
        return new ErroApi(
          429,
          `Limite de requisições do seu plano atingido. Tente novamente em ${aguardar ?? 1} s.`,
          corpo.codigo,
          {},
          aguardar,
        );
      case 502:
      case 503:
      case 504:
        return new ErroApi(resposta.status, corpo.title ?? 'Serviço temporariamente indisponível.', corpo.codigo);
      default:
        return new ErroApi(
          resposta.status,
          corpo.title ?? 'Ocorreu um erro inesperado.',
          corpo.codigo,
          corpo.errors ?? {},
        );
    }
  }
}

/** Converte todas as falhas HTTP em {@link ErroApi}. */
export const erroApiInterceptor: HttpInterceptorFn = (requisicao, proximo) =>
  proximo(requisicao).pipe(
    catchError((erro: unknown) =>
      throwError(() => (erro instanceof HttpErrorResponse ? ErroApi.de(erro) : erro)),
    ),
  );
