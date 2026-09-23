import { expect, Page, test } from '@playwright/test';

/** CNPJ válido e único por execução (o cadastro rejeita CNPJ repetido — RP-02). */
function gerarCnpj(): string {
  const base = Array.from({ length: 8 }, () => Math.floor(Math.random() * 10)).concat([0, 0, 0, 1]);
  const digito = (numeros: number[], pesos: number[]) => {
    const resto = numeros.reduce((soma, n, i) => soma + n * pesos[i], 0) % 11;
    return resto < 2 ? 0 : 11 - resto;
  };
  const primeiro = digito(base, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
  const segundo = digito([...base, primeiro], [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
  return [...base, primeiro, segundo].join('');
}

async function registrarLancamento(page: Page, tipo: 'Crédito' | 'Débito', valor: string, descricao: string) {
  await page.locator('mat-button-toggle', { hasText: tipo }).click();
  await page.getByLabel('Valor (R$)').fill(valor);
  await page.getByLabel('Descrição').fill(descricao);
  await page.getByRole('button', { name: 'Registrar' }).click();
  await expect(page.getByRole('cell', { name: descricao })).toBeVisible();
}

test('cadastro da empresa → login → lançamentos → saldo consolidado', async ({ page }) => {
  const sufixo = Date.now().toString(36);
  const email = `admin.${sufixo}@e2e.local`;
  const senha = 'SenhaE2e123';

  // 1. Onboarding em autoatendimento.
  await page.goto('/cadastro');
  await page.getByLabel('Razão social', { exact: true }).fill(`Empresa E2E ${sufixo} LTDA`);
  await page.getByLabel('CNPJ').fill(gerarCnpj());
  await page.getByLabel('Nome', { exact: true }).fill('Admin E2E');
  await page.getByLabel('E-mail').fill(email);
  await page.getByLabel('Senha').fill(senha);
  await page.getByRole('button', { name: 'Cadastrar' }).click();
  await expect(page.getByText('cadastrada no plano')).toBeVisible();

  // 2. Login no Keycloak (Authorization Code + PKCE).
  await page.getByRole('button', { name: 'Entrar' }).click();
  await page.locator('#username').fill(email);
  // Com Organizations habilitado o Keycloak pede o usuário e só depois a senha (identity-first).
  if (!(await page.locator('#password').isVisible())) {
    await page.locator('#kc-login').click();
  }
  await page.locator('#password').fill(senha);
  await page.locator('#kc-login').click();
  await expect(page).toHaveURL(/\/app\/lancamentos$/);
  await expect(page.getByText('Admin E2E')).toBeVisible();

  // 3. Lançamentos do dia.
  await registrarLancamento(page, 'Crédito', '150.50', `Venda ${sufixo}`);
  await registrarLancamento(page, 'Débito', '50', `Fornecedor ${sufixo}`);

  // 4. O consolidado converge em poucos segundos (consistência eventual — RNF-03).
  await page.getByRole('link', { name: 'Consolidado' }).click();
  await expect(async () => {
    await page.getByRole('button', { name: 'Atualizar' }).click();
    await expect(page.getByTestId('saldo-dia')).toContainText('100,50', { timeout: 1_000 });
  }).toPass({ timeout: 20_000 });
});
