import { bootstrapApplication } from '@angular/platform-browser';
import { App } from './app/app';
import { criarAppConfig } from './app/app.config';
import { carregarConfiguracao } from './app/core/config/configuracao';

// A configuração (URLs do gateway e do Keycloak) é lida em tempo de execução de /config.json:
// a mesma imagem da SPA serve qualquer ambiente.
carregarConfiguracao()
  .then((configuracao) => bootstrapApplication(App, criarAppConfig(configuracao)))
  .catch((erro) => console.error('Falha ao iniciar a aplicação', erro));
