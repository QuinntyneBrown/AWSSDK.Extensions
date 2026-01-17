import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MatListModule } from '@angular/material/list';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../services/session.service';
import { PokerSession } from '../models';
import { SessionDialog } from '../session-dialog/session-dialog';

@Component({
  selector: 'app-sessions-list',
  standalone: true,
  imports: [
    CommonModule,
    MatListModule,
    MatButtonModule,
    MatIconModule,
    MatCardModule,
    MatDialogModule
  ],
  templateUrl: './sessions-list.html',
  styleUrl: './sessions-list.scss'
})
export class SessionsList implements OnInit {
  private sessionsSubject = new BehaviorSubject<PokerSession[]>([]);
  sessions$ = this.sessionsSubject.asObservable();

  constructor(
    private sessionService: SessionService,
    private dialog: MatDialog,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.loadSessions();
  }

  private loadSessions(): void {
    this.sessionService.getAll().subscribe(sessions => this.sessionsSubject.next(sessions));
  }

  openCreateDialog(): void {
    const dialogRef = this.dialog.open(SessionDialog, {
      width: '400px'
    });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.sessionService.create({ name: result }).subscribe(() => this.loadSessions());
      }
    });
  }

  openSession(session: PokerSession): void {
    this.router.navigate(['/session', session.id]);
  }

  deleteSession(session: PokerSession, event: Event): void {
    event.stopPropagation();
    this.sessionService.delete(session.id).subscribe(() => this.loadSessions());
  }
}
