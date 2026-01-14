import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatDialogRef, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';

@Component({
  selector: 'app-event-dialog',
  standalone: true,
  imports: [
    FormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule
  ],
  templateUrl: './event-dialog.html',
  styleUrl: './event-dialog.scss'
})
export class EventDialog {
  message = '';
  level = 'info';
  source = '';

  levels = ['info', 'warning', 'error', 'debug'];

  constructor(public dialogRef: MatDialogRef<EventDialog>) {}

  onCancel(): void {
    this.dialogRef.close();
  }

  onSave(): void {
    if (this.message.trim()) {
      this.dialogRef.close({
        message: this.message,
        level: this.level,
        source: this.source,
        metadata: {}
      });
    }
  }
}
