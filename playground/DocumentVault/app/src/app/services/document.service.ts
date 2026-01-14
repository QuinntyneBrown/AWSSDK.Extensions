import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Document } from '../models';

@Injectable({ providedIn: 'root' })
export class DocumentService {
  private readonly apiUrl = 'http://localhost:5000/api/document';

  constructor(private http: HttpClient) {}

  getAll(tag?: string): Observable<Document[]> {
    const url = tag ? `${this.apiUrl}?tag=${tag}` : this.apiUrl;
    return this.http.get<Document[]>(url);
  }

  getTags(): Observable<string[]> {
    return this.http.get<string[]>(`${this.apiUrl}/tags`);
  }

  getDownloadUrl(id: string): string {
    return `${this.apiUrl}/${id}/download`;
  }

  upload(file: File, tags: string[]): Observable<Document> {
    const formData = new FormData();
    formData.append('file', file);
    formData.append('tags', tags.join(','));
    return this.http.post<Document>(this.apiUrl, formData);
  }

  updateTags(id: string, tags: string[]): Observable<Document> {
    return this.http.put<Document>(`${this.apiUrl}/${id}/tags`, tags);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }
}
