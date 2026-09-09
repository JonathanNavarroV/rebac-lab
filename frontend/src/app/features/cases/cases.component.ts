import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AccessControlApi } from '../../core/api/access-control.api';
import { GuidedCase } from '../../core/models/access-control.models';
import { ObjectChipComponent, PanelComponent } from '../../shared/ui.components';

/**
 * Los casos guiados: el punto de entrada del laboratorio.
 *
 * Cada tarjeta es una pregunta con su respuesta actual, qué demuestra y —cuando aplica— por
 * qué RBAC lo tendría difícil. Vienen del backend y son literalmente los mismos casos que
 * ejecuta la suite de conformidad, así que no pueden desincronizarse de lo que el sistema
 * hace de verdad.
 */
@Component({
  selector: 'pg-cases',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, PanelComponent, ObjectChipComponent],
  template: `
    <div class="mx-auto max-w-6xl space-y-5">
      <header>
        <h1 class="text-xl font-bold text-slate-100">Casos guiados</h1>
        <p class="mt-1 max-w-3xl text-sm leading-relaxed text-slate-400">
          Dieciséis preguntas elegidas para que cada una enseñe algo distinto. Todas se evalúan
          ahora mismo contra el estado real del sistema: si cambias relaciones, los resultados
          cambian aquí. Pulsa en cualquiera para verla en el Explorer con su árbol completo.
        </p>
      </header>

      @if (broken().length > 0) {
        <div
          class="rounded-lg border border-amber-500/30 bg-amber-500/10 p-4 text-sm text-amber-200"
        >
          <b>{{ broken().length }} caso(s) ya no dan el resultado esperado.</b>
          Has cambiado relaciones y el escenario original se ha alterado — que es exactamente
          para lo que está el laboratorio. Si quieres volver al punto de partida, usa
          «Restaurar escenario» en la pantalla de Relaciones.
        </div>
      }

      <pg-panel
        title="Los casos"
        hint="Verde = coincide con lo esperado. Ámbar = el resultado ha cambiado respecto al escenario original."
      >
        @if (loading()) {
          <p class="text-sm text-slate-400">Evaluando los casos…</p>
        }

        <ul class="grid gap-3 lg:grid-cols-2">
          @for (item of cases(); track item.code) {
            <li
              class="rounded-lg border p-4"
              [class]="
                item.stillMatchesExpectation
                  ? 'border-slate-800 bg-slate-950/60'
                  : 'border-amber-500/40 bg-amber-500/5'
              "
            >
              <div class="mb-2 flex items-start justify-between gap-3">
                <h3 class="text-sm font-semibold text-slate-100">
                  <span class="mr-1.5 text-slate-500">{{ item.code }}.</span>{{ item.title }}
                </h3>

                <span
                  class="shrink-0 rounded px-2 py-0.5 text-xs font-bold"
                  [class]="
                    item.actualAllowed
                      ? 'bg-emerald-500/15 text-emerald-300'
                      : 'bg-rose-500/15 text-rose-300'
                  "
                >
                  {{ item.actualAllowed === null ? '—' : item.actualAllowed ? 'ALLOW' : 'DENY' }}
                </span>
              </div>

              <div class="mb-3 flex flex-wrap items-center gap-1.5 text-xs">
                <pg-object-chip [reference]="item.subject" />
                <span class="tuple text-slate-500">--{{ item.relation }}--&gt;</span>
                <pg-object-chip [reference]="item.object" />
              </div>

              <p class="text-xs leading-relaxed text-slate-400">{{ item.whatItTeaches }}</p>

              @if (item.whyRbacStruggles; as rbac) {
                <p
                  class="mt-2 border-l-2 border-amber-500/40 pl-3 text-xs leading-relaxed text-amber-200/80"
                >
                  <b>Con RBAC:</b> {{ rbac }}
                </p>
              }

              @if (!item.stillMatchesExpectation) {
                <p class="mt-2 text-xs text-amber-300">
                  En el escenario original esto daba
                  <b>{{ item.expectedAllowed ? 'ALLOW' : 'DENY' }}</b
                  >.
                </p>
              }

              <a
                class="mt-3 inline-block text-xs font-medium text-violet-400 hover:text-violet-300"
                [routerLink]="['/explorer']"
                [queryParams]="{
                  subject: item.subject,
                  relation: item.relation,
                  object: item.object,
                }"
              >
                Ver el porqué en el Explorer →
              </a>
            </li>
          }
        </ul>
      </pg-panel>
    </div>
  `,
})
export class CasesComponent implements OnInit {
  private readonly api = inject(AccessControlApi);

  protected readonly cases = signal<GuidedCase[]>([]);
  protected readonly loading = signal(true);
  protected readonly broken = signal<GuidedCase[]>([]);

  async ngOnInit(): Promise<void> {
    try {
      const result = await this.api.getGuidedCases();

      this.cases.set(result);
      this.broken.set(result.filter((item) => !item.stillMatchesExpectation));
    } finally {
      this.loading.set(false);
    }
  }
}
