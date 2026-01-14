import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ConfigFile, CreateConfigFileRequest } from '../models';

@Injectable({ providedIn: 'root' })
export class ConfigService {
  private readonly apiUrl = 'http://localhost:5000/api/config';

  constructor(private http: HttpClient) {}

  getAll(fileType?: string): Observable<ConfigFile[]> {
    const url = fileType ? `${this.apiUrl}?fileType=${fileType}` : this.apiUrl;
    return this.http.get<ConfigFile[]>(url);
  }

  getFileTypes(): Observable<string[]> {
    return this.http.get<string[]>(`${this.apiUrl}/types`);
  }

  getById(id: string): Observable<ConfigFile> {
    return this.http.get<ConfigFile>(`${this.apiUrl}/${id}`);
  }

  getContent(id: string): Observable<string> {
    return this.http.get(`${this.apiUrl}/${id}/content`, { responseType: 'text' });
  }

  create(request: CreateConfigFileRequest): Observable<ConfigFile> {
    return this.http.post<ConfigFile>(this.apiUrl, request);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }
}
