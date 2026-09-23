import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

const PESOS_PRIMEIRO_DIGITO = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
const PESOS_SEGUNDO_DIGITO = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

/** Mesma regra do domínio (Cnpj.cs — RP-01): 14 dígitos, não repetidos, com dígitos verificadores válidos. */
export function cnpjValido(valor: string | null | undefined): boolean {
  const digitos = (valor ?? '').replace(/\D/g, '');
  if (digitos.length !== 14 || /^(\d)\1{13}$/.test(digitos)) {
    return false;
  }

  const digito = (base: string, pesos: number[]) => {
    const resto = pesos.reduce((soma, peso, i) => soma + Number(base[i]) * peso, 0) % 11;
    return resto < 2 ? 0 : 11 - resto;
  };

  const primeiro = digito(digitos, PESOS_PRIMEIRO_DIGITO);
  const segundo = digito(digitos, PESOS_SEGUNDO_DIGITO);
  return Number(digitos[12]) === primeiro && Number(digitos[13]) === segundo;
}

export const cnpjValidator: ValidatorFn = (controle: AbstractControl): ValidationErrors | null =>
  !controle.value || cnpjValido(controle.value) ? null : { cnpj: true };

/** Mesma política do backend: 8 a 128 caracteres, com letras e números. */
export const senhaForteValidator: ValidatorFn = (controle: AbstractControl): ValidationErrors | null => {
  const senha: string = controle.value ?? '';
  const forte = senha.length >= 8 && senha.length <= 128 && /[a-zA-Z]/.test(senha) && /\d/.test(senha);
  return !senha || forte ? null : { senhaFraca: true };
};

/** Data de hoje no fuso de São Paulo, no formato AAAA-MM-DD (mesma regra da data de competência). */
export function hojeEmSaoPaulo(): string {
  return paraIso(new Date());
}

export function paraIso(data: Date): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo' }).format(data);
}

/** Converte AAAA-MM-DD em Date local (meio-dia, para evitar deslocamento de fuso no datepicker). */
export function deIso(data: string): Date {
  const [ano, mes, dia] = data.split('-').map(Number);
  return new Date(ano, mes - 1, dia, 12);
}

export function somarDias(data: string, dias: number): string {
  const base = deIso(data);
  base.setDate(base.getDate() + dias);
  return `${base.getFullYear()}-${String(base.getMonth() + 1).padStart(2, '0')}-${String(base.getDate()).padStart(2, '0')}`;
}

/** Chave de idempotência por envio de formulário (RN-08). */
export function novaChaveIdempotencia(): string {
  return crypto.randomUUID();
}
