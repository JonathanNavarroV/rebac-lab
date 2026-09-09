export interface LabUser {
  id: string;
  name: string;
  subject: string;
  story: string | null;
}

export interface CatalogEntity {
  id: string;
  name: string;
  objectRef: string;
  parent: string | null;
  story: string | null;
}

export interface Catalog {
  users: CatalogEntity[];
  organizations: CatalogEntity[];
  teams: CatalogEntity[];
  groups: CatalogEntity[];
  projects: CatalogEntity[];
  folders: CatalogEntity[];
  resources: CatalogEntity[];
  authorizationMode: string;
}

export interface ProjectView {
  id: string;
  name: string;
  objectRef: string;
  description: string | null;
  organizationId: string | null;
  canView: boolean;
  canEdit: boolean;
  canDelete: boolean;
  canPublish: boolean;
}

/**
 * `checksPerformed` y `authorizationMs` no son telemetría: son parte de lo que enseña la
 * pantalla. Pintar una lista de N proyectos con sus botones cuesta 3N+1 preguntas de
 * autorización, y ese número puesto al lado del modo (InProcess/Remote) es lo que hace
 * evidente por qué existe el BatchCheck.
 */
export interface ProjectList {
  projects: ProjectView[];
  totalInCatalog: number;
  visible: number;
  authorizationMode: string;
  checksPerformed: number;
  authorizationMs: number;
}

export interface ResourceView {
  id: string;
  name: string;
  objectRef: string;
  parentRef: string | null;
  content: string | null;
  canView: boolean;
  canEdit: boolean;
  canDelete: boolean;
}

export interface ResourceList {
  resources: ResourceView[];
  totalInCatalog: number;
  visible: number;
  authorizationMode: string;
  checksPerformed: number;
  authorizationMs: number;
}

export interface ShareResult {
  tuple: string;
  explanation: string;
}

export interface ModelAnswer {
  model: string;
  allowed: boolean;
  reason: string;
  reasoning: string[];
}

export interface RbacStats {
  roles: number;
  permissions: number;
  userRoleAssignments: number;
  totalRows: number;
  rowsToAddOneResource: number;
}

export interface Comparison {
  user: string;
  action: string;
  object: string;
  rebac: ModelAnswer;
  rbac: ModelAnswer;
  agree: boolean;
  analysis: string;
  rbacStats: RbacStats;
  rebacTupleCount: number;
}

/** Cuerpo del 403 que devuelve el negocio cuando el módulo deniega. */
export interface ForbiddenPayload {
  title: string;
  status: number;
  detail: string;
  subject: string;
  relation: string;
  object: string;
  reason: string;
}
