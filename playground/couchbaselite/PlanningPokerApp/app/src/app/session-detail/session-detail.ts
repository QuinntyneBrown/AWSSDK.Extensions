import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../services/session.service';
import { PokerSession } from '../models';
import { VoteDialog } from '../vote-dialog/vote-dialog';

@Component({
  selector: 'app-session-detail',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatChipsModule,
    MatDialogModule
  ],
  templateUrl: './session-detail.html',
  styleUrl: './session-detail.scss'
})
export class SessionDetail implements OnInit {
  private sessionSubject = new BehaviorSubject<PokerSession | null>(null);
  session$ = this.sessionSubject.asObservable();
  cardValues = ['0', '1', '2', '3', '5', '8', '13', '21', '?'];

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private sessionService: SessionService,
    private dialog: MatDialog
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.loadSession(id);
    }
  }

  private loadSession(id: string): void {
    this.sessionService.getById(id).subscribe(session => this.sessionSubject.next(session));
  }

  openVoteDialog(): void {
    const session = this.sessionSubject.value;
    if (!session) return;

    const dialogRef = this.dialog.open(VoteDialog, {
      width: '400px',
      data: { cardValues: this.cardValues }
    });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.sessionService.submitVote(session.id, result).subscribe(s => this.sessionSubject.next(s));
      }
    });
  }

  revealVotes(): void {
    const session = this.sessionSubject.value;
    if (session) {
      this.sessionService.revealVotes(session.id).subscribe(s => this.sessionSubject.next(s));
    }
  }

  resetVotes(): void {
    const session = this.sessionSubject.value;
    if (session) {
      this.sessionService.resetVotes(session.id).subscribe(s => this.sessionSubject.next(s));
    }
  }

  goBack(): void {
    this.router.navigate(['/']);
  }
}
