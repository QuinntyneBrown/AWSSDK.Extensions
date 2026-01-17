import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialogRef, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { ConfigFile } from '../models';

export interface LoadDialogData {
  config: ConfigFile;
  content: string;
}

@Component({
  selector: 'app-load-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule, MatButtonModule],
  templateUrl: './load-dialog.html',
  styleUrl: './load-dialog.scss'
})
export class LoadDialog {
  constructor(
    public dialogRef: MatDialogRef<LoadDialog>,
    @Inject(MAT_DIALOG_DATA) public data: LoadDialogData
  ) {}

  onClose(): void {
    this.dialogRef.close();
  }
}
