import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Note, CreateNoteRequest } from '../models';

@Injectable({ providedIn: 'root' })
export class NoteService {
  private readonly apiUrl = 'http://localhost:5000/api/note';

  constructor(private http: HttpClient) {}

  getAll(): Observable<Note[]> {
    return this.http.get<Note[]>(this.apiUrl);
  }

  getById(id: string): Observable<Note> {
    return this.http.get<Note>(`${this.apiUrl}/${id}`);
  }

  create(request: CreateNoteRequest): Observable<Note> {
    return this.http.post<Note>(this.apiUrl, request);
  }

  update(id: string, request: CreateNoteRequest): Observable<Note> {
    return this.http.put<Note>(`${this.apiUrl}/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }
}
