import { Injectable, computed, inject, signal } from '@angular/core';
import { BusinessApi } from '../api/business.api';
import { Catalog, CatalogEntity } from '../models/business.models';
import { IdentityStore } from './identity.store';

/**
 * El inventario del laboratorio, cargado una vez y compartido por todas las pantallas.
 *
 * Es deliberadamente omnisciente: contiene TODO, sin filtrar por permisos. Para poder
 * preguntar "¿puede Ana ver Project Delta?" hay que poder elegir a Ana y a Delta en un
 * desplegable aunque Ana no los vea. Una aplicación real no tendría este endpoint; un
 * laboratorio de autorización no puede funcionar sin él.
 */
@Injectable({ providedIn: 'root' })
export class CatalogStore {
  private readonly api = inject(BusinessApi);
  private readonly identity = inject(IdentityStore);

  private readonly _catalog = signal<Catalog | null>(null);
  private readonly _loading = signal(false);
  private readonly _error = signal<string | null>(null);

  readonly catalog = this._catalog.asReadonly();
  readonly loading = this._loading.asReadonly();
  readonly error = this._error.asReadonly();

  /** Modo del control de acceso (`InProcess` o `Remote`), tal como lo reporta el negocio. */
  readonly authorizationMode = computed(() => this._catalog()?.authorizationMode ?? '—');

  /** Todo lo que puede ser OBJETO de un check: proyectos, carpetas, recursos, equipos... */
  readonly allObjects = computed<CatalogEntity[]>(() => {
    const catalog = this._catalog();

    if (!catalog) {
      return [];
    }

    return [
      ...catalog.projects,
      ...catalog.folders,
      ...catalog.resources,
      ...catalog.organizations,
      ...catalog.teams,
      ...catalog.groups,
    ];
  });

  /**
   * Todo lo que puede ser SUJETO: las personas, más los usersets que el modelo admite.
   *
   * Incluir los usersets en el mismo selector que las personas no es un atajo: es lo que
   * enseña que en ReBAC un conjunto puede ocupar exactamente el mismo hueco que un individuo
   * en cualquier relación.
   */
  readonly allSubjects = computed<string[]>(() => {
    const catalog = this._catalog();

    if (!catalog) {
      return [];
    }

    return [
      ...catalog.users.map((user) => user.objectRef),
      ...catalog.teams.map((team) => `${team.objectRef}#member`),
      ...catalog.groups.map((group) => `${group.objectRef}#member`),
      ...catalog.organizations.map((organization) => `${organization.objectRef}#member`),
      ...catalog.organizations.map((organization) => `${organization.objectRef}#admin`),
      'user:*',
    ];
  });

  async load(): Promise<void> {
    this._loading.set(true);
    this._error.set(null);

    try {
      const [catalog, users] = await Promise.all([this.api.getCatalog(), this.api.getUsers()]);

      this._catalog.set(catalog);
      this.identity.setUsers(users);
    } catch {
      this._error.set(
        'No se ha podido cargar el catálogo. ¿Están levantadas las dos APIs (:15100 y :15101) y la base de datos?',
      );
    } finally {
      this._loading.set(false);
    }
  }

  /** Nombre legible de una referencia, si el catálogo lo conoce. */
  displayName(reference: string): string | null {
    const base = reference.split('#')[0];

    const found = [...this.allObjects(), ...(this._catalog()?.users ?? [])].find(
      (entity) => entity.objectRef === base,
    );

    return found?.name ?? null;
  }
}
