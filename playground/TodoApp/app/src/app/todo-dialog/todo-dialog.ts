import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogRef, MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { TodoItem } from '../models';

export interface TodoDialogData {
  mode: 'create' | 'edit';
  todo?: TodoItem;
}

@Component({
  selector: 'app-todo-dialog',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule
  ],
  templateUrl: './todo-dialog.html',
  styleUrl: './todo-dialog.scss'
})
export class TodoDialog {
  title: string;
  description: string;

  constructor(
    public dialogRef: MatDialogRef<TodoDialog>,
    @Inject(MAT_DIALOG_DATA) public data: TodoDialogData
  ) {
    this.title = data.todo?.title ?? '';
    this.description = data.todo?.description ?? '';
  }

  onCancel(): void {
    this.dialogRef.close();
  }

  onSave(): void {
    if (this.title.trim()) {
      this.dialogRef.close({
        title: this.title,
        description: this.description,
        isCompleted: this.data.todo?.isCompleted ?? false
      });
    }
  }
}
