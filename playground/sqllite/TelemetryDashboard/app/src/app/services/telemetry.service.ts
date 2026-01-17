import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { TelemetryPoint, DashboardPanel, CreateTelemetryPointRequest, CreatePanelRequest } from '../models';

@Injectable({ providedIn: 'root' })
export class TelemetryService {
  private readonly apiUrl = 'http://localhost:5000/api/telemetry';

  constructor(private http: HttpClient) {}

  getPoints(): Observable<TelemetryPoint[]> {
    return this.http.get<TelemetryPoint[]>(`${this.apiUrl}/points`);
  }

  createPoint(request: CreateTelemetryPointRequest): Observable<TelemetryPoint> {
    return this.http.post<TelemetryPoint>(`${this.apiUrl}/points`, request);
  }

  updateValue(id: string, value: number): Observable<TelemetryPoint> {
    return this.http.put<TelemetryPoint>(`${this.apiUrl}/points/${id}/value`, value);
  }

  deletePoint(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/points/${id}`);
  }

  getPanels(): Observable<DashboardPanel[]> {
    return this.http.get<DashboardPanel[]>(`${this.apiUrl}/panels`);
  }

  createPanel(request: CreatePanelRequest): Observable<DashboardPanel> {
    return this.http.post<DashboardPanel>(`${this.apiUrl}/panels`, request);
  }

  deletePanel(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/panels/${id}`);
  }
}
