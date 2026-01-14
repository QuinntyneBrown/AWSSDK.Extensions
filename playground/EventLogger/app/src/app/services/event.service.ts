import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { LogEvent, CreateLogEventRequest } from '../models';

@Injectable({ providedIn: 'root' })
export class EventService {
  private readonly apiUrl = 'http://localhost:5000/api/event';

  constructor(private http: HttpClient) {}

  getAll(level?: string): Observable<LogEvent[]> {
    const url = level ? `${this.apiUrl}?level=${level}` : this.apiUrl;
    return this.http.get<LogEvent[]>(url);
  }

  create(request: CreateLogEventRequest): Observable<LogEvent> {
    return this.http.post<LogEvent>(this.apiUrl, request);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }

  clear(): Observable<void> {
    return this.http.delete<void>(this.apiUrl);
  }
}
