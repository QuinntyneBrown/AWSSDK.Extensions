import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogRef, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { TelemetryPoint } from '../models';

export interface AddPanelDialogData {
  points: TelemetryPoint[];
}

@Component({
  selector: 'app-add-panel-dialog',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule
  ],
  templateUrl: './add-panel-dialog.html',
  styleUrl: './add-panel-dialog.scss'
})
export class AddPanelDialog {
  title = '';
  telemetryPointId = '';
  displayType = 'gauge';

  displayTypes = ['gauge', 'bar', 'value'];

  constructor(
    public dialogRef: MatDialogRef<AddPanelDialog>,
    @Inject(MAT_DIALOG_DATA) public data: AddPanelDialogData
  ) {}

  onCancel(): void {
    this.dialogRef.close();
  }

  onCreate(): void {
    if (this.title.trim() && this.telemetryPointId) {
      this.dialogRef.close({
        title: this.title,
        telemetryPointId: this.telemetryPointId,
        displayType: this.displayType
      });
    }
  }
}
