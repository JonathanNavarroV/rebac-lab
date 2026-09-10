import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { AccessControlApi } from '../../core/api/access-control.api';
import { TourAnswer, TourLesson, TourQuestion } from '../../core/models/access-control.models';
import {
  DecisionBadgeComponent,
  MetricsComponent,
  ObjectChipComponent,
} from '../../shared/ui.components';
import { TraceTreeComponent } from '../../shared/trace-tree.component';

const STORAGE_KEY = 'playground.tour.progress';

/** Lo que se guarda entre sesiones: qué has respondido y si acertaste. */
interface Progress {
  [questionCode: string]: { chosen: string; correct: boolean };
}

/**
 * El cuestionario guiado.
 *
 * La mecánica no es la de un test de trivia: **predices, y al responder el sistema ejecuta la
 * pregunta contra el motor real** y te enseña lo que contesta de verdad —con su traza y sus
 * métricas— antes de darte la explicación.
 *
 * Eso tiene una consecuencia que da sentido a la pantalla: las respuestas correctas de las
 * preguntas de predicción están respaldadas por comprobaciones que un test de conformidad
 * ejecuta en cada build. Si alguien cambia el modelo y una lección deja de ser cierta, falla el
 * test en lugar de enseñarte algo falso. Y si has cambiado tú las relaciones mientras
 * experimentabas, la pantalla te avisa de que los números que estás viendo ya no son los del
 * escenario original.
 */
