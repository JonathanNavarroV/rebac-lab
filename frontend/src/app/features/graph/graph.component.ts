import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  OnInit,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import cytoscape, { Core, ElementDefinition } from 'cytoscape';
import { AccessControlApi } from '../../core/api/access-control.api';
import { GraphResponse } from '../../core/models/access-control.models';
import { CatalogStore } from '../../core/state/catalog.store';
import { IdentityStore } from '../../core/state/identity.store';
import { OBJECT_KINDS } from '../../shared/object-kind';
import { PanelComponent } from '../../shared/ui.components';

/**
 * El grafo de autorización.
 *
 * Conviene tener clara una distinción antes de mirarlo: **esto no es el diagrama de entidades
 * de la aplicación**. Es el grafo por el que viaja el acceso, y contiene nodos que no existen
 * como entidad en ninguna base de datos de negocio — los usersets (`team:backend#member`), que
 * son conjuntos y se dibujan con borde discontinuo.
 *
 * Se puede resaltar un camino concreto: al evaluar una pregunta, las aristas que conceden el
 * acceso se pintan y el resto del grafo se atenúa.
 */
@Component({
  selector: 'pg-graph',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PanelComponent],
  template: `
    <div class="mx-auto max-w-7xl space-y-5">
      <header>
        <h1 class="text-xl font-bold text-slate-100">Grafo de relaciones</h1>
        <p class="mt-1 max-w-3xl text-sm leading-relaxed text-slate-400">
          Cada arista es una tupla. Las de trazo discontinuo salen de un <b>conjunto</b>, no de
          una persona: cuando una arista sale de <code class="tuple">team:backend#member</code>,
          lo que dice es «quien pertenezca a este equipo», no «estas personas concretas».
        </p>
      </header>

      <pg-panel
        title="Vista"
        hint="Con muchas tuplas el grafo completo se vuelve ilegible — que es exactamente lo que pasa en un sistema real con millones. Centrar la vista en un nodo y limitar la distancia es la única forma práctica de mirarlo."
      >
        <div class="flex flex-wrap items-end gap-3">
          <label class="block">
            <span class="mb-1 block text-xs text-slate-400">Centrar en</span>
            <input
              list="graph-focus"
              class="tuple w-64 rounded-md border border-slate-700 bg-slate-950 px-3 py-2 text-sm text-slate-100"
              [(ngModel)]="focus"
              placeholder="(todo el grafo)"
            />
            <datalist id="graph-focus">
              @for (entity of catalog.allObjects(); track entity.objectRef) {
                <option [value]="entity.objectRef">{{ entity.name }}</option>
              }
              @for (user of identity.users(); track user.id) {
                <option [value]="user.subject">{{ user.name }}</option>
              }
            </datalist>
          </label>

          <label class="block">
            <span class="mb-1 block text-xs text-slate-400">Distancia</span>
            <input
              type="number"
              min="1"
              max="8"
              class="w-20 rounded-md border border-slate-700 bg-slate-950 px-3 py-2 text-sm text-slate-100"
              [(ngModel)]="depth"
            />
          </label>

          <label class="block">
            <span class="mb-1 block text-xs text-slate-400">Disposición</span>
            <select
              class="rounded-md border border-slate-700 bg-slate-950 px-3 py-2 text-sm text-slate-100"
              [(ngModel)]="layoutName"
            >
              <option value="breadthfirst" title="Por capas, siguiendo la dirección de las relaciones. Es la más legible para leer un grafo de autorización de arriba abajo.">
                Por capas
              </option>
              <option value="cose" title="Distribución por fuerzas. Agrupa lo que está relacionado, pero se apelotona en las zonas densas.">
                Por fuerzas
              </option>
              <option value="concentric" title="Anillos por número de conexiones: lo más conectado al centro.">
                Concéntrica
              </option>
            </select>
          </label>

          <button
            type="button"
            class="rounded-md bg-violet-600 px-4 py-2 text-sm font-semibold text-white hover:bg-violet-500"
            (click)="load()"
          >
            Aplicar
          </button>

          <div class="ml-auto flex flex-wrap items-end gap-2">
            <label class="block">
              <span class="mb-1 block text-xs text-slate-400">Resaltar el camino de</span>
              <input
                class="tuple w-44 rounded-md border border-slate-700 bg-slate-950 px-3 py-2 text-sm text-slate-100"
                [(ngModel)]="pathSubject"
                placeholder="user:juan"
              />
            </label>
            <label class="block">
              <input
                class="tuple w-28 rounded-md border border-slate-700 bg-slate-950 px-3 py-2 text-sm text-slate-100"
                [(ngModel)]="pathRelation"
                placeholder="can_edit"
              />
            </label>
            <label class="block">
              <input
                class="tuple w-44 rounded-md border border-slate-700 bg-slate-950 px-3 py-2 text-sm text-slate-100"
                [(ngModel)]="pathObject"
                placeholder="resource:a"
              />
            </label>
            <button
              type="button"
              class="rounded-md border border-emerald-600/40 bg-emerald-500/10 px-3 py-2 text-sm text-emerald-300 hover:bg-emerald-500/20"
              (click)="highlightPath()"
            >
              Resaltar
            </button>
            @if (highlighted()) {
              <button
                type="button"
                class="rounded-md border border-slate-700 px-3 py-2 text-sm text-slate-400 hover:bg-slate-800"
                (click)="clearHighlight()"
              >
                Quitar
              </button>
            }
          </div>
        </div>

        @if (pathMessage(); as message) {
          <p class="mt-3 rounded-md bg-slate-950 p-3 text-sm text-slate-300">{{ message }}</p>
        }

        <div class="mt-4 flex flex-wrap gap-3 text-xs">
          @for (kind of legend; track kind.key) {
            <span class="flex items-center gap-1.5 text-slate-400">
              <span
                class="inline-block size-3 rounded-full"
                [style.background-color]="kind.hex"
              ></span>
              {{ kind.label }}
            </span>
          }
          <span class="flex items-center gap-1.5 text-slate-400">
            <span class="inline-block h-0 w-5 border-t-2 border-dashed border-slate-400"></span>
            arista desde un conjunto
          </span>
        </div>
      </pg-panel>

      <div
        #host
        class="h-[600px] w-full rounded-xl border border-slate-800 bg-slate-950"
        aria-label="Grafo de relaciones"
      ></div>

      @if (selection(); as selected) {
        <pg-panel title="Arista seleccionada">
          <code class="tuple text-slate-200">{{ selected }}</code>
        </pg-panel>
      }
    </div>
  `,
})
export class GraphComponent implements OnInit, OnDestroy {
  private readonly api = inject(AccessControlApi);
  protected readonly catalog = inject(CatalogStore);
  protected readonly identity = inject(IdentityStore);

