import { Component, inject, OnInit } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatChipsModule } from '@angular/material/chips';
import { MatSnackBar } from '@angular/material/snack-bar';
import { BehaviorSubject, filter, Observable, take } from 'rxjs';
import { FileService } from '../../core/services/file.service';
import { FileVersion, StoredFile } from '../../core/models/stored-file.model';
import { FileSizePipe } from '../../shared/pipes/file-size.pipe';

interface DialogData {
  file: StoredFile;
}

@Component({
  selector: 'app-file-versions-dialog',
  imports: [
    CommonModule,
    DatePipe,
    MatDialogModule,
    MatButtonModule,
    MatIconModule,
    MatListModule,
    MatProgressSpinnerModule,
    MatChipsModule,
    FileSizePipe
  ],
  templateUrl: './file-versions-dialog.html',
  styleUrl: './file-versions-dialog.scss'
})
export class FileVersionsDialog implements OnInit {
  private readonly fileService = inject(FileService);
  private readonly dialogRef = inject(MatDialogRef<FileVersionsDialog>);
  private readonly data = inject<DialogData>(MAT_DIALOG_DATA);
  private readonly snackBar = inject(MatSnackBar);

  private readonly versions$ = new BehaviorSubject<FileVersion[]>([]);
  private readonly loading$ = new BehaviorSubject<boolean>(true);

  readonly fileVersions$: Observable<FileVersion[]> = this.versions$.asObservable();
  readonly isLoading$: Observable<boolean> = this.loading$.asObservable();
  readonly file = this.data.file;

  ngOnInit(): void {
    this.loadVersions();
  }

  private loadVersions(): void {
    this.loading$.next(true);
    this.fileService.getFileVersions(this.file.key).pipe(
      take(1)
    ).subscribe({
      next: versions => {
        this.versions$.next(versions);
        this.loading$.next(false);
      },
      error: () => {
        this.loading$.next(false);
      }
    });
  }

  downloadVersion(version: FileVersion): void {
    this.fileService.downloadFile(this.file.key, undefined, version.versionId).pipe(
      filter((blob): blob is Blob => blob !== null),
      take(1)
    ).subscribe(blob => {
      const url = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = this.getFileName(version.key);
      link.click();
      window.URL.revokeObjectURL(url);
      this.snackBar.open('Version downloaded', 'Close', { duration: 3000 });
    });
  }

  getFileName(key: string): string {
    const parts = key.split('/');
    return parts[parts.length - 1];
  }

  close(): void {
    this.dialogRef.close();
  }

  trackByVersionId(_index: number, version: FileVersion): string {
    return version.versionId;
  }
}
