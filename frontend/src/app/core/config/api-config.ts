/**
 * Las dos APIs del laboratorio.
 *
 * Que sean dos URLs distintas y no una sola detrás de un gateway es intencionado: el
 * frontend habla directamente con el servicio de negocio y con el módulo de control de
 * acceso, y así queda a la vista cuál de los dos responde cada cosa.
 *
 * - `business` sirve el catálogo y las operaciones protegidas. Nunca explica una decisión.
 * - `accessControl` sirve los checks, el grafo, el modelo y la auditoría. Nunca sabe qué
 *   es un proyecto.
 */
export const ApiConfig = {
  business: 'http://localhost:15100',
  accessControl: 'http://localhost:15101',
} as const;
