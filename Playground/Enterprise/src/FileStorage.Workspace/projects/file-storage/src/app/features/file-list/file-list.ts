import { Component, inject, OnInit } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatMenuModule } from '@angular/material/menu';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Observable, switchMap, filter, take } from 'rxjs';
import { FileService } from '../../core/services/file.service';
import { StoredFile } from '../../core/models/stored-file.model';
import { FileUploadDialog } from '../file-upload-dialog/file-upload-dialog';
import { FileVersionsDialog } from '../file-versions-dialog/file-versions-dialog';
import { DeleteConfirmDialog } from '../delete-confirm-dialog/delete-confirm-dialog';
import { FileSizePipe } from '../../shared/pipes/file-size.pipe';

@Component({
  selector: 'app-file-list',
  imports: [
    CommonModule,
    DatePipe,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatToolbarModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    MatMenuModule,
    FileSizePipe
  ],
  templateUrl: './file-list.html',
  styleUrl: './file-list.scss'
})
export class FileList implements OnInit {
  private readonly fileService = inject(FileService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly files$: Observable<StoredFile[]> = this.fileService.files$;
  readonly loading$: Observable<boolean> = this.fileService.loading$;
  readonly error$: Observable<string | null> = this.fileService.error$;

  readonly displayedColumns = ['icon', 'name', 'size', 'type', 'modified', 'version', 'actions'];

  ngOnInit(): void {
    this.fileService.loadFiles().pipe(take(1)).subscribe();
  }

  openUploadDialog(): void {
    const dialogRef = this.dialog.open(FileUploadDialog, {
      width: '100%',
      maxWidth: '500px',
      panelClass: 'file-upload-dialog'
    });

    dialogRef.afterClosed().pipe(
      filter(result => result === true),
      switchMap(() => this.fileService.loadFiles()),
      take(1)
    ).subscribe(() => {
      this.snackBar.open('File uploaded successfully', 'Close', { duration: 3000 });
    });
  }

  openVersionsDialog(file: StoredFile): void {
    this.dialog.open(FileVersionsDialog, {
      width: '100%',
      maxWidth: '600px',
      data: { file },
      panelClass: 'file-versions-dialog'
    });
  }

  downloadFile(file: StoredFile): void {
    this.fileService.downloadFile(file.key).pipe(
      filter((blob): blob is Blob => blob !== null),
      take(1)
    ).subscribe(blob => {
      const url = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = this.getFileName(file.key);
      link.click();
      window.URL.revokeObjectURL(url);
      this.snackBar.open('File downloaded', 'Close', { duration: 3000 });
    });
  }

  confirmDelete(file: StoredFile): void {
    const dialogRef = this.dialog.open(DeleteConfirmDialog, {
      width: '100%',
      maxWidth: '400px',
      data: { fileName: this.getFileName(file.key) },
      panelClass: 'delete-confirm-dialog'
    });

    dialogRef.afterClosed().pipe(
      filter(result => result === true),
      switchMap(() => this.fileService.deleteFile(file.key)),
      filter(success => success),
      take(1)
    ).subscribe(() => {
      this.snackBar.open('File deleted', 'Close', { duration: 3000 });
    });
  }

  refreshFiles(): void {
    this.fileService.loadFiles().pipe(take(1)).subscribe();
  }

  getFileName(key: string): string {
    const parts = key.split('/');
    return parts[parts.length - 1];
  }

  getFileIcon(contentType?: string): string {
    if (!contentType) return 'insert_drive_file';

    if (contentType.startsWith('image/')) return 'image';
    if (contentType.startsWith('video/')) return 'movie';
    if (contentType.startsWith('audio/')) return 'audio_file';
    if (contentType.includes('pdf')) return 'picture_as_pdf';
    if (contentType.includes('zip') || contentType.includes('compressed')) return 'folder_zip';
    if (contentType.includes('json') || contentType.includes('javascript')) return 'code';
    if (contentType.includes('text')) return 'description';

    return 'insert_drive_file';
  }

  trackByKey(_index: number, file: StoredFile): string {
    return file.key;
  }
}