@Component({
  selector: 'pg-tour',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ObjectChipComponent, DecisionBadgeComponent, MetricsComponent, TraceTreeComponent],
  template: `
    <div class="mx-auto max-w-5xl space-y-5">
      <header>
        <h1 class="text-xl font-bold text-slate-100">Tour guiado</h1>
        <p class="mt-1 max-w-3xl text-sm leading-relaxed text-slate-400">
          Once lecciones en forma de preguntas de predicción. La mecánica es siempre la misma:
          lee el contexto, <b class="text-slate-300">apuesta de verdad</b> por una respuesta, y
          solo entonces comprueba. Acertar enseña menos que fallar.
        </p>
        <p class="mt-2 max-w-3xl text-sm leading-relaxed text-slate-400">
          Al responder, el sistema no te enseña un texto: <b class="text-slate-300">ejecuta la
          pregunta contra el motor</b> y te devuelve lo que contesta ahora mismo, con el árbol de
          evaluación y el coste.
        </p>
      </header>

      @if (lessons().length > 0) {
        <div
          class="flex flex-wrap items-center gap-x-6 gap-y-2 rounded-lg border border-slate-800 bg-slate-900/50 px-4 py-3 text-sm"
        >
          <span class="text-slate-400">
            Respondidas <b class="text-slate-100">{{ answeredCount() }}</b> de
            <b class="text-slate-100">{{ totalQuestions() }}</b>
          </span>
          <span class="text-emerald-300">{{ correctCount() }} acertadas</span>
          @if (answeredCount() - correctCount(); as failed) {
            <span class="text-amber-300">{{ failed }} falladas</span>
          }
          <div class="h-1.5 min-w-40 flex-1 overflow-hidden rounded-full bg-slate-800">
            <div
              class="h-full rounded-full bg-violet-500 transition-all"
              [style.width.%]="(answeredCount() / totalQuestions()) * 100"
            ></div>
          </div>
          @if (answeredCount() > 0) {
            <button
              type="button"
              class="text-xs text-slate-500 hover:text-slate-300"
              (click)="resetProgress()"
            >
              Empezar de nuevo
            </button>
          }
        </div>
      }

      @for (lesson of lessons(); track lesson.code) {
        <section class="rounded-xl border border-slate-800 bg-slate-900/50">
          <!-- ── Contexto de la lección ─────────────────────────────────── -->
          <header class="border-b border-slate-800 px-4 py-3">
            <h2 class="text-sm font-semibold tracking-wide text-slate-100 uppercase">
              <span class="mr-2 text-violet-400">Lección {{ lesson.code }}</span>{{ lesson.title }}
            </h2>
            <p class="mt-2 text-sm leading-relaxed text-slate-400">{{ lesson.intro }}</p>

            @if (lesson.facts.length > 0) {
              <ul class="mt-3 space-y-1">
                @for (fact of lesson.facts; track $index) {
                  <li class="tuple flex gap-2 text-xs text-slate-400">
                    <span class="text-slate-600">·</span>
                    <span>{{ fact }}</span>
                  </li>
                }
              </ul>
            }

            @if (lesson.modelSnippet; as snippet) {
              <pre
                class="tuple mt-3 overflow-x-auto rounded-lg bg-slate-950 p-3 text-xs text-violet-200"
                >{{ snippet }}</pre
              >
            }
          </header>

          <!-- ── Preguntas ──────────────────────────────────────────────── -->
          <div class="divide-y divide-slate-800">
            @for (question of lesson.questions; track question.code) {
              <article class="p-4">
                <div class="mb-3 flex flex-wrap items-start gap-2">
                  <span
                    class="rounded px-1.5 py-0.5 text-[10px] font-bold"
                    [class]="
                      question.kind === 'prediction'
                        ? 'bg-violet-500/15 text-violet-300'
                        : 'bg-slate-700/50 text-slate-300'
                    "
                    [title]="
                      question.kind === 'prediction'
                        ? 'Se puede comprobar ejecutando el motor. Al responder verás la prueba.'
                        : 'Pregunta de criterio: por qué el modelo está escrito así. No se ejecuta.'
                    "
                  >
                    {{ question.code }}
                  </span>

                  <p class="min-w-0 flex-1 text-sm leading-relaxed text-slate-200">
                    {{ question.statement }}
                  </p>

                  @if (answerOf(question.code); as answered) {
                    <span
                      class="shrink-0 rounded px-2 py-0.5 text-xs font-bold"
                      [class]="
                        answered.correct
                          ? 'bg-emerald-500/15 text-emerald-300'
                          : 'bg-amber-500/15 text-amber-300'
                      "
                    >
                      {{ answered.correct ? '✓ acertada' : '✕ fallada' }}
                    </span>
                  }
                </div>

                <!-- Opciones -->
                <ul class="space-y-1.5">
                  @for (option of question.options; track option.key) {
                    <li>
                      <button
                        type="button"
                        class="flex w-full items-start gap-3 rounded-lg border px-3 py-2 text-left text-sm transition-colors"
                        [class]="optionClasses(question.code, option.key)"
                        [disabled]="busy() === question.code"
                        (click)="answer(question, option.key)"
                      >
                        <span class="tuple shrink-0 font-bold opacity-60">{{ option.key }}</span>
                        <span class="flex-1">{{ option.text }}</span>
                        @if (markFor(question.code, option.key); as mark) {
                          <span class="shrink-0 font-bold">{{ mark }}</span>
                        }
                      </button>
                    </li>
                  }
                </ul>

                @if (!answerOf(question.code) && question.hint) {
                  <p class="mt-2 text-xs text-slate-500">
                    <b>Pista:</b> {{ question.hint }}
                  </p>
                }

                @if (busy() === question.code) {
                  <p class="mt-3 text-xs text-slate-400">Ejecutando contra el motor…</p>
                }

                <!-- ── La respuesta, tras contestar ───────────────────────── -->
                @if (resultOf(question.code); as result) {
                  <div class="mt-4 space-y-3">
                    @if (result.scenarioWarning; as warning) {
                      <p
                        class="rounded-lg border border-amber-500/30 bg-amber-500/10 p-3 text-xs leading-relaxed text-amber-200"
                      >
                        {{ warning }}
                      </p>
                    }

                    <div
                      class="rounded-lg border p-3"
                      [class]="
                        result.correct
                          ? 'border-emerald-500/30 bg-emerald-500/5'
                          : 'border-amber-500/30 bg-amber-500/5'
                      "
                    >
                      <p class="mb-2 text-sm font-semibold"
                        [class]="result.correct ? 'text-emerald-300' : 'text-amber-300'">
                        {{
                          result.correct
                            ? '✓ Correcto'
                            : '✕ La respuesta era (' + result.correctOptionKey + ')'
                        }}
                      </p>
                      <p class="text-sm leading-relaxed whitespace-pre-line text-slate-300">
                        {{ result.explanation }}
                      </p>
                    </div>

                    <!-- ── La evidencia ejecutada en vivo ────────────────── -->
                    @if (result.evidence.length > 0) {
                      <div class="rounded-lg border border-slate-800 bg-slate-950/60 p-3">
                        <p class="mb-3 text-xs text-slate-500">
                          Y esto no es un texto escrito a mano: es lo que el motor acaba de
                          responder sobre tus tuplas de ahora mismo.
                        </p>

                        <div class="space-y-3">
                          @for (item of result.evidence; track $index) {
                            <div class="rounded-md border border-slate-800 bg-slate-900/40 p-3">
                              <div class="mb-2 flex flex-wrap items-center gap-2">
                                @if (item.check; as check) {
                                  <pg-decision-badge [allowed]="check.allowed" />
                                }
                                <span class="text-xs text-slate-300">{{ item.label }}</span>
                                @if (item.matchesExpectation === false) {
                                  <span
                                    class="rounded bg-amber-500/15 px-1.5 py-0.5 text-[10px] text-amber-300"
                                    title="Esta comprobación ya no da el resultado que la lección da por supuesto: has cambiado el escenario."
                                  >
                                    cambiado
                                  </span>
                                }
                              </div>

                              <div class="mb-2 flex flex-wrap items-center gap-1.5">
                                <pg-object-chip [reference]="item.subject" />
                                <span class="tuple text-xs text-slate-500"
                                  >--{{ item.relation }}--&gt;</span
                                >
                                <pg-object-chip [reference]="item.object" />
                              </div>

                              <!-- Evidencia de tipo check -->
                              @if (item.check; as check) {
                                <p class="mb-2 text-xs leading-relaxed text-slate-400">
                                  {{ check.reason }}
                                </p>

                                @if (check.paths.length > 0) {
                                  <ul class="mb-2 space-y-1">
                                    @for (path of check.paths; track $index) {
                                      <li class="tuple text-xs text-emerald-300">
                                        {{ path.chain }}
                                      </li>
                                    }
                                  </ul>
                                }

                                @if (check.trace; as trace) {
                                  <details class="mt-2">
                                    <summary
                                      class="cursor-pointer text-xs text-violet-400 hover:text-violet-300"
                                    >
                                      Ver el árbol de evaluación
                                    </summary>
                                    <div class="mt-2 border-l border-slate-800 pl-2">
                                      <pg-trace-tree [node]="trace" />
                                    </div>
                                  </details>
                                }

                                <div class="mt-2 border-t border-slate-800 pt-2">
                                  <pg-metrics [metrics]="check.metrics" />
                                </div>
                              }

                              <!-- Evidencia de tipo comparación de listado -->
                              @if (item.comparison; as comparison) {
                                <p class="mb-2 text-xs leading-relaxed text-slate-300">
                                  {{ comparison.verdict }}
                                </p>

                                <p class="tuple mb-2 text-xs text-slate-400">
                                  Resultado: {{ comparison.objects.join(', ') || '(ninguno)' }}
                                </p>

                                <div class="grid gap-2 sm:grid-cols-2">
                                  <div class="rounded border border-slate-800 p-2">
                                    <p class="mb-1 text-[10px] tracking-wide text-slate-500 uppercase">
                                      Ingenua
                                    </p>
                                    <pg-metrics [metrics]="comparison.naiveMetrics" />
                                  </div>
                                  <div class="rounded border border-slate-800 p-2">
                                    <p class="mb-1 text-[10px] tracking-wide text-slate-500 uppercase">
                                      Expansión inversa
                                    </p>
                                    <pg-metrics [metrics]="comparison.reverseMetrics" />
                                  </div>
                                </div>
                              }
                            </div>
                          }
                        </div>
                      </div>
                    }
                  </div>
                }
              </article>
            }
          </div>
        </section>
      }

      @if (error(); as message) {
        <div class="rounded-lg border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-200">
          {{ message }}
        </div>
      }
    </div>
  `,
})
export class TourComponent implements OnInit {
  private readonly api = inject(AccessControlApi);

