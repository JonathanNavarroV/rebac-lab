import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AccessControlApi } from '../../core/api/access-control.api';
import {
  DecisionChange,
  Relationship,
  RelationshipMutationResponse,
} from '../../core/models/access-control.models';
import { CatalogStore } from '../../core/state/catalog.store';
import { ObjectChipComponent, PanelComponent } from '../../shared/ui.components';

/**
 * Editor de relaciones.
 *
 * La pieza importante de esta pantalla no es el formulario: es el bloque de
 * **decisiones afectadas** que aparece después de crear o borrar una tupla. El backend
 * re-evalúa todos los casos guiados antes y después de cada escritura y devuelve los que han
 * cambiado de respuesta.
 *
 * Es la demostración directa del punto 6 del enunciado: una sola relación puede cambiar
 * muchas decisiones a la vez, incluidas algunas que no tienen nada que ver a simple vista con
 * el objeto que has tocado.
 */
@Component({
  selector: 'pg-relationships',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PanelComponent, ObjectChipComponent],
  template: `
    <div class="mx-auto max-w-6xl space-y-5">
      <header>
        <h1 class="text-xl font-bold text-slate-100">Relaciones</h1>
        <p class="mt-1 max-w-3xl text-sm leading-relaxed text-slate-400">
          Todo el estado de autorización del sistema son estas filas. No hay ninguna tabla de
          permisos en ninguna parte: lo que alguien puede hacer se deduce de aquí y del modelo.
        </p>
      </header>

      <pg-panel
        title="Crear una relación"
        hint="Se escribe en la notación del paper: objeto#relación@sujeto. El sujeto puede ser una persona (user:pedro), un conjunto (team:backend#member) o un comodín (user:*)."
      >
        <div class="flex flex-wrap gap-3">
          <input
            class="tuple min-w-80 flex-1 rounded-md border border-slate-700 bg-slate-950 px-3 py-2 text-slate-100 focus:border-violet-500 focus:outline-none"
            [(ngModel)]="draft"
            placeholder="project:alpha#editor@team:backend#member"
            (keyup.enter)="create()"
          />
          <button
            type="button"
            class="rounded-md bg-violet-600 px-4 py-2 text-sm font-semibold text-white hover:bg-violet-500 disabled:opacity-50"
            [disabled]="busy()"
            (click)="create()"
          >
            Crear
          </button>
          <button
            type="button"
            class="rounded-md border border-slate-700 px-3 py-2 text-sm text-slate-300 hover:bg-slate-800 disabled:opacity-50"
            [disabled]="busy()"
            (click)="reset()"
            title="Borra todas las relaciones y restaura el escenario inicial. Los modelos publicados no se tocan."
          >
            Restaurar escenario
          </button>
        </div>

        <div class="mt-3 flex flex-wrap gap-2 text-xs">
          <span class="text-slate-500">Prueba con:</span>
          @for (example of examples; track example.tuple) {
            <button
              type="button"
              class="tuple rounded border border-slate-700 px-2 py-0.5 text-slate-400 hover:border-violet-500 hover:text-violet-300"
              [title]="example.hint"
              (click)="draft = example.tuple"
            >
              {{ example.tuple }}
            </button>
          }
        </div>

        @if (error(); as message) {
          <p class="mt-3 rounded-md bg-rose-500/10 p-3 text-sm text-rose-200">{{ message }}</p>
        }
      </pg-panel>

      <!-- ── El efecto de la escritura ─────────────────────────────────── -->
      @if (lastMutation(); as mutation) {
        <pg-panel
          title="Qué ha cambiado"
          hint="Se han re-evaluado los 16 casos guiados antes y después de la operación. Estos son los que han cambiado de respuesta."
        >
          @if (mutation.affectedChecks.length === 0) {
            <p class="text-sm text-slate-400">
              Ninguna de las decisiones que sigue el laboratorio ha cambiado. Que una tupla no
              cambie nada es normal y también informativo: puede ser redundante con un camino
              que ya existía, o afectar a objetos que ningún caso guiado cubre.
            </p>
          } @else {
            <ul class="space-y-2">
              @for (change of mutation.affectedChecks; track $index) {
                <li
                  class="flex flex-wrap items-center gap-2 rounded-lg border border-slate-800 bg-slate-950/60 p-3 text-sm"
                >
                  <span
                    class="rounded px-2 py-0.5 text-xs font-bold"
                    [class]="
                      change.after
                        ? 'bg-emerald-500/15 text-emerald-300'
                        : 'bg-rose-500/15 text-rose-300'
                    "
                  >
                    {{ change.before ? 'ALLOW' : 'DENY' }} → {{ change.after ? 'ALLOW' : 'DENY' }}
                  </span>
                  <pg-object-chip [reference]="change.subject" />
                  <span class="tuple text-slate-500">--{{ change.relation }}--&gt;</span>
                  <pg-object-chip [reference]="change.object" />
                </li>
              }
            </ul>
          }
        </pg-panel>
      }

      <!-- ── Las relaciones existentes ─────────────────────────────────── -->
      <pg-panel
        title="Relaciones existentes ({{ filtered().length }})"
        hint="Las de tipo «parent» no conceden acceso por sí solas: son las vías por las que el acceso viaja hacia abajo cuando una regla las recorre."
      >
        <input
          class="mb-3 w-full rounded-md border border-slate-700 bg-slate-950 px-3 py-2 text-sm text-slate-100 focus:border-violet-500 focus:outline-none"
          [(ngModel)]="filter"
          placeholder="Filtrar por objeto, relación o sujeto…"
        />

        <div class="overflow-x-auto">
          <table class="w-full text-left text-sm">
            <thead class="border-b border-slate-800 text-xs text-slate-500 uppercase">
              <tr>
                <th class="py-2 pr-3">Objeto</th>
                <th class="py-2 pr-3">Relación</th>
                <th class="py-2 pr-3">Sujeto</th>
                <th class="py-2"></th>
              </tr>
            </thead>
            <tbody>
              @for (item of filtered(); track item.id) {
                <tr class="border-b border-slate-800/60 hover:bg-slate-800/30">
                  <td class="py-2 pr-3"><pg-object-chip [reference]="item.object" /></td>
                  <td class="tuple py-2 pr-3 text-slate-300">{{ item.relation }}</td>
                  <td class="py-2 pr-3"><pg-object-chip [reference]="item.subject" /></td>
                  <td class="py-2 text-right">
                    <button
                      type="button"
                      class="rounded border border-rose-600/40 px-2 py-1 text-xs text-rose-300 hover:bg-rose-500/10 disabled:opacity-50"
                      [disabled]="busy()"
                      (click)="remove(item)"
                    >
                      Borrar
                    </button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </pg-panel>
    </div>
  `,
})
export class RelationshipsComponent implements OnInit {
  private readonly api = inject(AccessControlApi);
  protected readonly catalog = inject(CatalogStore);

