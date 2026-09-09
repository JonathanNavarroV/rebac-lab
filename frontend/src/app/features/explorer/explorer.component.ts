import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AccessControlApi } from '../../core/api/access-control.api';
import { CheckResponse } from '../../core/models/access-control.models';
import { CatalogStore } from '../../core/state/catalog.store';
import { IdentityStore } from '../../core/state/identity.store';
import {
  DecisionBadgeComponent,
  MetricsComponent,
  ObjectChipComponent,
  PanelComponent,
} from '../../shared/ui.components';
import { TraceTreeComponent } from '../../shared/trace-tree.component';

/** Los permisos que tiene sentido preguntar, con una pista de qué son. */
const RELATIONS = [
  { value: 'can_view', hint: 'Permiso derivado. Se concede por muchas vías: viewer, edición, herencia, compartición...' },
  { value: 'can_edit', hint: 'Permiso derivado. Editor directo, propietario, o administrador de la organización.' },
  { value: 'can_delete', hint: 'Permiso derivado, más estricto: no basta con poder editar.' },
  { value: 'can_publish', hint: 'Intersección: hay que poder editar Y ser miembro de la organización.' },
  { value: 'viewer', hint: 'Relación DIRECTA (un hecho escrito), no un permiso derivado.' },
  { value: 'editor', hint: 'Relación directa.' },
  { value: 'owner', hint: 'Relación directa.' },
  { value: 'member', hint: 'Relación directa. Solo tiene sentido sobre equipos, grupos y organizaciones.' },
  { value: 'parent', hint: 'Relación directa que forma la jerarquía.' },
];

/**
 * El Authorization Explorer: la pantalla donde se responde "¿por qué?".
 *
 * Está construida alrededor de una idea: **el ALLOW o el DENY es lo menos interesante de la
 * respuesta**. Lo que se aprende está en el árbol de evaluación, en los caminos que
 * concedieron acceso, en las ramas que fallaron y —cuando deniega— en las tuplas que
 * bastaría crear.
 */
