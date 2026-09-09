import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AccessControlApi } from '../../core/api/access-control.api';
import { AuthorizationModel, ModelTemplate } from '../../core/models/access-control.models';
import { PanelComponent } from '../../shared/ui.components';

const KIND_HELP: Record<string, string> = {
  _this: 'Solo tuplas escritas directamente. Es el caso base y la única regla que toca la base de datos.',
  computed_userset: 'Otra relación del mismo objeto.',
  tuple_to_userset: 'Herencia: sube por una relación y pregunta arriba.',
  union: 'Cualquiera de las vías basta. De aquí salen los múltiples caminos.',
  intersection: 'Hay que cumplir todas las condiciones.',
  exclusion: 'Concede salvo que la rama excluida también conceda.',
};

/**
 * Visor del modelo de autorización.
 *
 * Muestra el DSL tal cual se escribió (con sus comentarios) y, al lado, la descomposición por
 * tipos y relaciones. La distinción visual entre relaciones **directas** y **derivadas** es lo
 * que más ayuda al principio: las primeras son hechos que alguien escribe, las segundas son
 * conclusiones que el motor calcula y que nunca deben materializarse.
 *
 * Permite además publicar otro modelo. Publicar la variante «sin herencia en carpetas» y
 * repetir el caso C es la forma más rápida de entender qué hace exactamente un
 * `tuple_to_userset`.
 */
@Component({
  selector: 'pg-model',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PanelComponent],
  template: `
    <div class="mx-auto max-w-6xl space-y-5">
      <header>
        <h1 class="text-xl font-bold text-slate-100">Modelo de autorización</h1>
        <p class="mt-1 max-w-3xl text-sm leading-relaxed text-slate-400">
          Qué relaciones existen y cómo se derivan. Es lo único que hay que cambiar para alterar
          la política de todo el sistema: ni una línea de negocio, ni una sola tupla.
        </p>
      </header>

      @if (models().length > 1) {
        <pg-panel
          title="Modelos publicados"
          hint="Son inmutables y versionados. Publicar uno nuevo no borra los anteriores, así que puedes responder la misma pregunta con dos modelos y comparar."
        >
          <ul class="space-y-2">
            @for (item of models(); track item.id) {
              <li
                class="flex flex-wrap items-center gap-3 rounded-lg border p-3"
                [class]="
                  item.isCurrent
                    ? 'border-violet-500/40 bg-violet-500/5'
                    : 'border-slate-800 bg-slate-950/60'
                "
              >
                <div class="min-w-0 flex-1">
                  <p class="text-sm font-medium text-slate-100">
                    {{ item.name ?? item.id }}
                    @if (item.isCurrent) {
                      <span
                        class="ml-2 rounded bg-violet-500/20 px-1.5 py-0.5 text-[10px] font-semibold text-violet-300"
                        >VIGENTE</span
                      >
                    }
                  </p>
                  @if (item.description) {
                    <p class="mt-0.5 text-xs text-slate-500">{{ item.description }}</p>
                  }
                </div>
                <code class="tuple text-xs text-slate-600">{{ item.id }}</code>
                @if (!item.isCurrent) {
                  <button
                    type="button"
                    class="rounded border border-slate-700 px-2 py-1 text-xs text-slate-300 hover:bg-slate-800"
                    (click)="activate(item)"
                    title="Vuelve a publicarlo para que pase a ser el vigente. Después repite un caso guiado y compara."
                  >
                    Hacer vigente
                  </button>
                }
              </li>
            }
          </ul>
        </pg-panel>
      }

      @if (current(); as model) {
        <div class="grid gap-4 lg:grid-cols-2">
          <pg-panel
            title="El DSL"
            hint="La misma sintaxis que OpenFGA. Lo que aprendas aquí es directamente transferible."
          >
            <pre
              class="tuple max-h-[600px] overflow-auto rounded-lg bg-slate-950 p-4 text-xs leading-relaxed whitespace-pre text-slate-300"
              >{{ model.dsl }}</pre
            >
          </pg-panel>

          <pg-panel
            title="Tipos y relaciones"
            hint="Azul = relación directa (un hecho que se escribe como tupla). Violeta = relación derivada (una conclusión que el motor calcula; escribirla sería un error de modelado)."
          >
            <div class="max-h-[600px] space-y-4 overflow-auto">
              @for (type of model.types; track type.name) {
                <div>
                  <h3 class="tuple mb-1 text-sm font-bold text-slate-100">{{ type.name }}</h3>
                  @if (type.relations.length === 0) {
                    <p class="text-xs text-slate-500">
                      Sin relaciones propias. Un usuario nunca es objeto de una comprobación,
                      solo sujeto.
                    </p>
                  }
                  <ul class="space-y-1.5">
                    @for (relation of type.relations; track relation.name) {
                      <li class="rounded-md border border-slate-800 bg-slate-950/60 p-2">
                        <div class="flex flex-wrap items-center gap-2">
                          <code
                            class="tuple rounded px-1.5 py-0.5 text-xs font-semibold"
                            [class]="
                              relation.isDirectlyAssignable
                                ? 'bg-sky-500/15 text-sky-300'
                                : 'bg-violet-500/15 text-violet-300'
                            "
                            [title]="
                              relation.isDirectlyAssignable
                                ? 'Relación directa: se escribe como tupla.'
                                : 'Relación derivada: se calcula. Escribirla materializaría un permiso.'
                            "
                          >
                            {{ relation.name }}
                          </code>
                          <span class="tuple text-xs text-slate-400">{{
                            relation.expression
                          }}</span>
                          <span
                            class="ml-auto rounded bg-slate-800 px-1.5 py-0.5 text-[10px] text-slate-400"
                            [title]="kindHelp(relation.kind)"
                            >{{ relation.kind }}</span
                          >
                        </div>
                        @if (relation.comment) {
                          <p class="mt-1 text-xs leading-relaxed text-slate-500">
                            {{ relation.comment }}
                          </p>
                        }
                      </li>
                    }
                  </ul>
                </div>
              }
            </div>
          </pg-panel>
        </div>
      }

      <pg-panel
        title="Publicar otro modelo"
        hint="El experimento recomendado: publica «Sin herencia en carpetas» y vuelve a los casos guiados. El caso C pasará de ALLOW a DENY sin que ninguna relación haya cambiado."
      >
        <div class="mb-3 flex flex-wrap gap-2">
          @for (template of templates(); track template.key) {
            <button
              type="button"
              class="rounded border border-slate-700 px-3 py-1.5 text-xs text-slate-300 hover:border-violet-500 hover:text-violet-300"
              [title]="template.description"
              (click)="draft = template.dsl"
            >
              {{ template.name }}
            </button>
          }
        </div>

        <textarea
          class="tuple h-64 w-full rounded-lg border border-slate-700 bg-slate-950 p-3 text-xs text-slate-200 focus:border-violet-500 focus:outline-none"
          [(ngModel)]="draft"
          placeholder="model&#10;  schema 1.1&#10;&#10;type user&#10;…"
        ></textarea>

        <div class="mt-3 flex items-center gap-3">
          <button
            type="button"
            class="rounded-md bg-violet-600 px-4 py-2 text-sm font-semibold text-white hover:bg-violet-500 disabled:opacity-50"
            [disabled]="publishing() || !draft.trim()"
            (click)="publish()"
          >
            Publicar
          </button>
          @if (message(); as text) {
            <span class="text-sm text-emerald-300">{{ text }}</span>
          }
        </div>

        @if (errors().length > 0) {
          <div class="mt-3 rounded-lg border border-rose-500/30 bg-rose-500/10 p-3">
            <p class="mb-2 text-sm font-semibold text-rose-200">
              El modelo no es válido. Se listan todos los errores, no solo el primero:
            </p>
            <ul class="space-y-1">
              @for (error of errors(); track $index) {
                <li class="text-xs leading-relaxed text-rose-200">· {{ error }}</li>
              }
            </ul>
          </div>
        }
      </pg-panel>
    </div>
  `,
})
export class ModelComponent implements OnInit {
  private readonly api = inject(AccessControlApi);

