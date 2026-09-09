import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { AccessControlApi } from '../../core/api/access-control.api';
import { AuditEntry } from '../../core/models/access-control.models';
import { ObjectChipComponent, PanelComponent } from '../../shared/ui.components';

/**
 * Auditoría de decisiones.
 *
 * Cada fila guarda el camino de autorización serializado y el modelo con el que se evaluó, y
 * esas dos columnas son las que separan una auditoría útil de un log inútil. En ReBAC la
 * pregunta «¿por qué tenía acceso?» es especialmente difícil de responder a posteriori: el
 * acceso pudo venir de una relación creada por otra persona, sobre otro objeto, tres niveles
 * más arriba, hace meses — y si las tuplas han cambiado desde entonces, reconstruirlo es
 * imposible. Por eso el camino se guarda en el momento de decidir, no se recalcula después.
 */
@Component({
  selector: 'pg-audit',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, DatePipe, RouterLink, PanelComponent, ObjectChipComponent],
  template: `
    <div class="mx-auto max-w-6xl space-y-5">
      <header>
        <h1 class="text-xl font-bold text-slate-100">Auditoría de decisiones</h1>
        <p class="mt-1 max-w-3xl text-sm leading-relaxed text-slate-400">
          Todas las decisiones evaluadas, con su explicación, su coste y el modelo con el que se
          decidieron. El origen distingue las que protegieron una operación real de las que
          fueron experimentos tuyos.
        </p>
      </header>

      <pg-panel title="Filtros">
        <div class="flex flex-wrap items-center gap-3">
          <input
            class="tuple w-56 rounded-md border border-slate-700 bg-slate-950 px-3 py-2 text-sm text-slate-100"
            [(ngModel)]="filter"
            placeholder="Filtrar por sujeto u objeto…"
          />

          <label class="flex items-center gap-2 text-xs text-slate-400">
            <input type="checkbox" class="accent-violet-500" [(ngModel)]="onlyDenied" />
            Solo denegadas
          </label>

          <label class="flex items-center gap-2 text-xs text-slate-400">
            <input type="checkbox" class="accent-violet-500" [(ngModel)]="onlyBusiness" />
            <span title="Las decisiones con origen «business-api» protegieron una operación real; las de «explorer» son experimentos del laboratorio.">
              Solo operaciones reales
            </span>
          </label>

          <button
            type="button"
            class="rounded-md border border-slate-700 px-3 py-2 text-sm text-slate-300 hover:bg-slate-800"
            (click)="load()"
          >
            Actualizar
          </button>
        </div>
      </pg-panel>

      <pg-panel title="Decisiones ({{ filtered().length }})">
        @if (filtered().length === 0) {
          <p class="text-sm text-slate-400">
            Todavía no hay decisiones registradas con esos filtros. Ve al Explorer y evalúa
            alguna pregunta.
          </p>
        }

        <ul class="space-y-2">
          @for (entry of filtered(); track entry.id) {
            <li class="rounded-lg border border-slate-800 bg-slate-950/60 p-3">
              <div class="flex flex-wrap items-center gap-2">
                <span
                  class="rounded px-2 py-0.5 text-xs font-bold"
                  [class]="
                    entry.allowed
                      ? 'bg-emerald-500/15 text-emerald-300'
                      : 'bg-rose-500/15 text-rose-300'
                  "
                >
                  {{ entry.allowed ? 'ALLOW' : 'DENY' }}
                </span>

                <pg-object-chip [reference]="entry.subject" />
                <span class="tuple text-slate-500">--{{ entry.relation }}--&gt;</span>
                <pg-object-chip [reference]="entry.object" />

                <span class="ml-auto flex items-center gap-2 text-xs text-slate-500">
                  @if (entry.origin) {
                    <span
                      class="rounded px-1.5 py-0.5"
                      [class]="
                        entry.origin === 'business-api'
                          ? 'bg-sky-500/10 text-sky-300'
                          : 'bg-slate-800 text-slate-400'
                      "
                      >{{ entry.origin }}</span
                    >
                  }
                  <span>{{ entry.durationMs }} ms</span>
                  <span>{{ entry.storeQueries }} consultas</span>
                  <span>{{ entry.timestamp | date: 'dd/MM HH:mm:ss' }}</span>
                </span>
              </div>

              <p class="mt-2 text-xs leading-relaxed text-slate-400">{{ entry.reason }}</p>

              <div class="mt-2 flex flex-wrap items-center gap-3 text-xs">
                @if (entry.modelId) {
                  <span
                    class="tuple text-slate-600"
                    title="Modelo con el que se evaluó. Sin este dato la decisión no sería reproducible: el mismo estado de tuplas con otro modelo puede dar otra respuesta."
                  >
                    modelo {{ entry.modelId }}
                  </span>
                }
                <a
                  class="text-violet-400 hover:text-violet-300"
                  [routerLink]="['/explorer']"
                  [queryParams]="{
                    subject: entry.subject,
                    relation: entry.relation,
                    object: entry.object,
                  }"
                >
                  Reproducir en el Explorer →
                </a>
                @if (entry.pathJson) {
                  <button
                    type="button"
                    class="text-slate-500 hover:text-slate-300"
                    (click)="toggle(entry.id)"
                  >
                    {{ expanded() === entry.id ? 'Ocultar' : 'Ver' }} el camino guardado
                  </button>
                }
              </div>

              @if (expanded() === entry.id && entry.pathJson) {
                <pre
                  class="tuple mt-2 max-h-72 overflow-auto rounded bg-slate-950 p-3 text-[11px] text-slate-400"
                  >{{ pretty(entry.pathJson) }}</pre
                >
              }
            </li>
          }
        </ul>
      </pg-panel>
    </div>
  `,
})
export class AuditComponent implements OnInit {
  private readonly api = inject(AccessControlApi);

  protected filter = '';
  protected onlyDenied = false;
  protected onlyBusiness = false;

  protected readonly entries = signal<AuditEntry[]>([]);
  protected readonly expanded = signal<number | null>(null);

  protected readonly filtered = computed(() => {
    const needle = this.filter.trim().toLowerCase();

    return this.entries().filter((entry) => {
      if (this.onlyDenied && entry.allowed) {
        return false;
      }

      if (this.onlyBusiness && entry.origin !== 'business-api') {
        return false;
      }

      if (!needle) {
        return true;
      }

      return (
        entry.subject.toLowerCase().includes(needle) || entry.object.toLowerCase().includes(needle)
      );
    });
  });

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async load(): Promise<void> {
    this.entries.set(await this.api.getAudit(200));
  }

  protected toggle(id: number): void {
    this.expanded.set(this.expanded() === id ? null : id);
  }

  protected pretty(json: string): string {
    try {
      return JSON.stringify(JSON.parse(json), null, 2);
    } catch {
      return json;
    }
  }
}
