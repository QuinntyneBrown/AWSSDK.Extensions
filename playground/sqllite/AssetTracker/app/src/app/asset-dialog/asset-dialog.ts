import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogRef, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { Asset } from '../models';

export interface AssetDialogData {
  mode: 'create' | 'edit';
  asset?: Asset;
}

@Component({
  selector: 'app-asset-dialog',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatDatepickerModule,
    MatNativeDateModule
  ],
  templateUrl: './asset-dialog.html',
  styleUrl: './asset-dialog.scss'
})
export class AssetDialog {
  name: string;
  category: string;
  status: string;
  location: string;
  assignedTo: string;
  acquiredAt: Date;

  categories = ['Electronics', 'Furniture', 'Vehicles', 'Equipment', 'Software'];
  statuses = ['available', 'in-use', 'maintenance', 'retired'];

  constructor(
    public dialogRef: MatDialogRef<AssetDialog>,
    @Inject(MAT_DIALOG_DATA) public data: AssetDialogData
  ) {
    this.name = data.asset?.name ?? '';
    this.category = data.asset?.category ?? '';
    this.status = data.asset?.status ?? 'available';
    this.location = data.asset?.location ?? '';
    this.assignedTo = data.asset?.assignedTo ?? '';
    this.acquiredAt = data.asset?.acquiredAt ? new Date(data.asset.acquiredAt) : new Date();
  }

  onCancel(): void {
    this.dialogRef.close();
  }

  onSave(): void {
    if (this.data.mode === 'create') {
      if (this.name.trim()) {
        this.dialogRef.close({
          name: this.name,
          category: this.category,
          location: this.location,
          acquiredAt: this.acquiredAt.toISOString()
        });
      }
    } else {
      this.dialogRef.close({
        status: this.status,
        location: this.location,
        assignedTo: this.assignedTo
      });
    }
  }
}
