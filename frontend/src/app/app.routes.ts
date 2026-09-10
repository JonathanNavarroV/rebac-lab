import { Routes } from '@angular/router';

/**
 * Todas las pantallas son lazy, como en tu frontend de psinet.
 *
 * La ruta por defecto son los casos guiados y no el Explorer: entrar directamente a un
 * formulario en blanco con tres desplegables no dice nada, mientras que una lista de
 * preguntas con su respuesta y su explicación es un punto de partida que se entiende solo.
 */
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'cases' },

  {
    path: 'cases',
    title: 'Casos guiados · Authorization Playground',
    loadComponent: () => import('./features/cases/cases.component').then((m) => m.CasesComponent),
  },
  {
    path: 'tour',
    title: 'Tour guiado · Authorization Playground',
    loadComponent: () => import('./features/tour/tour.component').then((m) => m.TourComponent),
  },
  {
    path: 'explorer',
    title: 'Authorization Explorer',
    loadComponent: () =>
      import('./features/explorer/explorer.component').then((m) => m.ExplorerComponent),
  },
  {
    path: 'relationships',
    title: 'Relaciones · Authorization Playground',
    loadComponent: () =>
      import('./features/relationships/relationships.component').then(
        (m) => m.RelationshipsComponent,
      ),
  },
  {
    path: 'graph',
    title: 'Grafo de relaciones',
    loadComponent: () => import('./features/graph/graph.component').then((m) => m.GraphComponent),
  },
  {
    path: 'objects',
    title: 'Listado autorizado',
    loadComponent: () =>
      import('./features/objects/objects.component').then((m) => m.ObjectsComponent),
  },
  {
    path: 'comparison',
    title: 'RBAC vs ReBAC',
    loadComponent: () =>
      import('./features/comparison/comparison.component').then((m) => m.ComparisonComponent),
  },
  {
    path: 'model',
    title: 'Modelo de autorización',
    loadComponent: () => import('./features/model/model.component').then((m) => m.ModelComponent),
  },
  {
    path: 'workspace',
    title: 'Espacio de trabajo',
    loadComponent: () =>
      import('./features/workspace/workspace.component').then((m) => m.WorkspaceComponent),
  },
  {
    path: 'audit',
    title: 'Auditoría de decisiones',
    loadComponent: () => import('./features/audit/audit.component').then((m) => m.AuditComponent),
  },

  { path: '**', redirectTo: 'cases' },
];
