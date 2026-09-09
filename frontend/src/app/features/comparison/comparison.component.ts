import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { BusinessApi } from '../../core/api/business.api';
import { Comparison } from '../../core/models/business.models';
import { CatalogStore } from '../../core/state/catalog.store';
import { IdentityStore } from '../../core/state/identity.store';
import { PanelComponent } from '../../shared/ui.components';

/**
 * RBAC contra ReBAC: la misma pregunta, los dos razonamientos, y el coste de cada modelo.
 *
 * La pantalla evita a propósito la conclusión fácil de que ReBAC «gana». En la mayoría de
 * preguntas los dos modelos coinciden, el razonamiento de RBAC es más corto y más fácil de
 * auditar, y eso es una virtud real. Lo que se compara de verdad es el **coste de mantener**
 * el modelo: cuántas filas hay que sostener y qué pasa cuando algo cambia de sitio.
 */
@Component({
  selector: 'pg-comparison',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PanelComponent],
  template: `
    <div class="mx-auto max-w-6xl space-y-5">
      <header>
        <h1 class="text-xl font-bold text-slate-100">RBAC vs ReBAC</h1>
        <p class="mt-1 max-w-3xl text-sm leading-relaxed text-slate-400">
          El mismo escenario está montado de las dos formas: como tuplas de relación, y como
          roles con permisos enumerados. Aquí se le hace la misma pregunta a los dos.
        </p>
      </header>

      <pg-panel title="La pregunta">
        <div class="flex flex-wrap items-end gap-3">
          <label class="block">
            <span class="mb-1 block text-xs text-slate-400">Usuario</span>
            <select
              class="rounded-md border border-slate-700 bg-slate-950 px-3 py-2 text-sm text-slate-100"
              [(ngModel)]="userId"
            >
              @for (user of identity.users(); track user.id) {
                <option [value]="user.id">{{ user.name }}</option>
              }
            </select>
          </label>

          <label class="block">
            <span class="mb-1 block text-xs text-slate-400">Acción</span>
            <select
              class="tuple rounded-md border border-slate-700 bg-slate-950 px-3 py-2 text-sm text-slate-100"
              [(ngModel)]="action"
            >
              @for (option of actions; track option) {
                <option [value]="option">{{ option }}</option>
              }
            </select>
          </label>

          <label class="block">
            <span class="mb-1 block text-xs text-slate-400">Objeto</span>
            <input
              list="cmp-objects"
              class="tuple w-56 rounded-md border border-slate-700 bg-slate-950 px-3 py-2 text-sm text-slate-100"
              [(ngModel)]="objectRef"
            />
            <datalist id="cmp-objects">
              @for (entity of catalog.allObjects(); track entity.objectRef) {
                <option [value]="entity.objectRef">{{ entity.name }}</option>
              }
            </datalist>
          </label>

          <button
            type="button"
            class="rounded-md bg-violet-600 px-4 py-2 text-sm font-semibold text-white hover:bg-violet-500 disabled:opacity-50"
            [disabled]="loading()"
            (click)="run()"
          >
            Preguntar a los dos
          </button>
        </div>

        <div class="mt-3 flex flex-wrap gap-2 text-xs">
          <span class="text-slate-500">Casos donde discrepan:</span>
          @for (preset of presets; track preset.label) {
            <button
              type="button"
              class="rounded border border-slate-700 px-2 py-0.5 text-slate-400 hover:border-violet-500 hover:text-violet-300"
              [title]="preset.hint"
              (click)="apply(preset)"
            >
              {{ preset.label }}
            </button>
          }
        </div>
      </pg-panel>

      @if (result(); as comparison) {
        <div class="grid gap-4 lg:grid-cols-2">
          @for (answer of [comparison.rebac, comparison.rbac]; track answer.model) {
            <pg-panel [title]="answer.model">
              <div class="mb-3 flex items-center gap-3">
                <span
                  class="rounded-lg px-3 py-1.5 text-sm font-bold tracking-wider"
                  [class]="
                    answer.allowed
                      ? 'bg-emerald-500/15 text-emerald-300'
                      : 'bg-rose-500/15 text-rose-300'
                  "
                >
                  {{ answer.allowed ? '✓ ALLOW' : '✕ DENY' }}
                </span>
              </div>

              <ol class="mb-3 space-y-1.5">
                @for (step of answer.reasoning; track $index) {
                  <li class="flex gap-2 text-xs leading-relaxed text-slate-400">
                    <span class="shrink-0 text-slate-600">{{ $index + 1 }}.</span>
                    <span>{{ step }}</span>
                  </li>
                }
              </ol>
            </pg-panel>
          }
        </div>

        <pg-panel
          [title]="comparison.agree ? 'Coinciden' : '⚠️ Discrepan — aquí está lo interesante'"
        >
          <p class="text-sm leading-relaxed text-slate-300">{{ comparison.analysis }}</p>
        </pg-panel>

        <pg-panel
          title="El coste de cada modelo"
          hint="Estas cifras son del mismo escenario, expresado de las dos formas. Es la comparación más honesta que se puede hacer entre los dos modelos."
        >
          <div class="grid gap-4 sm:grid-cols-2">
            <div class="rounded-lg border border-slate-800 bg-slate-950/60 p-4">
              <h3 class="mb-3 text-xs font-semibold tracking-wide text-slate-400 uppercase">
                RBAC
              </h3>
              <dl class="space-y-1.5 text-sm">
                <div class="flex justify-between">
                  <dt class="text-slate-400">Roles</dt>
                  <dd class="font-semibold text-slate-200">{{ comparison.rbacStats.roles }}</dd>
                </div>
                <div class="flex justify-between">
                  <dt class="text-slate-400">Permisos enumerados</dt>
                  <dd class="font-semibold text-slate-200">
                    {{ comparison.rbacStats.permissions }}
                  </dd>
                </div>
                <div class="flex justify-between">
                  <dt class="text-slate-400">Asignaciones</dt>
                  <dd class="font-semibold text-slate-200">
                    {{ comparison.rbacStats.userRoleAssignments }}
                  </dd>
                </div>
                <div class="flex justify-between border-t border-slate-800 pt-1.5">
                  <dt class="text-slate-300">Total de filas</dt>
                  <dd class="font-bold text-rose-300">{{ comparison.rbacStats.totalRows }}</dd>
                </div>
              </dl>
            </div>

            <div class="rounded-lg border border-slate-800 bg-slate-950/60 p-4">
              <h3 class="mb-3 text-xs font-semibold tracking-wide text-slate-400 uppercase">
                ReBAC
              </h3>
              <dl class="space-y-1.5 text-sm">
                <div class="flex justify-between">
                  <dt class="text-slate-400">Tipos de relación</dt>
                  <dd class="font-semibold text-slate-200">1 (todo son tuplas)</dd>
                </div>
                <div class="flex justify-between">
                  <dt class="text-slate-400">Tablas de permisos</dt>
                  <dd class="font-semibold text-slate-200">0</dd>
                </div>
                <div class="flex justify-between border-t border-slate-800 pt-1.5">
                  <dt class="text-slate-300">Total de tuplas</dt>
                  <dd class="font-bold text-emerald-300">42</dd>
                </div>
              </dl>
            </div>
          </div>

          <div
            class="mt-4 rounded-lg border border-amber-500/30 bg-amber-500/10 p-4 text-sm leading-relaxed text-amber-100"
          >
            <b>La cifra que de verdad importa.</b> Añadir un solo recurso nuevo a Project Alpha
            exige insertar <b>{{ comparison.rbacStats.rowsToAddOneResource }}</b> filas en RBAC —
            una por cada rol que debería alcanzarlo, multiplicada por las acciones. En ReBAC
            hace falta <b>una</b>: la que dice de qué cuelga. Todo lo demás se deduce al
            preguntar.
            <br /><br />
            Y lo mismo cada vez que alguien mueve una carpeta, entra en un equipo o se crea un
            proyecto. El coste de RBAC no está en el motor —que es diez veces más simple— sino
            en mantener esas filas sincronizadas con la realidad para siempre.
          </div>
        </pg-panel>
      }
    </div>
  `,
})
export class ComparisonComponent implements OnInit {
  private readonly api = inject(BusinessApi);
  protected readonly catalog = inject(CatalogStore);
  protected readonly identity = inject(IdentityStore);

