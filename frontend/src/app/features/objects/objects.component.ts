import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AccessControlApi } from '../../core/api/access-control.api';
import { ListObjectsComparison } from '../../core/models/access-control.models';
import { CatalogStore } from '../../core/state/catalog.store';
import { IdentityStore } from '../../core/state/identity.store';
import { MetricsComponent, ObjectChipComponent, PanelComponent } from '../../shared/ui.components';

/**
 * Listado de objetos autorizados: «dame todo lo que Juan puede ver».
 *
 * Responde la misma pregunta con las dos estrategias y las pone lado a lado. Es el punto 11
 * del enunciado, y probablemente la pantalla con más contenido técnico del laboratorio,
 * porque enseña tres cosas a la vez:
 *
 *  1. Que el resultado es idéntico. Si alguna vez no lo fuera, sería un bug.
 *  2. Que el coste no lo es, y en qué dirección.
 *  3. Que la estrategia naive sabe algo que la inversa no puede saber: por qué NO ves lo que
 *     no ves. La inversa nunca llega a mirar esos objetos.
 */
@Component({
  selector: 'pg-objects',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, PanelComponent, ObjectChipComponent, MetricsComponent],
  template: `
    <div class="mx-auto max-w-6xl space-y-5">
      <header>
        <h1 class="text-xl font-bold text-slate-100">Listado autorizado</h1>
        <p class="mt-1 max-w-3xl text-sm leading-relaxed text-slate-400">
          <code class="tuple">Check</code> pregunta por un objeto concreto.
          <code class="tuple">ListObjects</code> pregunta por todos a la vez, y es el problema
          difícil: hay que recorrer el grafo al revés, desde el sujeto.
        </p>
      </header>

      <pg-panel title="La consulta">
        <div class="flex flex-wrap items-end gap-3">
          <label class="block">
            <span class="mb-1 block text-xs text-slate-400">Sujeto</span>
            <input
              list="obj-subjects"
              class="tuple w-56 rounded-md border border-slate-700 bg-slate-950 px-3 py-2 text-sm text-slate-100"
              [(ngModel)]="subject"
            />
            <datalist id="obj-subjects">
              @for (option of catalog.allSubjects(); track option) {
                <option [value]="option"></option>
              }
            </datalist>
          </label>

          <label class="block">
            <span class="mb-1 block text-xs text-slate-400">Permiso</span>
            <select
              class="tuple rounded-md border border-slate-700 bg-slate-950 px-3 py-2 text-sm text-slate-100"
              [(ngModel)]="relation"
            >
              @for (option of relations; track option) {
                <option [value]="option">{{ option }}</option>
              }
            </select>
          </label>

          <label class="block">
            <span class="mb-1 block text-xs text-slate-400">Tipo de objeto</span>
            <select
              class="tuple rounded-md border border-slate-700 bg-slate-950 px-3 py-2 text-sm text-slate-100"
              [(ngModel)]="objectType"
            >
              @for (option of objectTypes; track option) {
                <option [value]="option">{{ option }}</option>
              }
            </select>
          </label>

          <button
            type="button"
            class="rounded-md bg-violet-600 px-4 py-2 text-sm font-semibold text-white hover:bg-violet-500 disabled:opacity-50"
            [disabled]="loading()"
            (click)="run()"
          >
            {{ loading() ? 'Calculando…' : 'Comparar estrategias' }}
          </button>

          <button
            type="button"
            class="rounded-md border border-slate-700 px-3 py-2 text-sm text-slate-300 hover:bg-slate-800"
            (click)="useCurrentUser()"
          >
            Usar «{{ identity.current()?.name ?? 'yo' }}»
          </button>
        </div>
      </pg-panel>

      @if (result(); as comparison) {
        <pg-panel
          [title]="comparison.sameResult ? 'Mismo resultado, distinto coste' : '⚠️ Los resultados DIFIEREN'"
          hint="Las dos estrategias deben devolver siempre el mismo conjunto: la naive es un simple bucle de checks y sirve de referencia."
        >
          <p class="text-sm leading-relaxed text-slate-300">{{ comparison.verdict }}</p>
        </pg-panel>

        <div class="grid gap-4 lg:grid-cols-2">
          <!-- Naive -->
          <pg-panel
            title="Naive · enumerar y comprobar"
            hint="Recorre TODO el catálogo del tipo y hace un Check completo por cada objeto. Coste proporcional al tamaño del catálogo."
          >
            <ul class="space-y-2">
              @for (item of comparison.naive.objects; track item.object) {
                <li class="flex items-center gap-2">
                  <span class="text-emerald-400">✓</span>
                  <pg-object-chip [reference]="item.object" />
                  <span class="text-xs text-slate-500">{{
                    catalog.displayName(item.object)
                  }}</span>
                </li>
              }
            </ul>

            @if (comparison.naive.denied.length > 0) {
              <div class="mt-4 border-t border-slate-800 pt-3">
                <p class="mb-2 text-xs text-slate-500">
                  Y esto es lo que <b>no</b> ve — información que solo la estrategia naive puede
                  dar, porque es la única que mira todos los objetos:
                </p>
                <ul class="space-y-2">
                  @for (item of comparison.naive.denied; track item.object) {
                    <li>
                      <div class="flex items-center gap-2">
                        <span class="text-rose-400">✕</span>
                        <pg-object-chip [reference]="item.object" />
                      </div>
                      <p class="mt-1 pl-6 text-xs leading-relaxed text-slate-500">
                        {{ item.reason }}
                      </p>
                      <a
                        class="mt-1 ml-6 inline-block text-xs text-violet-400 hover:text-violet-300"
                        [routerLink]="['/explorer']"
                        [queryParams]="{
                          subject: comparison.naive.subject,
                          relation: comparison.naive.relation,
                          object: item.object,
                        }"
                      >
                        ¿Por qué no? →
                      </a>
                    </li>
                  }
                </ul>
              </div>
            }

            <div class="mt-4 border-t border-slate-800 pt-3">
              <pg-metrics [metrics]="comparison.naive.metrics" />
            </div>
          </pg-panel>

          <!-- Expansión inversa -->
          <pg-panel
            title="Expansión inversa · partir del sujeto"
            hint="Calcula a qué conjuntos pertenece el sujeto, entra por el índice inverso y propaga por la jerarquía. Coste proporcional a lo que el sujeto puede ver."
          >
            <ul class="space-y-2">
              @for (item of comparison.reverse.objects; track item.object) {
                <li class="flex items-center gap-2">
                  <span class="text-emerald-400">✓</span>
                  <pg-object-chip [reference]="item.object" />
                  <span class="text-xs text-slate-500">{{
                    catalog.displayName(item.object)
                  }}</span>
                </li>
              }
            </ul>

            <p class="mt-4 border-t border-slate-800 pt-3 text-xs leading-relaxed text-slate-500">
              Aquí no hay lista de denegados, y no es una carencia de la implementación: la
              expansión inversa <b>nunca llega a mirar</b> los objetos a los que el sujeto no
              tiene acceso. Para responder «¿por qué no puedo ver esto?» hace falta un Check
              concreto sobre ese objeto.
            </p>

            @if (comparison.reverse.metrics.confirmationChecks > 0) {
              <p
                class="mt-3 rounded-md border border-amber-500/30 bg-amber-500/10 p-3 text-xs leading-relaxed text-amber-200"
              >
                <b
                  >Ha hecho {{ comparison.reverse.metrics.confirmationChecks }} checks de
                  confirmación.</b
                >
                El modelo tiene una regla no monótona en el camino (un
                <code class="tuple">but not</code> o un <code class="tuple">and</code>) que no se
                puede invertir, así que la expansión solo produce candidatos y hay que
                comprobarlos uno a uno. Es el punto débil de esta estrategia, y con un catálogo
                pequeño puede hacer que salga más cara que la naive.
              </p>
            }

            <div class="mt-4 border-t border-slate-800 pt-3">
              <pg-metrics [metrics]="comparison.reverse.metrics" />
            </div>
          </pg-panel>
        </div>
      }
    </div>
  `,
})
export class ObjectsComponent implements OnInit {
  private readonly api = inject(AccessControlApi);
  protected readonly catalog = inject(CatalogStore);
  protected readonly identity = inject(IdentityStore);

  protected subject = 'user:juan';
  protected relation = 'can_view';
  protected objectType = 'project';

  protected readonly relations = ['can_view', 'can_edit', 'can_delete'];
  protected readonly objectTypes = ['project', 'resource', 'folder', 'team', 'organization'];

  protected readonly result = signal<ListObjectsComparison | null>(null);
  protected readonly loading = signal(false);

  async ngOnInit(): Promise<void> {
    await this.run();
  }

  protected useCurrentUser(): void {
    this.subject = this.identity.currentSubject();
    void this.run();
  }

  protected async run(): Promise<void> {
    this.loading.set(true);

    try {
      this.result.set(
        await this.api.compareListStrategies(this.subject, this.relation, this.objectType),
      );
    } finally {
      this.loading.set(false);
    }
  }
}
