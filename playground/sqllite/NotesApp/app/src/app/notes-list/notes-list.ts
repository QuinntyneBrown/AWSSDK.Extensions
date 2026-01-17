import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { BehaviorSubject } from 'rxjs';
import { NoteService } from '../services/note.service';
import { Note } from '../models';
import { NoteDialog } from '../note-dialog/note-dialog';

@Component({
  selector: 'app-notes-list',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatButtonModule, MatIconModule, MatDialogModule],
  templateUrl: './notes-list.html',
  styleUrl: './notes-list.scss'
})
export class NotesList implements OnInit {
  private notesSubject = new BehaviorSubject<Note[]>([]);
  notes$ = this.notesSubject.asObservable();

  constructor(
    private noteService: NoteService,
    private dialog: MatDialog
  ) {}

  ngOnInit(): void {
    this.loadNotes();
  }

  private loadNotes(): void {
    this.noteService.getAll().subscribe(notes => this.notesSubject.next(notes));
  }

  openCreateDialog(): void {
    const dialogRef = this.dialog.open(NoteDialog, {
      width: '500px',
      data: { mode: 'create' }
    });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.noteService.create(result).subscribe(() => this.loadNotes());
      }
    });
  }

  openEditDialog(note: Note): void {
    const dialogRef = this.dialog.open(NoteDialog, {
      width: '500px',
      data: { mode: 'edit', note }
    });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.noteService.update(note.id, result).subscribe(() => this.loadNotes());
      }
    });
  }

  deleteNote(note: Note): void {
    this.noteService.delete(note.id).subscribe(() => this.loadNotes());
  }
}
