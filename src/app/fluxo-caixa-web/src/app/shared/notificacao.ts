import { inject, Injectable } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';

import { ErroApi } from '../core/api/erro-api';

@Injectable({ providedIn: 'root' })
export class Notificacao {
  private readonly snackBar = inject(MatSnackBar);

  sucesso(mensagem: string): void {
    this.snackBar.open(mensagem, 'OK', { duration: 4000, panelClass: 'notificacao-sucesso' });
  }

  erro(erro: unknown): void {
    const mensagem = erro instanceof ErroApi ? erro.mensagem : 'Ocorreu um erro inesperado.';
    this.snackBar.open(mensagem, 'Fechar', { duration: 7000, panelClass: 'notificacao-erro' });
  }
}