@Component({
  selector: 'pg-explorer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    PanelComponent,
    ObjectChipComponent,
    DecisionBadgeComponent,
    MetricsComponent,
    TraceTreeComponent,
  ],
  template: `
    <div class="mx-auto max-w-6xl space-y-5">
      <header>
        <h1 class="text-xl font-bold text-slate-100">Authorization Explorer</h1>
        <p class="mt-1 max-w-3xl text-sm leading-relaxed text-slate-400">
          Haz una pregunta de autorización y mira cómo la responde el motor. La decisión es lo de
          menos: lo que enseña es el recorrido, los caminos alternativos y, si deniega, qué
          faltaba exactamente.
        </p>
      </header>

      <pg-panel
        title="La pregunta"
        hint="El sujeto puede ser una persona o un conjunto. Prueba a preguntar por «team:backend#member» en lugar de por una persona: el motor responde igual, porque para él un conjunto y un individuo ocupan el mismo hueco."
      >
        <div class="grid gap-4 md:grid-cols-3">
          <label class="block">
            <span class="mb-1 block text-xs font-medium text-slate-400">Sujeto</span>
            <input
              list="subjects"
              class="tuple w-full rounded-md border border-slate-700 bg-slate-950 px-3 py-2 text-slate-100 focus:border-violet-500 focus:outline-none"
              [(ngModel)]="subject"
              (ngModelChange)="onInputChange()"
              placeholder="user:juan"
            />
            <datalist id="subjects">
              @for (option of catalog.allSubjects(); track option) {
                <option [value]="option"></option>
              }
            </datalist>
          </label>

          <label class="block">
            <span class="mb-1 block text-xs font-medium text-slate-400">Relación o permiso</span>
            <select
              class="tuple w-full rounded-md border border-slate-700 bg-slate-950 px-3 py-2 text-slate-100 focus:border-violet-500 focus:outline-none"
              [(ngModel)]="relation"
              (ngModelChange)="onInputChange()"
            >
              @for (option of relations; track option.value) {
                <option [value]="option.value" [title]="option.hint">{{ option.value }}</option>
              }
            </select>
            <span class="mt-1 block text-xs text-slate-500">{{ relationHint() }}</span>
          </label>

          <label class="block">
            <span class="mb-1 block text-xs font-medium text-slate-400">Objeto</span>
            <input
              list="objects"
              class="tuple w-full rounded-md border border-slate-700 bg-slate-950 px-3 py-2 text-slate-100 focus:border-violet-500 focus:outline-none"
              [(ngModel)]="object"
              (ngModelChange)="onInputChange()"
              placeholder="project:alpha"
            />
            <datalist id="objects">
              @for (entity of catalog.allObjects(); track entity.objectRef) {
                <option [value]="entity.objectRef">{{ entity.name }}</option>
              }
            </datalist>
          </label>
        </div>

        <div class="mt-4 flex flex-wrap items-center gap-3">
          <button
            type="button"
            class="rounded-md bg-violet-600 px-4 py-2 text-sm font-semibold text-white hover:bg-violet-500 disabled:opacity-50"
            [disabled]="loading()"
            (click)="run()"
          >
            {{ loading() ? 'Evaluando…' : 'Evaluar' }}
          </button>

          <button
            type="button"
            class="rounded-md border border-slate-700 px-3 py-2 text-sm text-slate-300 hover:bg-slate-800"
            (click)="useCurrentUser()"
          >
            Usar «{{ identity.current()?.name ?? 'yo' }}»
          </button>

          <label class="flex items-center gap-2 text-xs text-slate-400">
            <input type="checkbox" class="accent-violet-500" [(ngModel)]="explain" />
            <span
              title="Explora todas las ramas en lugar de parar en el primer ALLOW. Más caro, pero es lo único que permite ver los múltiples caminos."
            >
              Explicación completa
            </span>
          </label>

          <label class="flex items-center gap-2 text-xs text-slate-400">
            <span title="Bajar el límite convierte un acceso heredado desde muy arriba en un DENY. Prueba con 3 sobre resource:b.">
              Profundidad máx.
            </span>
            <input
              type="number"
              min="1"
              max="25"
              class="w-16 rounded-md border border-slate-700 bg-slate-950 px-2 py-1 text-slate-100"
              [(ngModel)]="maxDepth"
            />
          </label>
        </div>
      </pg-panel>

      @if (error(); as message) {
        <div class="rounded-lg border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-200">
          {{ message }}
        </div>
      }

      @if (result(); as decision) {
        <!-- ── La decisión ─────────────────────────────────────────────── -->
        <pg-panel title="La decisión">
          <div class="flex flex-wrap items-center gap-3">
            <pg-decision-badge [allowed]="decision.allowed" />
            <pg-object-chip [reference]="decision.subject" />
            <span class="tuple text-slate-500">--{{ decision.relation }}--&gt;</span>
            <pg-object-chip [reference]="decision.object" />
          </div>

          <p class="mt-4 text-sm leading-relaxed text-slate-300">{{ decision.reason }}</p>

          <div class="mt-4 border-t border-slate-800 pt-3">
            <pg-metrics [metrics]="decision.metrics" />
          </div>
        </pg-panel>

        <!-- ── Los caminos ─────────────────────────────────────────────── -->
        @if (decision.paths.length > 0) {
          <pg-panel
            title="Caminos de autorización"
            [hint]="
              decision.paths.length > 1
                ? 'Hay más de un camino. Esto es importante al revocar: quitar uno solo NO retira el acceso, y es el error de seguridad más típico en un sistema ReBAC.'
                : 'Un único camino. Romperlo retira el acceso.'
            "
          >
            <ol class="space-y-3">
              @for (path of decision.paths; track $index) {
                <li class="rounded-lg border border-slate-800 bg-slate-950/60 p-3">
                  <div class="mb-2 flex items-center gap-2 text-xs text-slate-500">
                    <span
                      class="rounded bg-emerald-500/15 px-1.5 py-0.5 font-semibold text-emerald-300"
                    >
                      camino {{ $index + 1 }}
                    </span>
                    <span>{{ path.length }} salto(s)</span>
                  </div>

                  <p class="tuple mb-2 text-sm text-slate-200">{{ path.chain }}</p>

                  <ul class="space-y-1 text-xs text-slate-400">
                    @for (step of path.steps; track $index) {
                      <li class="flex gap-2">
                        <span class="text-slate-600">•</span>
                        <span>{{ step.description }}</span>
                      </li>
                    }
                  </ul>
                </li>
              }
            </ol>
          </pg-panel>
        }

        <!-- ── Qué faltaba ─────────────────────────────────────────────── -->
        @if (decision.suggestedTuples.length > 0) {
          <pg-panel
            title="Bastaría con UNA de estas relaciones"
            hint="Cada línea es una vía distinta por la que el modelo permite conceder este permiso. Fíjate especialmente en las que apuntan a un conjunto: no hace falta tocar a la persona, basta con dar acceso a un grupo del que ya forma parte."
          >
            <ul class="space-y-2">
              @for (tuple of decision.suggestedTuples; track tuple) {
                <li class="flex flex-wrap items-center gap-2">
                  <code class="tuple rounded bg-slate-950 px-2 py-1 text-slate-300">{{ tuple }}</code>
                  <button
                    type="button"
                    class="rounded border border-emerald-600/40 bg-emerald-500/10 px-2 py-1 text-xs text-emerald-300 hover:bg-emerald-500/20 disabled:opacity-50"
                    [disabled]="creating()"
                    (click)="createAndRerun(tuple)"
                  >
                    Crear y volver a evaluar
                  </button>
                </li>
              }
            </ul>
          </pg-panel>
        }

        <!-- ── La traza ────────────────────────────────────────────────── -->
        @if (decision.trace; as trace) {
          <pg-panel
            title="Árbol de evaluación"
            hint="El recorrido completo, incluidas las ramas que denegaron. Las que concedieron salen abiertas; pulsa en cualquiera para plegarla o desplegarla. Pasa el ratón por las etiquetas (_this, tuple_to_userset…) para ver qué hace cada regla."
          >
            <pg-trace-tree [node]="trace" />
          </pg-panel>
        }
      }
    </div>
  `,
})
export class ExplorerComponent implements OnInit {
  private readonly api = inject(AccessControlApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly catalog = inject(CatalogStore);
  protected readonly identity = inject(IdentityStore);

  protected readonly relations = RELATIONS;

  protected subject = 'user:juan';
  protected relation = 'can_edit';
  protected object = 'project:alpha';
  protected explain = true;
  protected maxDepth = 25;

  protected readonly result = signal<CheckResponse | null>(null);
  protected readonly loading = signal(false);
  protected readonly creating = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly relationHint = computed(
    () => RELATIONS.find((option) => option.value === this.relation)?.hint ?? '',
  );

  ngOnInit(): void {
    // Los parámetros de la URL permiten enlazar una pregunta concreta desde otras pantallas
    // (los casos guiados, el 403 del espacio de trabajo, la auditoría). Es lo que hace que
    // "investigar esto en el Explorer" sea un enlace y no una reconstrucción manual.
    const params = this.route.snapshot.queryParamMap;

    this.subject = params.get('subject') ?? this.subject;
    this.relation = params.get('relation') ?? this.relation;
    this.object = params.get('object') ?? this.object;

    void this.run();
  }

  protected onInputChange(): void {
    this.error.set(null);
  }

  protected useCurrentUser(): void {
    this.subject = this.identity.currentSubject();
    void this.run();
  }

  protected async run(): Promise<void> {
    if (!this.subject || !this.relation || !this.object) {
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    // Se refleja la pregunta en la URL para que sea compartible y para que el botón "atrás"
    // del navegador funcione como se espera.
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { subject: this.subject, relation: this.relation, object: this.object },
      replaceUrl: true,
    });

    try {
      const request = {
        subject: this.subject,
        relation: this.relation,
        object: this.object,
        explain: this.explain,
        maxDepth: this.maxDepth,
      };

      this.result.set(
        this.explain ? await this.api.explain(request) : await this.api.check(request),
      );
    } catch (error) {
      this.result.set(null);
      this.error.set(this.describeError(error));
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * Crea una de las tuplas sugeridas y repite la pregunta.
   *
   * Es el gesto que mejor resume el laboratorio: un DENY, una relación nueva, y el mismo
   * check pasa a ALLOW sin haber tocado ni el código ni el modelo.
   */
  protected async createAndRerun(tuple: string): Promise<void> {
    this.creating.set(true);

    try {
      await this.api.createRelationship(tuple);
      await this.run();
    } catch (error) {
      this.error.set(this.describeError(error));
    } finally {
      this.creating.set(false);
    }
  }

  private describeError(error: unknown): string {
    const problem = error as { error?: { errors?: Record<string, string[]>; title?: string } };
    const errors = problem?.error?.errors;

    if (errors) {
      return Object.entries(errors)
        .map(([field, messages]) => `${field}: ${messages.join(' ')}`)
        .join(' · ');
    }

    return 'No se ha podido evaluar. ¿Está levantado el módulo de control de acceso en :15101?';
  }
}