  private readonly host = viewChild.required<ElementRef<HTMLDivElement>>('host');
  private cy: Core | null = null;

  protected focus = '';
  protected depth = 3;
  protected layoutName: 'breadthfirst' | 'cose' | 'concentric' = 'breadthfirst';
  protected pathSubject = 'user:juan';
  protected pathRelation = 'can_edit';
  protected pathObject = 'resource:a';

  protected readonly selection = signal<string | null>(null);
  protected readonly highlighted = signal(false);
  protected readonly pathMessage = signal<string | null>(null);

  protected readonly legend = Object.entries(OBJECT_KINDS).map(([key, value]) => ({
    key,
    label: value.label,
    hex: value.hex,
  }));

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  ngOnDestroy(): void {
    this.cy?.destroy();
  }

  protected async load(): Promise<void> {
    const graph = await this.api.getGraph(this.focus.trim() || undefined, this.depth);
    this.render(graph);
  }

  private render(graph: GraphResponse): void {
    this.cy?.destroy();
    this.clearHighlightState();

    const elements: ElementDefinition[] = [
      ...graph.nodes.map((node) => ({
        data: {
          id: node.id,
          label: node.displayName ?? node.label,
          reference: node.id,
          kind: node.kind.replace('-userset', ''),
          userset: node.kind.endsWith('-userset') ? 'yes' : 'no',
        },
      })),
      ...graph.edges.map((edge) => ({
        data: {
          id: `e${edge.id}-${edge.source}-${edge.target}`,
          source: edge.source,
          target: edge.target,
          label: edge.relation,
          tuple: edge.tuple,
          userset: edge.userset ? 'yes' : 'no',
        },
      })),
    ];

    this.cy = cytoscape({
      container: this.host().nativeElement,
      elements,
      style: [
        {
          selector: 'node',
          style: {
            label: 'data(label)',
            'background-color': (node) => OBJECT_KINDS[node.data('kind')]?.hex ?? '#64748b',
            color: '#e2e8f0',
            'font-size': 10,
            'text-valign': 'bottom',
            'text-margin-y': 5,
            width: 22,
            height: 22,
            'border-width': 2,
            'border-color': '#0f172a',
          },
        },
        {
          // Los usersets se dibujan como rombos huecos: no son entidades, son conjuntos.
          selector: 'node[userset = "yes"]',
          style: {
            shape: 'diamond',
            'background-opacity': 0.25,
            'border-style': 'dashed',
            'border-color': (node) => OBJECT_KINDS[node.data('kind')]?.hex ?? '#64748b',
            width: 26,
            height: 26,
          },
        },
        {
          selector: 'edge',
          style: {
            label: 'data(label)',
            'font-size': 8,
            color: '#94a3b8',
            'text-background-color': '#020617',
            'text-background-opacity': 0.85,
            'text-background-padding': '2px',
            width: 1.2,
            'line-color': '#334155',
            'target-arrow-color': '#334155',
            'target-arrow-shape': 'triangle',
            'arrow-scale': 0.8,
            'curve-style': 'bezier',
          },
        },
        {
          selector: 'edge[userset = "yes"]',
          style: { 'line-style': 'dashed' },
        },
        {
          selector: '.faded',
          style: { opacity: 0.12 },
        },
        {
          selector: '.path-edge',
          style: {
            'line-color': '#34d399',
            'target-arrow-color': '#34d399',
            width: 3,
            color: '#6ee7b7',
            'font-size': 10,
            'font-weight': 'bold',
            opacity: 1,
          },
        },
        {
          selector: '.path-node',
          style: {
            'border-color': '#34d399',
            'border-width': 3,
            opacity: 1,
          },
        },
      ],
      layout: this.buildLayout(),
    });

    this.cy.on('tap', 'edge', (event) => this.selection.set(event.target.data('tuple')));
    this.cy.on('tap', 'node', (event) => this.selection.set(event.target.data('reference')));
  }

