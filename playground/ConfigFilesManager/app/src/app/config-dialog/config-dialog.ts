import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatDialogRef, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';

@Component({
  selector: 'app-config-dialog',
  standalone: true,
  imports: [
    FormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatSelectModule
  ],
  templateUrl: './config-dialog.html',
  styleUrl: './config-dialog.scss'
})
export class ConfigDialog {
  name = '';
  fileType = '';
  description = '';
  environment = '';
  content = '';

  fileTypes = ['json', 'yaml', 'xml', 'ini', 'toml', 'env'];
  environments = ['development', 'staging', 'production'];

  constructor(public dialogRef: MatDialogRef<ConfigDialog>) {}

  onCancel(): void {
    this.dialogRef.close();
  }

  onCreate(): void {
    if (this.name.trim() && this.fileType) {
      this.dialogRef.close({
        name: this.name,
        fileType: this.fileType,
        description: this.description,
        environment: this.environment,
        content: this.content
      });
    }
  }
}
