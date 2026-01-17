import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { BehaviorSubject } from 'rxjs';
import { EventService } from '../services/event.service';
import { LogEvent } from '../models';
import { EventDialog } from '../event-dialog/event-dialog';

@Component({
  selector: 'app-events-list',
  standalone: true,
  imports: [
    CommonModule,
    MatButtonModule,
    MatIconModule,
    MatCardModule,
    MatChipsModule,
    MatSelectModule,
    MatFormFieldModule,
    MatDialogModule
  ],
  templateUrl: './events-list.html',
  styleUrl: './events-list.scss'
})
export class EventsList implements OnInit {
  private eventsSubject = new BehaviorSubject<LogEvent[]>([]);
  private selectedLevelSubject = new BehaviorSubject<string>('');

  events$ = this.eventsSubject.asObservable();
  selectedLevel$ = this.selectedLevelSubject.asObservable();

  levels = ['info', 'warning', 'error', 'debug'];

  constructor(
    private eventService: EventService,
    private dialog: MatDialog
  ) {}

  ngOnInit(): void {
    this.loadEvents();
  }

  private loadEvents(): void {
    const level = this.selectedLevelSubject.value;
    this.eventService.getAll(level || undefined).subscribe(events => this.eventsSubject.next(events));
  }

  onFilterChange(level: string): void {
    this.selectedLevelSubject.next(level);
    this.loadEvents();
  }

  openAddDialog(): void {
    const dialogRef = this.dialog.open(EventDialog, { width: '500px' });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.eventService.create(result).subscribe(() => this.loadEvents());
      }
    });
  }

  clearAll(): void {
    this.eventService.clear().subscribe(() => this.loadEvents());
  }

  getLevelClass(level: string): string {
    return `events-list__level--${level}`;
  }
}
