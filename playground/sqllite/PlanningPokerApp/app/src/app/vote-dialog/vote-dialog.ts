import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogRef, MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';

export interface VoteDialogData {
  cardValues: string[];
}

@Component({
  selector: 'app-vote-dialog',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatChipsModule
  ],
  templateUrl: './vote-dialog.html',
  styleUrl: './vote-dialog.scss'
})
export class VoteDialog {
  participantName = '';
  selectedValue = '';

  constructor(
    public dialogRef: MatDialogRef<VoteDialog>,
    @Inject(MAT_DIALOG_DATA) public data: VoteDialogData
  ) {}

  selectCard(value: string): void {
    this.selectedValue = value;
  }

  onCancel(): void {
    this.dialogRef.close();
  }

  onSubmit(): void {
    if (this.participantName.trim() && this.selectedValue) {
      this.dialogRef.close({
        participantName: this.participantName,
        value: this.selectedValue
      });
    }
  }
}