  protected draft = '';

  protected readonly models = signal<AuthorizationModel[]>([]);
  protected readonly current = signal<AuthorizationModel | null>(null);
  protected readonly templates = signal<ModelTemplate[]>([]);
  protected readonly publishing = signal(false);
  protected readonly message = signal<string | null>(null);
  protected readonly errors = signal<string[]>([]);

  async ngOnInit(): Promise<void> {
    await this.load();
    this.templates.set(await this.api.getModelTemplates());
  }

  protected kindHelp(kind: string): string {
    return KIND_HELP[kind] ?? kind;
  }

  protected async activate(model: AuthorizationModel): Promise<void> {
    // Republicar un modelo existente lo vuelve a poner el último de la lista y por tanto el
    // vigente. Como el identificador es un hash del contenido, no se crea una versión nueva.
    await this.api.publishModel(model.dsl, model.name ?? undefined, model.description ?? undefined);
    await this.load();
  }

  protected async publish(): Promise<void> {
    this.publishing.set(true);
    this.errors.set([]);
    this.message.set(null);

    try {
      const published = await this.api.publishModel(this.draft, 'Publicado desde el laboratorio');

      this.message.set(`Publicado. Ahora es el modelo vigente (${published.id}).`);
      this.draft = '';
      await this.load();
    } catch (error) {
      const problem = error as { error?: { errors?: Record<string, string[]> } };
      const found = problem?.error?.errors;

      this.errors.set(
        found
          ? Object.entries(found).flatMap(([line, messages]) =>
              messages.map((message) => `${line}: ${message}`),
            )
          : ['No se ha podido publicar el modelo.'],
      );
    } finally {
      this.publishing.set(false);
    }
  }

  private async load(): Promise<void> {
    const models = await this.api.getModels();

    this.models.set(models);
    this.current.set(models.find((model) => model.isCurrent) ?? models[0] ?? null);
  }
}
