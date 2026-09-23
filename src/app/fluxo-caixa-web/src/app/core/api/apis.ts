import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { CONFIGURACAO } from '../config/configuracao';
import {
  CadastroEmpresa,
  ConsolidadoPeriodo,
  Lancamento,
  NovoLancamento,
  NovoUsuario,
  Pagina,
  Plano,
  SaldoDiario,
  Tenant,
  UsuarioDoTenant,
} from './modelos';

/** Base dos clientes: todas as chamadas passam pelo API Gateway (ADR-0009). */
abstract class ApiBase {
  protected readonly http = inject(HttpClient);
  protected readonly url = inject(CONFIGURACAO).apiUrl.replace(/\/$/, '');
}

@Injectable({ providedIn: 'root' })
export class TenantsApi extends ApiBase {
  planos(): Promise<Plano[]> {
    return firstValueFrom(this.http.get<Plano[]>(`${this.url}/api/v1/planos`));
  }

  cadastrar(dados: CadastroEmpresa): Promise<Tenant> {
    return firstValueFrom(this.http.post<Tenant>(`${this.url}/api/v1/tenants`, dados));
  }

  atual(): Promise<Tenant> {
    return firstValueFrom(this.http.get<Tenant>(`${this.url}/api/v1/tenants/atual`));
  }

  alterarPlano(plano: string): Promise<Tenant> {
    return firstValueFrom(this.http.put<Tenant>(`${this.url}/api/v1/tenants/atual/plano`, { plano }));
  }

  usuarios(): Promise<UsuarioDoTenant[]> {
    return firstValueFrom(this.http.get<UsuarioDoTenant[]>(`${this.url}/api/v1/tenants/atual/usuarios`));
  }

  adicionarUsuario(usuario: NovoUsuario): Promise<UsuarioDoTenant> {
    return firstValueFrom(this.http.post<UsuarioDoTenant>(`${this.url}/api/v1/tenants/atual/usuarios`, usuario));
  }
}

@Injectable({ providedIn: 'root' })
export class LancamentosApi extends ApiBase {
  listar(data: string, pagina = 1, tamanhoPagina = 20): Promise<Pagina<Lancamento>> {
    const params = new HttpParams().set('data', data).set('pagina', pagina).set('tamanhoPagina', tamanhoPagina);
    return firstValueFrom(this.http.get<Pagina<Lancamento>>(`${this.url}/api/v1/lancamentos`, { params }));
  }

  /**
   * Registra um lançamento. A mesma `chaveIdempotencia` deve ser reutilizada em reenvios do mesmo
   * formulário: o serviço devolve o lançamento já criado em vez de duplicá-lo (RN-08).
   */
  registrar(lancamento: NovoLancamento, chaveIdempotencia: string): Promise<Lancamento> {
    const headers = new HttpHeaders({ 'Idempotency-Key': chaveIdempotencia });
    return firstValueFrom(this.http.post<Lancamento>(`${this.url}/api/v1/lancamentos`, lancamento, { headers }));
  }

  estornar(id: string): Promise<Lancamento> {
    return firstValueFrom(this.http.post<Lancamento>(`${this.url}/api/v1/lancamentos/${id}/estorno`, null));
  }
}

@Injectable({ providedIn: 'root' })
export class ConsolidadoApi extends ApiBase {
  dia(data: string): Promise<SaldoDiario> {
    return firstValueFrom(this.http.get<SaldoDiario>(`${this.url}/api/v1/consolidado/${data}`));
  }

  periodo(inicio: string, fim: string): Promise<ConsolidadoPeriodo> {
    const params = new HttpParams().set('inicio', inicio).set('fim', fim);
    return firstValueFrom(this.http.get<ConsolidadoPeriodo>(`${this.url}/api/v1/consolidado`, { params }));
  }
}
