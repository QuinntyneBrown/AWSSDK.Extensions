import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogRef, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { Note } from '../models';

export interface NoteDialogData {
  mode: 'create' | 'edit';
  note?: Note;
}

@Component({
  selector: 'app-note-dialog',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatButtonToggleModule
  ],
  templateUrl: './note-dialog.html',
  styleUrl: './note-dialog.scss'
})
export class NoteDialog {
  title: string;
  content: string;
  color: string;

  colors = ['default', 'yellow', 'green', 'blue', 'pink', 'purple'];

  constructor(
    public dialogRef: MatDialogRef<NoteDialog>,
    @Inject(MAT_DIALOG_DATA) public data: NoteDialogData
  ) {
    this.title = data.note?.title ?? '';
    this.content = data.note?.content ?? '';
    this.color = data.note?.color ?? 'default';
  }

  onCancel(): void {
    this.dialogRef.close();
  }

  onSave(): void {
    if (this.title.trim()) {
      this.dialogRef.close({
        title: this.title,
        content: this.content,
        color: this.color
      });
    }
  }
}
