import { Routes } from '@angular/router';

import { adminGuard, autenticadoGuard } from './core/auth/auth';
import { Shell } from './layout/shell';
import { CadastroPage } from './features/publico/cadastro';
import { InicioPage } from './features/publico/inicio';

export const routes: Routes = [
  { path: '', component: InicioPage, title: 'Fluxo de Caixa' },
  { path: 'cadastro', component: CadastroPage, title: 'Cadastrar empresa · Fluxo de Caixa' },
  {
    path: 'app',
    component: Shell,
    canActivate: [autenticadoGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'lancamentos' },
      {
        path: 'lancamentos',
        title: 'Lançamentos · Fluxo de Caixa',
        loadComponent: () => import('./features/lancamentos/lancamentos').then((m) => m.LancamentosPage),
      },
      {
        path: 'consolidado',
        title: 'Consolidado · Fluxo de Caixa',
        loadComponent: () => import('./features/consolidado/consolidado').then((m) => m.ConsolidadoPage),
      },
      {
        path: 'empresa',
        title: 'Minha empresa · Fluxo de Caixa',
        canActivate: [adminGuard],
        loadComponent: () => import('./features/empresa/empresa').then((m) => m.EmpresaPage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
