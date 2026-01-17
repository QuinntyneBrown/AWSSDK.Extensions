import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { TodoItem, CreateTodoItemRequest, UpdateTodoItemRequest } from '../models';

@Injectable({ providedIn: 'root' })
export class TodoService {
  private readonly apiUrl = 'http://localhost:5000/api/todo';

  constructor(private http: HttpClient) {}

  getAll(): Observable<TodoItem[]> {
    return this.http.get<TodoItem[]>(this.apiUrl);
  }

  getById(id: string): Observable<TodoItem> {
    return this.http.get<TodoItem>(`${this.apiUrl}/${id}`);
  }

  create(request: CreateTodoItemRequest): Observable<TodoItem> {
    return this.http.post<TodoItem>(this.apiUrl, request);
  }

  update(id: string, request: UpdateTodoItemRequest): Observable<TodoItem> {
    return this.http.put<TodoItem>(`${this.apiUrl}/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }
}
