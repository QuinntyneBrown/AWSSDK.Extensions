import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

interface DialogData {
  fileName: string;
}

@Component({
  selector: 'app-delete-confirm-dialog',
  imports: [
    CommonModule,
    MatDialogModule,
    MatButtonModule,
    MatIconModule
  ],
  templateUrl: './delete-confirm-dialog.html',
  styleUrl: './delete-confirm-dialog.scss'
})
export class DeleteConfirmDialog {
  private readonly dialogRef = inject(MatDialogRef<DeleteConfirmDialog>);
  private readonly data = inject<DialogData>(MAT_DIALOG_DATA);

  readonly fileName = this.data.fileName;

  confirm(): void {
    this.dialogRef.close(true);
  }

  cancel(): void {
    this.dialogRef.close(false);
  }
}