  protected readonly lessons = signal<TourLesson[]>([]);
  protected readonly busy = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  /** Progreso persistido: sobrevive a recargar la página. */
  private readonly progress = signal<Progress>(this.readProgress());

  /**
   * Resultados de esta sesión.
   *
   * No se persisten: guardar la explicación en localStorage la dejaría congelada, y la gracia
   * es que la evidencia se ejecuta contra el estado actual del motor. Si recargas, vuelves a
   * ver qué acertaste, pero para ver la prueba hay que volver a preguntar.
   */
  private readonly results = signal<Record<string, TourAnswer>>({});

  protected readonly totalQuestions = computed(() =>
    this.lessons().reduce((total, lesson) => total + lesson.questions.length, 0),
  );

  protected readonly answeredCount = computed(() => Object.keys(this.progress()).length);

  protected readonly correctCount = computed(
    () => Object.values(this.progress()).filter((entry) => entry.correct).length,
  );

  async ngOnInit(): Promise<void> {
    try {
      this.lessons.set(await this.api.getTour());
    } catch {
      this.error.set(
        'No se ha podido cargar el tour. ¿Está levantado el módulo de control de acceso en :15101?',
      );
    }
  }

  protected answerOf(code: string): { chosen: string; correct: boolean } | undefined {
    return this.progress()[code];
  }

