import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { CatalogStore } from './core/state/catalog.store';
import { IdentityStore } from './core/state/identity.store';

interface NavItem {
  path: string;
  label: string;
  icon: string;
  hint: string;
}

/**
 * Shell de la aplicación: navegación y selector de identidad.
 *
 * El selector va en la barra superior, siempre visible, y eso es una decisión de diseño con
 * intención: en un laboratorio de autorización la pregunta "¿y si fuera otra persona?" se
 * hace continuamente, y tenerla a un clic de distancia en cualquier pantalla es lo que
 * convierte la aplicación en algo con lo que se experimenta en lugar de algo que se consulta.
 */
@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <div class="flex min-h-screen flex-col">
      <header class="sticky top-0 z-20 border-b border-slate-800 bg-slate-950/90 backdrop-blur">
        <div class="flex flex-wrap items-center gap-4 px-5 py-3">
          <div class="flex items-baseline gap-3">
            <h1 class="text-base font-bold tracking-tight text-slate-100">
              Authorization <span class="text-violet-400">Playground</span>
            </h1>
            <span class="hidden text-xs text-slate-500 sm:inline">ReBAC · estilo Zanzibar</span>
          </div>

          <div class="ml-auto flex flex-wrap items-center gap-3">
            <span
              class="rounded-md px-2 py-1 text-xs ring-1 ring-inset"
              [class]="
                catalog.authorizationMode() === 'Remote'
                  ? 'bg-amber-500/10 text-amber-300 ring-amber-500/30'
                  : 'bg-slate-800 text-slate-400 ring-slate-700'
              "
              [title]="
                catalog.authorizationMode() === 'Remote'
                  ? 'El negocio pregunta al módulo de control de acceso por HTTP, como en un despliegue real. Cada check es un salto de red.'
                  : 'El motor de control de acceso corre dentro del servicio de negocio. Latencia ~0, pero se esconden los problemas de latencia, caché y consistencia.'
              "
            >
              control de acceso: <b>{{ catalog.authorizationMode() }}</b>
            </span>

            <label class="flex items-center gap-2 text-xs text-slate-400">
              <span class="whitespace-nowrap">Actuando como</span>
              <!--
                La selección se marca en cada <option> y no con [value] en el <select>.
                Con [value], el valor se asigna antes de que existan las opciones, así que el
                navegador se queda mostrando la primera de la lista mientras el estado real es
                otro: el desplegable decía una cosa y el resto de la pantalla respondía por
                otra persona.
              -->
              <select
                class="rounded-md border border-slate-700 bg-slate-900 px-2 py-1.5 text-sm text-slate-100 focus:border-violet-500 focus:outline-none"
                (change)="onActAs($event)"
              >
                @for (user of identity.users(); track user.id) {
                  <option [value]="user.id" [selected]="user.id === identity.currentId()">
                    {{ user.name }}
                  </option>
                }
              </select>
            </label>
          </div>
        </div>

        @if (identity.current(); as user) {
          @if (user.story) {
            <p class="border-t border-slate-800/60 px-5 py-2 text-xs text-slate-400">
              <b class="text-slate-300">{{ user.name }}:</b> {{ user.story }}
            </p>
          }
        }
      </header>

      <div class="flex flex-1 flex-col lg:flex-row">
        <nav class="shrink-0 border-b border-slate-800 lg:w-60 lg:border-r lg:border-b-0">
          <ul class="flex gap-1 overflow-x-auto p-2 lg:flex-col lg:overflow-visible">
            @for (item of navItems; track item.path) {
              <li>
                <a
                  [routerLink]="item.path"
                  routerLinkActive="bg-violet-500/15 text-violet-200 ring-1 ring-inset ring-violet-500/30"
                  class="flex items-center gap-2.5 rounded-lg px-3 py-2 text-sm whitespace-nowrap text-slate-400 hover:bg-slate-800/60 hover:text-slate-200"
                  [title]="item.hint"
                >
                  <span aria-hidden="true">{{ item.icon }}</span>
                  <span>{{ item.label }}</span>
                </a>
              </li>
            }
          </ul>
        </nav>

        <main class="min-w-0 flex-1 p-5">
          @if (catalog.error(); as error) {
            <div
              class="mb-4 rounded-lg border border-rose-500/30 bg-rose-500/10 p-4 text-sm text-rose-200"
            >
              {{ error }}
            </div>
          }
          <router-outlet />
        </main>
      </div>
    </div>
  `,
})
export class App implements OnInit {
  protected readonly identity = inject(IdentityStore);
  protected readonly catalog = inject(CatalogStore);

  protected readonly navItems: NavItem[] = [
    {
      path: '/tour',
      label: 'Tour guiado',
      icon: '🎓',
      hint: 'Once lecciones: predice la respuesta y comprueba ejecutándola contra el motor.',
    },
    {
      path: '/cases',
      label: 'Casos guiados',
      icon: '📌',
      hint: '16 preguntas con su respuesta actual y qué enseña cada una.',
    },
    {
      path: '/explorer',
      label: 'Explorer',
      icon: '🔍',
      hint: '¿Puede X hacer Y sobre Z? Y, sobre todo, POR QUÉ.',
    },
    {
      path: '/relationships',
      label: 'Relaciones',
      icon: '🔗',
      hint: 'Crear y borrar tuplas, y ver qué decisiones cambian al hacerlo.',
    },
    {
      path: '/graph',
      label: 'Grafo',
      icon: '🕸️',
      hint: 'El grafo por el que viaja el acceso. No es el diagrama de entidades.',
    },
    {
      path: '/objects',
      label: 'Listado autorizado',
      icon: '📋',
      hint: '«Dame todo lo que Juan puede ver»: naive contra expansión inversa.',
    },
    {
      path: '/comparison',
      label: 'RBAC vs ReBAC',
      icon: '⚖️',
      hint: 'La misma pregunta por los dos modelos, con el coste de cada uno.',
    },
    {
      path: '/model',
      label: 'Modelo',
      icon: '📐',
      hint: 'Qué relaciones existen y cómo se derivan.',
    },
    {
      path: '/workspace',
      label: 'Espacio de trabajo',
      icon: '🗂️',
      hint: 'La aplicación de negocio de verdad, gobernada por el módulo.',
    },
    {
      path: '/audit',
      label: 'Auditoría',
      icon: '📜',
      hint: 'Cada decisión registrada, con su camino.',
    },
  ];

  ngOnInit(): void {
    void this.catalog.load();
  }

  protected onActAs(event: Event): void {
    this.identity.actAs((event.target as HTMLSelectElement).value);
  }
}
