import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { IdentityStore } from '../state/identity.store';
import { ApiConfig } from '../config/api-config';

/**
 * Añade la cabecera de identidad a las peticiones al servicio de negocio.
 *
 * Solo al negocio, y esa distinción no es un descuido: el módulo de control de acceso **no
 * tiene identidad de llamante**. A él se le pregunta siempre por un sujeto explícito
 * (`{"subject": "user:ana", ...}`), y por eso desde el Explorer se puede preguntar por
 * cualquiera sin suplantar a nadie.
 *
 * Es una separación que también existe en los sistemas reales: quién pregunta y por quién se
 * pregunta son dos cosas distintas, y confundirlas es la raíz de bastantes fallos de
 * autorización.
 */
export const actAsInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith(ApiConfig.business)) {
    return next(request);
  }

  const identity = inject(IdentityStore);

  return next(
    request.clone({
      setHeaders: { 'X-Act-As': identity.currentId() },
    }),
  );
};
