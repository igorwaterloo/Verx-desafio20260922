import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';

export interface DadosConfirmacao {
  titulo: string;
  mensagem: string;
  confirmar: string;
}

@Component({
  selector: 'app-confirmacao',
  imports: [MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ dados.titulo }}</h2>
    <mat-dialog-content>{{ dados.mensagem }}</mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button [mat-dialog-close]="false">Cancelar</button>
      <button mat-flat-button [mat-dialog-close]="true">{{ dados.confirmar }}</button>
    </mat-dialog-actions>
  `,
})
export class ConfirmacaoDialog {
  protected readonly dados = inject<DadosConfirmacao>(MAT_DIALOG_DATA);
}

export async function confirmar(dialog: MatDialog, dados: DadosConfirmacao): Promise<boolean> {
  const referencia = dialog.open(ConfirmacaoDialog, { data: dados, width: '420px' });
  return (await firstValueFrom(referencia.afterClosed())) === true;
}
