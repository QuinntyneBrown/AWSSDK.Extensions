import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatDialogRef, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';

@Component({
  selector: 'app-bookmark-dialog',
  standalone: true,
  imports: [
    FormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule
  ],
  templateUrl: './bookmark-dialog.html',
  styleUrl: './bookmark-dialog.scss'
})
export class BookmarkDialog {
  title = '';
  url = '';
  category = '';
  description = '';

  constructor(public dialogRef: MatDialogRef<BookmarkDialog>) {}

  onCancel(): void {
    this.dialogRef.close();
  }

  onSave(): void {
    if (this.title.trim() && this.url.trim()) {
      this.dialogRef.close({
        title: this.title,
        url: this.url,
        category: this.category,
        description: this.description
      });
    }
  }
}
