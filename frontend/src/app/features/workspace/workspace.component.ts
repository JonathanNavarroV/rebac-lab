import { ChangeDetectionStrategy, Component, OnInit, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { BusinessApi } from '../../core/api/business.api';
import { ForbiddenPayload, ProjectList, ResourceList } from '../../core/models/business.models';
import { IdentityStore } from '../../core/state/identity.store';
import { ObjectChipComponent, PanelComponent } from '../../shared/ui.components';

/**
 * El espacio de trabajo: la aplicación de negocio de verdad.
 *
 * Es la pantalla menos vistosa y la que cierra el círculo. Todo lo demás del laboratorio
 * pregunta al módulo de control de acceso <i>directamente</i>; aquí se ve el flujo real: el
 * negocio consulta sus propias entidades, le pregunta al módulo qué puede hacer el usuario, y
 * pinta o esconde botones en consecuencia — sin saber en ningún momento por qué.
 *
 * Y cuando una operación se deniega, el 403 trae el motivo que redactó el módulo, con un
 * enlace para investigarlo en el Explorer.
 */
@Component({
  selector: 'pg-workspace',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, PanelComponent, ObjectChipComponent],
  template: `
    <div class="mx-auto max-w-6xl space-y-5">
      <header>
        <h1 class="text-xl font-bold text-slate-100">Espacio de trabajo</h1>
        <p class="mt-1 max-w-3xl text-sm leading-relaxed text-slate-400">
          La aplicación de negocio, gobernada por el módulo de control de acceso. Cambia de
          identidad en la barra superior y observa cómo cambian la lista y los botones sin que
          nada más se haya movido.
        </p>
      </header>

      @if (projects(); as list) {
        <div
          class="flex flex-wrap items-center gap-x-6 gap-y-2 rounded-lg border border-slate-800 bg-slate-900/50 px-4 py-3 text-xs text-slate-400"
        >
          <span>
            Ves <b class="text-slate-200">{{ list.visible }}</b> de
            <b class="text-slate-200">{{ list.totalInCatalog }}</b> proyectos
          </span>
          <span
            title="Preguntas de autorización que ha costado pintar esta lista: una para el listado y tres por proyecto (editar, borrar, publicar)."
          >
            <b class="text-slate-200">{{ list.checksPerformed }}</b> checks
          </span>
          <span
            ><b class="text-slate-200">{{ list.authorizationMs }}</b> ms</span
          >
          <span
            class="rounded px-1.5 py-0.5 ring-1 ring-inset"
            [class]="
              list.authorizationMode === 'Remote'
                ? 'bg-amber-500/10 text-amber-300 ring-amber-500/30'
                : 'bg-slate-800 text-slate-400 ring-slate-700'
            "
          >
            {{ list.authorizationMode }}
          </span>
          <span class="text-slate-600">
            Cambia <code class="tuple">Authorization:Mode</code> a <code class="tuple">Remote</code>
            en appsettings y vuelve a mirar estos números.
          </span>
        </div>
      }

      @if (forbidden(); as denial) {
        <div class="rounded-lg border border-rose-500/30 bg-rose-500/10 p-4">
          <p class="text-sm font-semibold text-rose-200">{{ denial.detail }}</p>
          <p class="mt-2 text-xs leading-relaxed text-rose-200/80">{{ denial.reason }}</p>
          <a
            class="mt-2 inline-block text-xs font-medium text-violet-300 hover:text-violet-200"
            [routerLink]="['/explorer']"
            [queryParams]="{
              subject: denial.subject,
              relation: denial.relation,
              object: denial.object,
            }"
          >
            Investigar este DENY en el Explorer →
          </a>
        </div>
      }

      <pg-panel
        title="Proyectos"
        hint="Los botones aparecen o no según lo que responda el módulo. El código del frontend no sabe qué es un equipo ni una organización: solo lee los flags canEdit, canDelete y canPublish."
      >
        @if (projects(); as list) {
          @if (list.projects.length === 0) {
            <p class="text-sm text-slate-400">
              No ves ningún proyecto. Prueba a cambiar de identidad, o a darte acceso desde la
              pantalla de Relaciones.
            </p>
          }

          <ul class="space-y-2">
            @for (project of list.projects; track project.id) {
              <li
                class="flex flex-wrap items-center gap-3 rounded-lg border border-slate-800 bg-slate-950/60 p-3"
              >
                <pg-object-chip [reference]="project.objectRef" />
                <span class="text-sm text-slate-200">{{ project.name }}</span>

                <span class="ml-auto flex flex-wrap gap-2">
                  <button
                    type="button"
                    class="rounded border px-2 py-1 text-xs"
                    [class]="
                      project.canEdit
                        ? 'border-slate-700 text-slate-300 hover:bg-slate-800'
                        : 'cursor-not-allowed border-slate-800 text-slate-600'
                    "
                    [disabled]="!project.canEdit"
                    [title]="
                      project.canEdit
                        ? 'can_edit = ALLOW'
                        : 'can_edit = DENY. El botón está deshabilitado porque el módulo dijo que no.'
                    "
                    (click)="rename(project.id, project.name)"
                  >
                    Renombrar
                  </button>

                  <button
                    type="button"
                    class="rounded border px-2 py-1 text-xs"
                    [class]="
                      project.canPublish
                        ? 'border-emerald-600/40 text-emerald-300 hover:bg-emerald-500/10'
                        : 'cursor-not-allowed border-slate-800 text-slate-600'
                    "
                    [disabled]="!project.canPublish"
                    title="can_publish exige poder editar Y ser miembro de la organización. Prueba con Sofía: puede editar pero no publicar."
                    (click)="publish(project.id)"
                  >
                    Publicar
                  </button>

                  <button
                    type="button"
                    class="rounded border px-2 py-1 text-xs"
                    [class]="
                      project.canDelete
                        ? 'border-rose-600/40 text-rose-300 hover:bg-rose-500/10'
                        : 'cursor-not-allowed border-slate-800 text-slate-600'
                    "
                    [disabled]="!project.canDelete"
                    title="can_delete es más estricta que can_edit: no basta con ser editor."
                    (click)="remove(project.id)"
                  >
                    Borrar
                  </button>
                </span>
              </li>
            }
          </ul>
        }
      </pg-panel>

      <pg-panel
        title="Recursos"
        hint="Compartir admite una persona, un equipo, un grupo o una organización entera. Es el MISMO endpoint en los cuatro casos: solo cambia la notación del sujeto."
      >
        @if (resources(); as list) {
          <p class="mb-3 text-xs text-slate-500">
            Ves {{ list.visible }} de {{ list.totalInCatalog }} recursos · {{ list.checksPerformed }}
            checks · {{ list.authorizationMs }} ms
          </p>

          <ul class="space-y-2">
            @for (resource of list.resources; track resource.id) {
              <li class="rounded-lg border border-slate-800 bg-slate-950/60 p-3">
                <div class="flex flex-wrap items-center gap-3">
                  <pg-object-chip [reference]="resource.objectRef" />
                  <span class="text-sm text-slate-200">{{ resource.name }}</span>
                  @if (resource.parentRef) {
                    <span class="tuple text-xs text-slate-600">en {{ resource.parentRef }}</span>
                  }

                  @if (resource.canEdit) {
                    <span class="ml-auto flex items-center gap-2">
                      <input
                        class="tuple w-52 rounded border border-slate-700 bg-slate-950 px-2 py-1 text-xs text-slate-200"
                        [value]="shareDraft()[resource.id] ?? ''"
                        (input)="setShareDraft(resource.id, $event)"
                        placeholder="team:frontend#member"
                        list="share-targets"
                      />
                      <button
                        type="button"
                        class="rounded border border-slate-700 px-2 py-1 text-xs text-slate-300 hover:bg-slate-800"
                        (click)="share(resource.id)"
                      >
                        Compartir
                      </button>
                    </span>
                  } @else {
                    <span class="ml-auto text-xs text-slate-600">solo lectura</span>
                  }
                </div>
              </li>
            }
          </ul>
        }

        <datalist id="share-targets">
          <option value="user:pedro">Una persona</option>
          <option value="team:frontend#member">Un equipo entero</option>
          <option value="group:seguridad#member">Un grupo</option>
          <option value="organization:acme#member">Toda una organización</option>
        </datalist>

        @if (shareResult(); as result) {
          <div
            class="mt-3 rounded-lg border border-emerald-500/30 bg-emerald-500/10 p-3 text-xs leading-relaxed text-emerald-200"
          >
            <code class="tuple">{{ result.tuple }}</code>
            <p class="mt-1">{{ result.explanation }}</p>
          </div>
        }
      </pg-panel>
    </div>
  `,
})
export class WorkspaceComponent implements OnInit {
  private readonly api = inject(BusinessApi);
  private readonly identity = inject(IdentityStore);

