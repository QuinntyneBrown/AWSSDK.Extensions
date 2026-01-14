import { Component, inject, ElementRef, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { BehaviorSubject, filter, take } from 'rxjs';
import { FileService } from '../../core/services/file.service';
import { FileSizePipe } from '../../shared/pipes/file-size.pipe';

@Component({
  selector: 'app-file-upload-dialog',
  imports: [
    CommonModule,
    FormsModule,
    MatDialogModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressBarModule,
    FileSizePipe
  ],
  templateUrl: './file-upload-dialog.html',
  styleUrl: './file-upload-dialog.scss'
})
export class FileUploadDialog {
  private readonly fileService = inject(FileService);
  private readonly dialogRef = inject(MatDialogRef<FileUploadDialog>);

  @ViewChild('fileInput') fileInput!: ElementRef<HTMLInputElement>;

  selectedFile: File | null = null;
  customKey = '';
  isDragOver = false;
  private readonly uploading$ = new BehaviorSubject<boolean>(false);
  readonly isUploading$ = this.uploading$.asObservable();

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver = true;
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver = false;
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver = false;

    const files = event.dataTransfer?.files;
    if (files && files.length > 0) {
      this.selectFile(files[0]);
    }
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.selectFile(input.files[0]);
    }
  }

  selectFile(file: File): void {
    this.selectedFile = file;
    this.customKey = file.name;
  }

  triggerFileInput(): void {
    this.fileInput.nativeElement.click();
  }

  clearSelection(): void {
    this.selectedFile = null;
    this.customKey = '';
    if (this.fileInput) {
      this.fileInput.nativeElement.value = '';
    }
  }

  upload(): void {
    if (!this.selectedFile || !this.customKey.trim()) {
      return;
    }

    this.uploading$.next(true);

    this.fileService.uploadFile(this.selectedFile, this.customKey.trim()).pipe(
      filter(result => result !== null),
      take(1)
    ).subscribe({
      next: () => {
        this.uploading$.next(false);
        this.dialogRef.close(true);
      },
      error: () => {
        this.uploading$.next(false);
      }
    });
  }

  cancel(): void {
    this.dialogRef.close(false);
  }
}