  protected resultOf(code: string): TourAnswer | undefined {
    return this.results()[code];
  }

  protected async answer(question: TourQuestion, optionKey: string): Promise<void> {
    // Una vez respondida no se puede cambiar: apostar y luego rectificar al ver el resultado
    // vaciaría el ejercicio de sentido.
    if (this.answerOf(question.code) || this.busy()) {
      return;
    }

    this.busy.set(question.code);

    try {
      const result = await this.api.answerTourQuestion(question.code, optionKey);

      this.results.update((current) => ({ ...current, [question.code]: result }));

      this.progress.update((current) => {
        const updated = { ...current, [question.code]: { chosen: optionKey, correct: result.correct } };
        localStorage.setItem(STORAGE_KEY, JSON.stringify(updated));
        return updated;
      });
    } catch {
      this.error.set('No se ha podido corregir la respuesta.');
    } finally {
      this.busy.set(null);
    }
  }

  protected resetProgress(): void {
    localStorage.removeItem(STORAGE_KEY);
    this.progress.set({});
    this.results.set({});
  }

  protected optionClasses(questionCode: string, optionKey: string): string {
    const answered = this.answerOf(questionCode);
    const result = this.resultOf(questionCode);

    if (!answered) {
      return 'border-slate-700 bg-slate-950 text-slate-300 hover:border-violet-500 hover:bg-slate-800/60';
    }

    // La correcta siempre se marca en verde, hayas acertado o no: lo que interesa al terminar
    // es saber cuál era, no solo si fallaste.
    if (result && optionKey === result.correctOptionKey) {
      return 'border-emerald-500/50 bg-emerald-500/10 text-emerald-200';
    }

    if (optionKey === answered.chosen) {
      return 'border-amber-500/50 bg-amber-500/10 text-amber-200';
    }

    return 'border-slate-800 bg-slate-950/40 text-slate-500';
  }

  protected markFor(questionCode: string, optionKey: string): string | null {
    const answered = this.answerOf(questionCode);
    const result = this.resultOf(questionCode);

    if (!answered) {
      return null;
    }

    if (result && optionKey === result.correctOptionKey) {
      return '✓';
    }

    return optionKey === answered.chosen ? '✕' : null;
  }

  private readProgress(): Progress {
    try {
      return JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}') as Progress;
    } catch {
      return {};
    }
  }
}