  /**
   * Parámetros de disposición según la opción elegida.
   *
   * Ninguna disposición automática hace legible un grafo denso: las etiquetas van debajo de
   * cada nodo y acaban tocándose. Por eso hay tres y no una, y por eso existe además el
   * filtro "centrar en" — que es también lo que se hace en un sistema real, donde el grafo
   * completo tiene millones de nodos y solo se puede mirar por vecindarios.
   *
   * «Por capas» es la que mejor se lee para autorización: las aristas van del sujeto al
   * objeto, así que las capas quedan ordenadas de quién tiene acceso hacia sobre qué.
   */
  private buildLayout(): cytoscape.LayoutOptions {
    switch (this.layoutName) {
      case 'breadthfirst':
        return {
          name: 'breadthfirst',
          animate: false,
          directed: true,
          padding: 40,
          spacingFactor: 1.6,
          avoidOverlap: true,
        } as unknown as cytoscape.LayoutOptions;

      case 'concentric':
        return {
          name: 'concentric',
          animate: false,
          padding: 40,
          minNodeSpacing: 60,
          concentric: (node: cytoscape.NodeSingular) => node.degree(false),
          levelWidth: () => 2,
        } as unknown as cytoscape.LayoutOptions;

      default:
        return {
          name: 'cose',
          animate: false,
          padding: 40,
          nodeRepulsion: () => 60000,
          idealEdgeLength: () => 150,
          nodeOverlap: 60,
          gravity: 40,
          componentSpacing: 140,
        } as unknown as cytoscape.LayoutOptions;
    }
  }

  /**
   * Evalúa una pregunta y resalta en el grafo las tuplas que concedieron el acceso.
   *
   * Es la conexión entre las dos formas de mirar lo mismo: el Explorer lo cuenta como texto,
   * el grafo lo enseña como recorrido.
   */
  protected async highlightPath(): Promise<void> {
    if (!this.cy) {
      return;
    }

    const decision = await this.api.explain({
      subject: this.pathSubject,
      relation: this.pathRelation,
      object: this.pathObject,
      explain: true,
    });

    if (!decision.allowed) {
      this.clearHighlight();
      this.pathMessage.set(
        `DENY: no hay ningún camino que resaltar. ${decision.reason}`,
      );
      return;
    }

    const tuples = new Set(
      decision.paths.flatMap((path) =>
        path.steps.map((step) => step.tuple).filter((tuple): tuple is string => !!tuple),
      ),
    );

    this.cy.elements().addClass('faded');

    const edges = this.cy.edges().filter((edge) => tuples.has(edge.data('tuple')));

    edges.removeClass('faded').addClass('path-edge');
    edges.connectedNodes().removeClass('faded').addClass('path-node');

    this.highlighted.set(true);
    this.pathMessage.set(
      `ALLOW por ${decision.paths.length} camino(s), ${tuples.size} tupla(s) implicadas. ` +
        `El más corto: ${decision.paths[0]?.chain ?? ''}`,
    );
  }

  protected clearHighlight(): void {
    this.clearHighlightState();
    this.pathMessage.set(null);
  }

  private clearHighlightState(): void {
    this.cy?.elements().removeClass('faded path-edge path-node');
    this.highlighted.set(false);
  }
}
