import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';

import { TenantsApi } from '../../core/api/apis';
import { ErroApi } from '../../core/api/erro-api';
import { AuthService } from '../../core/auth/auth';
import { CadastroPage } from './cadastro';

const plano = { codigo: 'free', nome: 'Free', limiteLancamentosMes: 500, limiteUsuarios: 2, requisicoesPorSegundo: 20, precoMensal: 0 };
const dados = {
  razaoSocial: 'Padaria Exemplo LTDA',
  nomeFantasia: '',
  cnpj: '11.222.333/0001-81',
  plano: 'free',
  administrador: { nome: 'Ana Admin', email: 'ana@exemplo.com', senha: 'Senha123' },
};

describe('CadastroPage', () => {
  let api: { planos: ReturnType<typeof vi.fn>; cadastrar: ReturnType<typeof vi.fn> };

  async function criar() {
    api = { planos: vi.fn().mockResolvedValue([plano]), cadastrar: vi.fn() };
    await TestBed.configureTestingModule({
      imports: [CadastroPage],
      providers: [
        provideRouter([]),
        { provide: TenantsApi, useValue: api },
        { provide: AuthService, useValue: { entrar: vi.fn(), autenticado: signal(false) } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(CadastroPage);
    await fixture.whenStable();
    const pagina = fixture.componentInstance as unknown as { formulario: { setValue(v: unknown): void }; enviar(): Promise<void> };
    return { fixture, pagina, elemento: fixture.nativeElement as HTMLElement };
  }

  it('não envia formulário inválido', async () => {
    const { pagina } = await criar();

    await pagina.enviar();

    expect(api.cadastrar).not.toHaveBeenCalled();
  });

  it('mostra o sucesso e o botão de entrar após o cadastro', async () => {
    const { fixture, pagina, elemento } = await criar();
    api.cadastrar.mockResolvedValue({ razaoSocial: dados.razaoSocial, nomeFantasia: null, plano });

    pagina.formulario.setValue(dados);
    await pagina.enviar();
    await fixture.whenStable();

    expect(api.cadastrar).toHaveBeenCalledWith({ ...dados, nomeFantasia: null });
    expect(elemento.textContent).toContain('cadastrada no plano Free');
  });

  it('com o IdP indisponível (503), guarda o cadastro como pendente e permite concluir depois', async () => {
    const { fixture, pagina, elemento } = await criar();
    api.cadastrar.mockRejectedValue(new ErroApi(503, 'Indisponível'));

    pagina.formulario.setValue(dados);
    await pagina.enviar();
    await fixture.whenStable();

    expect(elemento.querySelector('[role=alert]')?.textContent).toContain('tente concluir novamente');
    expect(elemento.querySelector('button[type=submit]')?.textContent).toContain('Tentar concluir o cadastro');
  });

  it('mostra a mensagem do conflito de CNPJ', async () => {
    const { fixture, pagina, elemento } = await criar();
    api.cadastrar.mockRejectedValue(new ErroApi(409, 'Já existe uma empresa com este CNPJ.'));

    pagina.formulario.setValue(dados);
    await pagina.enviar();
    await fixture.whenStable();

    expect(elemento.querySelector('[role=alert]')?.textContent).toContain('Já existe uma empresa com este CNPJ.');
  });
});
