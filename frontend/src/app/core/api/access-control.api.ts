import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiConfig } from '../config/api-config';
import {
  AuditEntry,
  AuthorizationModel,
  CheckRequest,
  CheckResponse,
  ExpandResponse,
  GraphResponse,
  GuidedCase,
  ListObjectsComparison,
  ListObjectsResponse,
  ModelTemplate,
  Relationship,
  RelationshipMutationResponse,
} from '../models/access-control.models';

/**
 * Cliente del módulo de control de acceso.
 *
 * Fíjate en que todos los métodos reciben el sujeto de forma explícita. No hay ningún
 * "usuario actual" implícito: preguntar por Ana desde la sesión de Juan es una operación
 * normal y perfectamente legítima aquí, porque este servicio decide sobre sujetos, no sobre
 * sesiones.
 */
@Injectable({ providedIn: 'root' })
export class AccessControlApi {
  private readonly http = inject(HttpClient);
  private readonly base = ApiConfig.accessControl;

  // ── Decisiones ────────────────────────────────────────────────────────────

  check(request: CheckRequest): Promise<CheckResponse> {
    return firstValueFrom(
      this.http.post<CheckResponse>(`${this.base}/access-control/check`, request),
    );
  }

  /** Igual que `check` pero explorando todas las ramas: traza completa y todos los caminos. */
  explain(request: CheckRequest): Promise<CheckResponse> {
    return firstValueFrom(
      this.http.post<CheckResponse>(`${this.base}/access-control/explain`, request),
    );
  }

  expand(object: string, relation: string): Promise<ExpandResponse> {
    return firstValueFrom(
      this.http.post<ExpandResponse>(`${this.base}/access-control/expand`, { object, relation }),
    );
  }

  listObjects(
    subject: string,
    relation: string,
    objectType: string,
    strategy: 'naive' | 'reverse' = 'reverse',
  ): Promise<ListObjectsResponse> {
    return firstValueFrom(
      this.http.post<ListObjectsResponse>(`${this.base}/access-control/list-objects`, {
        subject,
        relation,
        objectType,
        strategy,
      }),
    );
  }

  compareListStrategies(
    subject: string,
    relation: string,
    objectType: string,
  ): Promise<ListObjectsComparison> {
    return firstValueFrom(
      this.http.post<ListObjectsComparison>(`${this.base}/access-control/list-objects/compare`, {
        subject,
        relation,
        objectType,
      }),
    );
  }

  // ── Relaciones ────────────────────────────────────────────────────────────

  getRelationships(filters: Record<string, string | undefined> = {}): Promise<Relationship[]> {
    let params = new HttpParams();

    Object.entries(filters).forEach(([key, value]) => {
      if (value) {
        params = params.set(key, value);
      }
    });

    return firstValueFrom(
      this.http.get<Relationship[]>(`${this.base}/access-control/relationships`, { params }),
    );
  }

  createRelationship(tuple: string): Promise<RelationshipMutationResponse> {
    return firstValueFrom(
      this.http.post<RelationshipMutationResponse>(
        `${this.base}/access-control/relationships`,
        { tuple },
      ),
    );
  }

  deleteRelationship(id: number): Promise<RelationshipMutationResponse> {
    return firstValueFrom(
      this.http.delete<RelationshipMutationResponse>(
        `${this.base}/access-control/relationships/${id}`,
      ),
    );
  }

  // ── Modelo ────────────────────────────────────────────────────────────────

  getModels(): Promise<AuthorizationModel[]> {
    return firstValueFrom(
      this.http.get<AuthorizationModel[]>(`${this.base}/access-control/models`),
    );
  }

  getModelTemplates(): Promise<ModelTemplate[]> {
    return firstValueFrom(
      this.http.get<ModelTemplate[]>(`${this.base}/access-control/models/templates`),
    );
  }

  publishModel(dsl: string, name?: string, description?: string): Promise<AuthorizationModel> {
    return firstValueFrom(
      this.http.post<AuthorizationModel>(`${this.base}/access-control/models`, {
        dsl,
        name,
        description,
      }),
    );
  }

  // ── Grafo, auditoría y casos ──────────────────────────────────────────────

  getGraph(focus?: string, depth = 3): Promise<GraphResponse> {
    let params = new HttpParams().set('depth', depth);

    if (focus) {
      params = params.set('focus', focus);
    }

    return firstValueFrom(
      this.http.get<GraphResponse>(`${this.base}/access-control/graph`, { params }),
    );
  }

  getAudit(take = 100): Promise<AuditEntry[]> {
    return firstValueFrom(
      this.http.get<AuditEntry[]>(`${this.base}/access-control/audit`, {
        params: new HttpParams().set('take', take),
      }),
    );
  }

  getGuidedCases(): Promise<GuidedCase[]> {
    return firstValueFrom(this.http.get<GuidedCase[]>(`${this.base}/access-control/cases`));
  }

  resetScenario(): Promise<{ reset: boolean; tuples: number }> {
    return firstValueFrom(
      this.http.post<{ reset: boolean; tuples: number }>(
        `${this.base}/access-control/seed/reset`,
        {},
      ),
    );
  }
}
