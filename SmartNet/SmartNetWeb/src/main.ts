import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { aplicarTemaInicial } from './app/shared/tema.service';

// design.md D1: resolves and writes `data-tema` before bootstrap, so there is no flash of the
// wrong theme while Angular loads.
aplicarTemaInicial();

bootstrapApplication(App, appConfig)
  .catch((err) => console.error(err));
