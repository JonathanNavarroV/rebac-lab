import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { CheckMetrics } from '../core/models/access-control.models';
import { isUserset, kindStyle } from './object-kind';

/**
 * Insignia de un objeto o sujeto: `project:alpha`, `team:backend#member`, `user:*`.
 *
 * Distingue visualmente los tres casos, y esa distinción es la que más cuesta interiorizar
 * al empezar con ReBAC:
 *
 *   user:juan            una persona
 *   team:backend#member  un CONJUNTO, que se evalúa al preguntar
 *   user:*               cualquiera
 */
@Component({
  selector: 'pg-object-chip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span
      class="tuple inline-flex items-center gap-1.5 rounded-md px-2 py-0.5 ring-1 ring-inset"
      [class]="style().chip"
      [title]="title()"
    >
      <span aria-hidden="true">{{ style().icon }}</span>
      <span>{{ reference() }}</span>
      @if (userset()) {
        <span
          class="rounded bg-black/25 px-1 text-[10px] font-semibold tracking-wide uppercase"
          title="Es un conjunto definido por una relación, no una persona concreta"
        >
          conjunto
        </span>
      }
      @if (wildcard()) {
        <span
          class="rounded bg-black/25 px-1 text-[10px] font-semibold tracking-wide uppercase"
          title="Comodín: cualquier sujeto de este tipo"
        >
          todos
        </span>
      }
    </span>
  `,
})
export class ObjectChipComponent {
  readonly reference = input.required<string>();

  protected readonly style = computed(() => kindStyle(this.reference()));
  protected readonly userset = computed(() => isUserset(this.reference()));
  protected readonly wildcard = computed(() => this.reference().endsWith(':*'));

  protected readonly title = computed(() => {
    if (this.wildcard()) {
      return `Comodín: cualquier «${this.style().label.toLowerCase()}» cumple esta relación.`;
    }

    if (this.userset()) {
      const [base, relation] = this.reference().split('#');
      return `Conjunto: quien tenga la relación «${relation}» sobre «${base}». No nombra a nadie en concreto.`;
    }

    return `${this.style().label}: ${this.reference()}`;
  });
}

/** ALLOW / DENY en grande. */
@Component({
  selector: 'pg-decision-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span
      class="inline-flex items-center gap-2 rounded-lg px-3 py-1.5 text-sm font-bold tracking-wider ring-1 ring-inset"
      [class]="
        allowed()
          ? 'bg-emerald-500/15 text-emerald-300 ring-emerald-500/40'
          : 'bg-rose-500/15 text-rose-300 ring-rose-500/40'
      "
    >
      {{ allowed() ? '✓ ALLOW' : '✕ DENY' }}
    </span>
  `,
})
export class DecisionBadgeComponent {
  readonly allowed = input.required<boolean>();
}

/**
 * Métricas de una evaluación.
 *
 * Se muestran siempre, en todas las pantallas, a propósito. En autorización el coste no es
 * un detalle de implementación: el check está en el camino crítico de cada petición, y tener
 * el número delante mientras experimentas es lo que hace que la intuición sobre rendimiento
 * se construya sola.
 */
@Component({
  selector: 'pg-metrics',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-slate-400">
      <span title="Consultas lanzadas contra el almacén de tuplas. Aquí está el coste real.">
        <b class="text-slate-200">{{ metrics().storeQueries }}</b> consultas
      </span>
      <span title="Tuplas leídas en total.">
        <b class="text-slate-200">{{ metrics().tuplesRead }}</b> tuplas
      </span>
      <span title="Nodos del árbol de evaluación recorridos.">
        <b class="text-slate-200">{{ metrics().nodesEvaluated }}</b> nodos
      </span>
      <span title="Profundidad máxima alcanzada. Delata jerarquías profundas.">
        prof. <b class="text-slate-200">{{ metrics().maxDepthReached }}</b>
      </span>
      @if (metrics().memoizationHits > 0) {
        <span title="Subárboles servidos desde la memoización interna del propio check.">
          <b class="text-slate-200">{{ metrics().memoizationHits }}</b> memo
        </span>
      }
      @if (metrics().cyclesDetected > 0) {
        <span
          class="text-amber-400"
          title="Ramas abandonadas por detectar un ciclo en las relaciones."
        >
          <b>{{ metrics().cyclesDetected }}</b> ciclos
        </span>
      }
      @if (metrics().confirmationChecks > 0) {
        <span
          class="text-amber-400"
          title="Checks extra que hubo que hacer porque el modelo tiene reglas no monótonas (but not / and) que no se pueden invertir."
        >
          <b>{{ metrics().confirmationChecks }}</b> confirmaciones
        </span>
      }
      <span class="font-semibold text-slate-200">{{ metrics().durationMs }} ms</span>
      @if (metrics().evaluationMode; as mode) {
        <span
          class="rounded px-1.5 py-0.5 ring-1 ring-inset"
          [class]="
            mode === 'Remote'
              ? 'bg-amber-500/10 text-amber-300 ring-amber-500/30'
              : 'bg-slate-700/40 text-slate-300 ring-slate-600'
          "
          [title]="
            mode === 'Remote'
              ? 'El control de acceso es un servicio aparte: cada check es un salto de red.'
              : 'El motor corre dentro del proceso: latencia prácticamente cero.'
          "
        >
          {{ mode }}
        </span>
      }
    </div>
  `,
})
export class MetricsComponent {
  readonly metrics = input.required<CheckMetrics>();
}

/** Panel con título y explicación, el contenedor estándar del laboratorio. */
@Component({
  selector: 'pg-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="rounded-xl border border-slate-800 bg-slate-900/50">
      <header class="border-b border-slate-800 px-4 py-3">
        <h2 class="text-sm font-semibold tracking-wide text-slate-100 uppercase">
          {{ title() }}
        </h2>
        @if (hint(); as text) {
          <p class="mt-1 text-xs leading-relaxed text-slate-400">{{ text }}</p>
        }
      </header>
      <div class="p-4">
        <ng-content />
      </div>
    </section>
  `,
})
export class PanelComponent {
  readonly title = input.required<string>();
  readonly hint = input<string>();
}
