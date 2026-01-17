import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Asset, CreateAssetRequest, UpdateAssetRequest } from '../models';

@Injectable({ providedIn: 'root' })
export class AssetService {
  private readonly apiUrl = 'http://localhost:5000/api/asset';

  constructor(private http: HttpClient) {}

  getAll(status?: string): Observable<Asset[]> {
    const url = status ? `${this.apiUrl}?status=${status}` : this.apiUrl;
    return this.http.get<Asset[]>(url);
  }

  create(request: CreateAssetRequest): Observable<Asset> {
    return this.http.post<Asset>(this.apiUrl, request);
  }

  update(id: string, request: UpdateAssetRequest): Observable<Asset> {
    return this.http.put<Asset>(`${this.apiUrl}/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }
}