  protected readonly projects = signal<ProjectList | null>(null);
  protected readonly resources = signal<ResourceList | null>(null);
  protected readonly forbidden = signal<ForbiddenPayload | null>(null);
  protected readonly shareResult = signal<{ tuple: string; explanation: string } | null>(null);
  protected readonly shareDraft = signal<Record<string, string | undefined>>({});

  constructor() {
    // Al cambiar de identidad se recarga todo. Es el efecto más didáctico de la pantalla:
    // la misma lista, el mismo código, y contenido distinto porque el sujeto es otro.
    effect(() => {
      this.identity.currentId();
      void this.load();
    });
  }

  ngOnInit(): void {
    void this.load();
  }

  protected setShareDraft(resourceId: string, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.shareDraft.update((draft) => ({ ...draft, [resourceId]: value }));
  }

  protected async rename(id: string, currentName: string): Promise<void> {
    await this.guard(() => this.api.updateProject(id, `${currentName} ·`, null));
  }

  protected async publish(id: string): Promise<void> {
    await this.guard(() => this.api.publishProject(id));
  }

  protected async remove(id: string): Promise<void> {
    await this.guard(() => this.api.deleteProject(id));
  }

  protected async share(resourceId: string): Promise<void> {
    const target = this.shareDraft()[resourceId]?.trim();

    if (!target) {
      return;
    }

    await this.guard(async () => {
      this.shareResult.set(await this.api.shareResource(resourceId, target));
    });
  }

  /**
   * Ejecuta una operación de negocio y captura el 403 con su explicación.
   *
   * El motivo del DENY se muestra tal cual lo redactó el módulo de control de acceso. En
   * producción eso no se haría —explicar por qué no tienes acceso filtra el organigrama— pero
   * aquí es justo lo que se quiere ver.
   */
  private async guard(operation: () => Promise<unknown>): Promise<void> {
    this.forbidden.set(null);

    try {
      await operation();
      await this.load();
    } catch (error) {
      const problem = error as { status?: number; error?: ForbiddenPayload };

      if (problem?.status === 403 && problem.error) {
        this.forbidden.set(problem.error);
      }
    }
  }

  private async load(): Promise<void> {
    const [projects, resources] = await Promise.all([
      this.api.getProjects(),
      this.api.getResources(),
    ]);

    this.projects.set(projects);
    this.resources.set(resources);
  }
}
