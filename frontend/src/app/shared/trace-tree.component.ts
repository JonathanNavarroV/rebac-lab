import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { CheckTraceNode } from '../core/models/access-control.models';

/**
 * Etiquetas legibles para cada variante de regla del modelo.
 *
 * La traza muestra los nombres del paper (`_this`, `tuple_to_userset`...) junto a una
 * explicación en castellano, y las dos cosas a la vez son intencionadas: los nombres
 * técnicos son los que aparecerán en la documentación de OpenFGA o en el paper de Zanzibar
 * cuando vayas a leerlos, así que conviene irse familiarizando con ellos mientras se
 * entiende qué hacen.
 */
const KIND_LABELS: Record<string, { badge: string; help: string; color: string }> = {
  relation: {
    badge: 'relación',
    help: 'Se evalúa una relación sobre un objeto concreto.',
    color: 'bg-slate-700/50 text-slate-300',
  },
  _this: {
    badge: '_this',
    help: 'Caso base: se leen las tuplas escritas directamente. Es la única regla que consulta la base de datos.',
    color: 'bg-sky-500/15 text-sky-300',
  },
  tuple: {
    badge: 'tupla',
    help: 'Una tupla concreta que se está comprobando.',
    color: 'bg-slate-600/40 text-slate-300',
  },
  computed_userset: {
    badge: 'computed_userset',
    help: 'Salto a otra relación del MISMO objeto. Es lo que permite que el negocio pregunte por can_edit y no por editor.',
    color: 'bg-violet-500/15 text-violet-300',
  },
  tuple_to_userset: {
    badge: 'tuple_to_userset',
    help: 'Herencia: se sube por una relación (normalmente parent) y se pregunta arriba. Es lo que hace funcionar las jerarquías.',
    color: 'bg-cyan-500/15 text-cyan-300',
  },
  parent: {
    badge: 'padre',
    help: 'Objeto de arriba al que se ha subido para heredar.',
    color: 'bg-cyan-500/10 text-cyan-200',
  },
  union: {
    badge: 'union',
    help: 'Basta con que una rama conceda. Es de donde salen los múltiples caminos de acceso.',
    color: 'bg-emerald-500/15 text-emerald-300',
  },
  intersection: {
    badge: 'intersection',
    help: 'Hay que cumplir TODAS las condiciones. Modela cosas como «editar y además ser de la casa».',
    color: 'bg-amber-500/15 text-amber-300',
  },
  exclusion: {
    badge: 'exclusion',
    help: 'Concede salvo que la rama de exclusión también conceda. La exclusión gana a todos los caminos.',
    color: 'bg-rose-500/15 text-rose-300',
  },
};

/**
 * Árbol de evaluación de un check, indentado y plegable.
 *
 * Muestra **también las ramas que denegaron**, y esa decisión es la que convierte la pantalla
 * en material didáctico: ver por dónde NO llegó el acceso enseña tanto como ver por dónde sí.
 * Un check que devuelve solo `true` no permite aprender nada.
 */
@Component({
  selector: 'pg-trace-tree',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TraceTreeComponent],
  template: `
    <div class="tuple">
      <button
        type="button"
        class="flex w-full items-start gap-2 rounded px-1.5 py-1 text-left hover:bg-slate-800/60"
        (click)="toggle()"
      >
        <span class="w-4 shrink-0 pt-0.5 text-slate-500">
          @if (node().children.length > 0) {
            {{ expanded() ? '▾' : '▸' }}
          }
        </span>

        <span class="shrink-0 pt-0.5" [class]="node().allowed ? 'text-emerald-400' : 'text-rose-400'">
          {{ node().allowed ? '✓' : '✕' }}
        </span>

        <span class="min-w-0 flex-1">
          <span class="flex flex-wrap items-center gap-2">
            <span
              class="rounded px-1.5 py-0.5 text-[10px] font-semibold"
              [class]="meta().color"
              [title]="meta().help"
            >
              {{ meta().badge }}
            </span>

            <span [class]="node().allowed ? 'text-slate-100' : 'text-slate-400'">
              {{ node().label }}
            </span>

            @if (node().fromCache) {
              <span
                class="rounded bg-slate-700/50 px-1.5 py-0.5 text-[10px] text-slate-400"
                title="Este subárbol ya se había resuelto antes durante este mismo check."
              >
                memo
              </span>
            }
          </span>

          @if (node().detail; as detail) {
            <span class="mt-0.5 block text-xs text-slate-500">← {{ detail }}</span>
          }
        </span>
      </button>

      @if (expanded() && node().children.length > 0) {
        <div class="ml-3 border-l border-slate-800 pl-3">
          @for (child of node().children; track $index) {
            <pg-trace-tree [node]="child" [autoExpand]="autoExpand()" />
          }
        </div>
      }
    </div>
  `,
})
export class TraceTreeComponent {
  readonly node = input.required<CheckTraceNode>();

  /**
   * Si es `true`, las ramas que concedieron acceso salen abiertas y las que denegaron,
   * plegadas. Es lo que hace que al abrir el Explorer se vea directamente el camino que
   * importó, sin perderse entre las ramas muertas.
   */
  readonly autoExpand = input(true);

  private readonly manual = signal<boolean | null>(null);

  protected readonly meta = computed(
    () => KIND_LABELS[this.node().kind] ?? KIND_LABELS['relation'],
  );

  protected readonly expanded = computed(() => {
    const override = this.manual();

    if (override !== null) {
      return override;
    }

    return this.autoExpand() ? this.node().allowed : false;
  });

  protected toggle(): void {
    this.manual.set(!this.expanded());
  }
}
