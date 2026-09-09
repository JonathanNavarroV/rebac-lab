/**
 * Espejo de los contratos de `Playground.Contracts.AccessControl`.
 *
 * Se mantienen a mano en lugar de generarlos: son pocos y estables, y escribirlos obliga a
 * leerlos, que en un proyecto cuyo objetivo es entender el modelo no es mala cosa.
 */

export interface CheckRequest {
  subject: string;
  relation: string;
  object: string;
  explain?: boolean;
  modelId?: string | null;
  maxDepth?: number | null;
}

export interface PathStep {
  kind: string;
  description: string;
  tuple: string | null;
}

export interface AuthorizationPath {
  length: number;
  chain: string;
  steps: PathStep[];
}

/** Un nodo del árbol de evaluación. Es la explicación completa de una decisión. */
export interface CheckTraceNode {
  kind: string;
  label: string;
  allowed: boolean;
  detail: string | null;
  tuple: string | null;
  depth: number;
  fromCache: boolean;
  children: CheckTraceNode[];
}

export interface CheckMetrics {
  storeQueries: number;
  tuplesRead: number;
  nodesEvaluated: number;
  maxDepthReached: number;
  memoizationHits: number;
  cyclesDetected: number;
  confirmationChecks: number;
  durationMs: number;
  evaluationMode: string | null;
}

export interface CheckResponse {
  allowed: boolean;
  subject: string;
  relation: string;
  object: string;
  reason: string;
  paths: AuthorizationPath[];
  suggestedTuples: string[];
  trace: CheckTraceNode | null;
  modelId: string | null;
  metrics: CheckMetrics;
}

export interface ExpandNode {
  kind: string;
  label: string;
  object: string | null;
  relation: string | null;
  subject: string | null;
  tuple: string | null;
  children: ExpandNode[];
}

export interface ExpandResponse {
  object: string;
  relation: string;
  root: ExpandNode;
  leaves: string[];
  metrics: CheckMetrics;
}

export interface AuthorizedObject {
  object: string;
  reason: string;
  path: AuthorizationPath | null;
}

export interface ListObjectsResponse {
  subject: string;
  relation: string;
  objectType: string;
  strategy: string;
  objects: AuthorizedObject[];
  denied: AuthorizedObject[];
  metrics: CheckMetrics;
}

export interface ListObjectsComparison {
  naive: ListObjectsResponse;
  reverse: ListObjectsResponse;
  sameResult: boolean;
  verdict: string;
}

export interface Relationship {
  id: number;
  tuple: string;
  object: string;
  objectType: string;
  objectId: string;
  relation: string;
  subject: string;
  subjectType: string;
  subjectId: string;
  subjectRelation: string | null;
  isUserset: boolean;
  isWildcard: boolean;
  createdAt: string;
}

/** Una decisión que cambió de resultado al crear o borrar una relación. */
export interface DecisionChange {
  subject: string;
  relation: string;
  object: string;
  before: boolean;
  after: boolean;
  reason: string;
}

export interface RelationshipMutationResponse {
  relationship: Relationship | null;
  created: boolean;
  affectedChecks: DecisionChange[];
}

export interface ModelRelation {
  name: string;
  expression: string;
  kind: string;
  isDirectlyAssignable: boolean;
  acceptedSubjects: string[];
  comment: string | null;
}

export interface ModelType {
  name: string;
  relations: ModelRelation[];
  comment: string | null;
}

export interface AuthorizationModel {
  id: string;
  schemaVersion: string;
  name: string | null;
  description: string | null;
  dsl: string;
  types: ModelType[];
  isCurrent: boolean;
}

export interface ModelTemplate {
  key: string;
  name: string;
  description: string;
  dsl: string;
}

export interface GraphNode {
  id: string;
  label: string;
  kind: string;
  displayName: string | null;
}

export interface GraphEdge {
  id: number;
  source: string;
  target: string;
  relation: string;
  userset: boolean;
  wildcard: boolean;
  tuple: string;
}

export interface GraphResponse {
  nodes: GraphNode[];
  edges: GraphEdge[];
}

export interface AuditEntry {
  id: number;
  timestamp: string;
  subject: string;
  relation: string;
  object: string;
  allowed: boolean;
  reason: string;
  pathJson: string | null;
  modelId: string | null;
  evaluationMode: string | null;
  durationMs: number;
  storeQueries: number;
  origin: string | null;
}

export interface GuidedCase {
  code: string;
  title: string;
  subject: string;
  relation: string;
  object: string;
  expectedAllowed: boolean;
  actualAllowed: boolean | null;
  stillMatchesExpectation: boolean;
  whatItTeaches: string;
  whyRbacStruggles: string | null;
  minimumPaths: number;
}
