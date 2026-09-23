import { Component, inject } from '@angular/core';
import { MatIconRegistry } from '@angular/material/icon';
import { RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  template: '<router-outlet />',
})
export class App {
  constructor() {
    // Ícones Material Symbols servidos pela própria aplicação (sem fontes externas — CSP restrita).
    inject(MatIconRegistry).setDefaultFontSetClass('material-symbols-outlined');
  }
}