  protected userId = 'juan';
  protected action = 'edit';
  protected objectRef = 'resource:a';

  protected readonly actions = ['view', 'edit', 'delete'];

  protected readonly result = signal<Comparison | null>(null);
  protected readonly loading = signal(false);

  /**
   * Preajustes elegidos para llevar directamente a los casos donde los modelos discrepan, que
   * son los que enseñan algo. Buscarlos a mano lleva un rato.
   */
  protected readonly presets = [
    {
      label: 'Pedro ve Resource A',
      userId: 'pedro',
      action: 'view',
      objectRef: 'resource:a',
      hint: 'ReBAC concede por el grupo Seguridad. RBAC no tiene ninguna fila para ese acceso: nadie lo materializó.',
    },
    {
      label: 'Pedro ve Resource B',
      userId: 'pedro',
      action: 'view',
      objectRef: 'resource:b',
      hint: 'Herencia a través de dos carpetas. En RBAC habría que haber propagado el permiso hacia abajo al concederlo.',
    },
    {
      label: 'María edita Resource E',
      userId: 'maria',
      action: 'edit',
      objectRef: 'resource:e',
      hint: 'Administradora de la organización. En RBAC hay que enumerar cada objeto de Acme, uno por uno.',
    },
    {
      label: 'Ana ve Resource D',
      userId: 'ana',
      action: 'view',
      objectRef: 'resource:d',
      hint: 'Exclusión explícita: ReBAC deniega pese a tener camino. RBAC no sabe expresar «todo menos esto».',
    },
  ];

  async ngOnInit(): Promise<void> {
    await this.run();
  }

  protected apply(preset: { userId: string; action: string; objectRef: string }): void {
    this.userId = preset.userId;
    this.action = preset.action;
    this.objectRef = preset.objectRef;
    void this.run();
  }

  protected async run(): Promise<void> {
    this.loading.set(true);

    try {
      this.result.set(await this.api.compare(this.userId, this.action, this.objectRef));
    } finally {
      this.loading.set(false);
    }
  }
}
