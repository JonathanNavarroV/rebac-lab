import { Injectable, computed, signal } from '@angular/core';
import { LabUser } from '../models/business.models';

const STORAGE_KEY = 'playground.act-as';

/**
 * Quién soy ahora mismo en el laboratorio.
 *
 * Es el estado más importante de toda la aplicación: cambiarlo hace que TODAS las pantallas
 * respondan distinto sin que ninguna relación haya cambiado. Poder pasar de Juan a Ana en un
 * clic y repetir la misma pregunta es, probablemente, la forma más rápida de entender que en
 * ReBAC el acceso es una propiedad de la relación entre dos cosas, y no un atributo de
 * ninguna de las dos.
 *
 * Implementado con signals (el frontend es zoneless): cualquier `computed` o `effect` que
 * dependa de `current` se recalcula solo al cambiar de identidad.
 */
@Injectable({ providedIn: 'root' })
export class IdentityStore {
  private readonly _users = signal<LabUser[]>([]);
  private readonly _currentId = signal<string>(localStorage.getItem(STORAGE_KEY) ?? 'juan');

  readonly users = this._users.asReadonly();
  readonly currentId = this._currentId.asReadonly();

  /** El usuario activo, o `null` mientras no se ha cargado el catálogo. */
  readonly current = computed(
    () => this._users().find((user) => user.id === this._currentId()) ?? null,
  );

  /** Referencia como sujeto: `user:juan`. Es lo que se manda a los endpoints de check. */
  readonly currentSubject = computed(() => `user:${this._currentId()}`);

  setUsers(users: LabUser[]): void {
    this._users.set(users);

    // Si la identidad guardada ya no existe (por ejemplo tras un reset del escenario), se
    // cae a la primera disponible en lugar de dejar la aplicación preguntando por alguien
    // que no está.
    if (users.length > 0 && !users.some((user) => user.id === this._currentId())) {
      this.actAs(users[0].id);
    }
  }

  actAs(userId: string): void {
    this._currentId.set(userId);
    localStorage.setItem(STORAGE_KEY, userId);
  }
}
