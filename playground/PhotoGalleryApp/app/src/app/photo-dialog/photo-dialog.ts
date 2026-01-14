import { Component, Inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { Photo } from '../models';

export interface PhotoDialogData {
  photo: Photo;
  photoUrl: string;
}

@Component({
  selector: 'app-photo-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule],
  templateUrl: './photo-dialog.html',
  styleUrl: './photo-dialog.scss'
})
export class PhotoDialog {
  constructor(
    public dialogRef: MatDialogRef<PhotoDialog>,
    @Inject(MAT_DIALOG_DATA) public data: PhotoDialogData
  ) {}

  onClose(): void {
    this.dialogRef.close();
  }
}
