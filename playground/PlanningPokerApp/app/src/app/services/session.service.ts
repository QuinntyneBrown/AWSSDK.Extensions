import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PokerSession, CreateSessionRequest, SubmitVoteRequest } from '../models';

@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly apiUrl = 'http://localhost:5000/api/session';

  constructor(private http: HttpClient) {}

  getAll(): Observable<PokerSession[]> {
    return this.http.get<PokerSession[]>(this.apiUrl);
  }

  getById(id: string): Observable<PokerSession> {
    return this.http.get<PokerSession>(`${this.apiUrl}/${id}`);
  }

  create(request: CreateSessionRequest): Observable<PokerSession> {
    return this.http.post<PokerSession>(this.apiUrl, request);
  }

  updateStory(id: string, story: string): Observable<PokerSession> {
    return this.http.put<PokerSession>(`${this.apiUrl}/${id}/story`, { story });
  }

  submitVote(id: string, request: SubmitVoteRequest): Observable<PokerSession> {
    return this.http.post<PokerSession>(`${this.apiUrl}/${id}/vote`, request);
  }

  revealVotes(id: string): Observable<PokerSession> {
    return this.http.post<PokerSession>(`${this.apiUrl}/${id}/reveal`, {});
  }

  resetVotes(id: string): Observable<PokerSession> {
    return this.http.post<PokerSession>(`${this.apiUrl}/${id}/reset`, {});
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }
}
