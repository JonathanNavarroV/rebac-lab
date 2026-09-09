import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiConfig } from '../config/api-config';
import {
  Catalog,
  Comparison,
  LabUser,
  ProjectList,
  ProjectView,
  ResourceList,
  ResourceView,
  ShareResult,
} from '../models/business.models';

/**
 * Cliente del servicio de negocio.
 *
 * A diferencia del cliente del módulo de control de acceso, aquí NO se pasa el sujeto: va
 * implícito en la cabecera de identidad que pone el interceptor. Es la diferencia entre
 * "quién pregunta" y "por quién se pregunta", y se nota al comparar las dos pantallas: en el
 * Explorer eliges a cualquiera, en el espacio de trabajo solo ves lo tuyo.
 */
@Injectable({ providedIn: 'root' })
export class BusinessApi {
  private readonly http = inject(HttpClient);
  private readonly base = ApiConfig.business;

  getUsers(): Promise<LabUser[]> {
    return firstValueFrom(this.http.get<LabUser[]>(`${this.base}/auth/users`));
  }

  getCatalog(): Promise<Catalog> {
    return firstValueFrom(this.http.get<Catalog>(`${this.base}/catalog`));
  }

  getProjects(): Promise<ProjectList> {
    return firstValueFrom(this.http.get<ProjectList>(`${this.base}/projects`));
  }

  updateProject(id: string, name: string, description: string | null): Promise<ProjectView> {
    return firstValueFrom(
      this.http.put<ProjectView>(`${this.base}/projects/${id}`, { name, description }),
    );
  }

  publishProject(id: string): Promise<ProjectView> {
    return firstValueFrom(this.http.post<ProjectView>(`${this.base}/projects/${id}/publish`, {}));
  }

  deleteProject(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.base}/projects/${id}`));
  }

  getResources(parentRef?: string): Promise<ResourceList> {
    let params = new HttpParams();

    if (parentRef) {
      params = params.set('parentRef', parentRef);
    }

    return firstValueFrom(this.http.get<ResourceList>(`${this.base}/resources`, { params }));
  }

  updateResource(id: string, name: string, content: string | null): Promise<ResourceView> {
    return firstValueFrom(
      this.http.put<ResourceView>(`${this.base}/resources/${id}`, { name, content }),
    );
  }

  shareResource(id: string, shareWith: string): Promise<ShareResult> {
    return firstValueFrom(
      this.http.post<ShareResult>(`${this.base}/resources/${id}/share`, { shareWith }),
    );
  }

  compare(userId: string, action: string, objectRef: string): Promise<Comparison> {
    return firstValueFrom(
      this.http.post<Comparison>(`${this.base}/comparison/check`, { userId, action, objectRef }),
    );
  }
}
