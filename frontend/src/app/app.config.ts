import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';

import { routes } from './app.routes';
import { actAsInterceptor } from './core/http/act-as.interceptor';

/**
 * Configuración de la aplicación.
 *
 * `provideZonelessChangeDetection` como en tu frontend de psinet: nada de zone.js, todo
 * el estado va con signals. Es importante tenerlo presente al leer los componentes —
 * cualquier estado que deba repintar la vista tiene que ser un `signal`, no una
 * propiedad suelta.
 */
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([actAsInterceptor])),
  ],
};