  protected draft = '';
  protected filter = '';

  protected readonly relationships = signal<Relationship[]>([]);
  protected readonly lastMutation = signal<RelationshipMutationResponse | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  /**
   * Ejemplos elegidos para que cada uno provoque un cambio visible en los casos guiados.
   */
  protected readonly examples = [
    {
      tuple: 'project:delta#editor@user:juan',
      hint: 'Acceso directo a un proyecto de otra organización. Cambia lo que Juan ve en el listado.',
    },
    {
      tuple: 'organization:acme#admin@user:pedro',
      hint: 'Una sola tupla y Pedro pasa a poder editar y borrar TODOS los proyectos de Acme.',
    },
    {
      tuple: 'resource:a#blocked@user:pedro',
      hint: 'Exclusión: Pedro tiene dos caminos hacia Resource A y esta tupla los anula los dos.',
    },
    {
      tuple: 'group:seguridad#member@user:ana',
      hint: 'Mete a Ana en un grupo y hereda de golpe todo lo que ese grupo (y el que lo contiene) tiene.',
    },
  ];

  protected readonly filtered = computed(() => {
    const needle = this.filter.trim().toLowerCase();
    const all = this.relationships();

    if (!needle) {
      return all;
    }

    return all.filter((item) => item.tuple.toLowerCase().includes(needle));
  });

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async create(): Promise<void> {
    const tuple = this.draft.trim();

    if (!tuple) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      this.lastMutation.set(await this.api.createRelationship(tuple));
      this.draft = '';
      await this.load();
    } catch (error) {
      this.error.set(this.describeError(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(item: Relationship): Promise<void> {
    this.busy.set(true);
    this.error.set(null);

    try {
      this.lastMutation.set(await this.api.deleteRelationship(item.id));
      await this.load();
    } catch (error) {
      this.error.set(this.describeError(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async reset(): Promise<void> {
    this.busy.set(true);

    try {
      await this.api.resetScenario();
      this.lastMutation.set(null);
      await this.load();
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    this.relationships.set(await this.api.getRelationships());
  }

  /**
   * Los errores del backend al crear una tupla son didácticos (explican por qué el modelo no
   * la admite), así que se muestran tal cual en lugar de con un mensaje genérico.
   */
  private describeError(error: unknown): string {
    const problem = error as { error?: { errors?: Record<string, string[]> } };
    const errors = problem?.error?.errors;

    if (errors) {
      return Object.values(errors).flat().join(' ');
    }

    return 'No se ha podido completar la operación.';
  }
}
