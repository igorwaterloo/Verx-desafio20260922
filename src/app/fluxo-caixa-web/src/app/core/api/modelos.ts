// Contratos das APIs (JSON em camelCase, enums como texto).

export type TipoLancamento = 'Credito' | 'Debito';

export interface Lancamento {
  id: string;
  tipo: TipoLancamento;
  valor: number;
  dataCompetencia: string;
  descricao: string;
  lancamentoOriginalId: string | null;
  estornado: boolean;
  criadoPor: string;
  criadoEm: string;
}

export interface NovoLancamento {
  tipo: TipoLancamento;
  valor: number;
  dataCompetencia: string;
  descricao: string;
}

export interface Pagina<T> {
  itens: T[];
  numeroPagina: number;
  tamanhoPagina: number;
  total: number;
}

export interface SaldoDiario {
  data: string;
  totalCreditos: number;
  totalDebitos: number;
  saldo: number;
  quantidadeLancamentos: number;
}

export interface ConsolidadoPeriodo {
  inicio: string;
  fim: string;
  totalCreditos: number;
  totalDebitos: number;
  saldo: number;
  quantidadeLancamentos: number;
  dias: SaldoDiario[];
}

export interface Plano {
  codigo: string;
  nome: string;
  limiteLancamentosMes: number;
  limiteUsuarios: number;
  requisicoesPorSegundo: number;
  precoMensal: number;
}

export type StatusTenant = 'Pendente' | 'Ativo' | 'Falhou';

export interface Tenant {
  id: string;
  razaoSocial: string;
  nomeFantasia: string | null;
  cnpj: string;
  plano: Plano;
  status: StatusTenant;
  criadoEm: string;
}

export interface CadastroEmpresa {
  razaoSocial: string;
  nomeFantasia: string | null;
  cnpj: string;
  plano: string;
  administrador: { nome: string; email: string; senha: string };
}

export type Papel = 'admin' | 'operador';

export interface UsuarioDoTenant {
  id: string;
  email: string;
  nome: string;
  habilitado: boolean;
}

export interface NovoUsuario {
  nome: string;
  email: string;
  senha: string;
  papel: Papel;
}
