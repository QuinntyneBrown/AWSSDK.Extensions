import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Bookmark, CreateBookmarkRequest } from '../models';

@Injectable({ providedIn: 'root' })
export class BookmarkService {
  private readonly apiUrl = 'http://localhost:5000/api/bookmark';

  constructor(private http: HttpClient) {}

  getAll(category?: string): Observable<Bookmark[]> {
    const url = category ? `${this.apiUrl}?category=${category}` : this.apiUrl;
    return this.http.get<Bookmark[]>(url);
  }

  getCategories(): Observable<string[]> {
    return this.http.get<string[]>(`${this.apiUrl}/categories`);
  }

  create(request: CreateBookmarkRequest): Observable<Bookmark> {
    return this.http.post<Bookmark>(this.apiUrl, request);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }
}
