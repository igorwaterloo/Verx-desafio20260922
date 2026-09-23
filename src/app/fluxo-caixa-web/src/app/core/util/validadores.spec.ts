import { FormControl } from '@angular/forms';

import { cnpjValidator, cnpjValido, deIso, paraIso, senhaForteValidator, somarDias } from './validadores';

describe('validadores', () => {
  describe('cnpjValido', () => {
    it.each(['11.222.333/0001-81', '11222333000181', '45.723.174/0001-10'])('aceita %s', (cnpj) => {
      expect(cnpjValido(cnpj)).toBe(true);
    });

    it.each(['11.222.333/0001-82', '11111111111111', '1122233300018', '', null, undefined])('rejeita %s', (cnpj) => {
      expect(cnpjValido(cnpj)).toBe(false);
    });
  });

  it('cnpjValidator deixa o campo vazio para o required', () => {
    expect(cnpjValidator(new FormControl(''))).toBeNull();
    expect(cnpjValidator(new FormControl('123'))).toEqual({ cnpj: true });
  });

  it.each([
    ['curta1', { senhaFraca: true }],
    ['somenteletras', { senhaFraca: true }],
    ['12345678', { senhaFraca: true }],
    ['Senha123', null],
    ['', null],
  ])('senhaForteValidator(%s)', (senha, esperado) => {
    expect(senhaForteValidator(new FormControl(senha))).toEqual(esperado);
  });

  it('converte datas ISO sem deslocamento de fuso', () => {
    expect(paraIso(deIso('2026-03-01'))).toBe('2026-03-01');
    expect(somarDias('2026-03-01', -1)).toBe('2026-02-28');
    expect(somarDias('2026-12-31', 1)).toBe('2027-01-01');
  });
});
