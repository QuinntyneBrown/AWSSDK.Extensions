import { Component, inject, OnInit } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatListModule, MatSelectionListChange } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { BehaviorSubject, combineLatest, map, Observable, take } from 'rxjs';
import { FileService } from '../../core/services/file.service';
import { StoredFile } from '../../core/models/stored-file.model';
import { FileSizePipe } from '../../shared/pipes/file-size.pipe';

@Component({
  selector: 'app-file-select-dialog',
  imports: [
    CommonModule,
    FormsModule,
    DatePipe,
    MatDialogModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatListModule,
    MatProgressSpinnerModule,
    FileSizePipe
  ],
  templateUrl: './file-select-dialog.html',
  styleUrl: './file-select-dialog.scss'
})
export class FileSelectDialog implements OnInit {
  private readonly fileService = inject(FileService);
  private readonly dialogRef = inject(MatDialogRef<FileSelectDialog>);

  private readonly searchTerm$ = new BehaviorSubject<string>('');
  private readonly selectedFile$ = new BehaviorSubject<StoredFile | null>(null);

  readonly loading$: Observable<boolean> = this.fileService.loading$;

  readonly filteredFiles$: Observable<StoredFile[]> = combineLatest([
    this.fileService.files$,
    this.searchTerm$
  ]).pipe(
    map(([files, term]) => {
      if (!term.trim()) {
        return files;
      }
      const lowerTerm = term.toLowerCase();
      return files.filter(file =>
        file.key.toLowerCase().includes(lowerTerm) ||
        (file.contentType?.toLowerCase().includes(lowerTerm) ?? false)
      );
    })
  );

  readonly hasSelection$ = this.selectedFile$.pipe(map(file => file !== null));
  searchValue = '';

  ngOnInit(): void {
    this.fileService.loadFiles().pipe(take(1)).subscribe();
  }

  onSearchChange(term: string): void {
    this.searchTerm$.next(term);
  }

  onSelectionChange(event: MatSelectionListChange): void {
    const selected = event.options[0];
    if (selected && selected.selected) {
      this.selectedFile$.next(selected.value as StoredFile);
    } else {
      this.selectedFile$.next(null);
    }
  }

  selectFile(): void {
    const file = this.selectedFile$.value;
    if (file) {
      this.dialogRef.close(file);
    }
  }

  cancel(): void {
    this.dialogRef.close(null);
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
