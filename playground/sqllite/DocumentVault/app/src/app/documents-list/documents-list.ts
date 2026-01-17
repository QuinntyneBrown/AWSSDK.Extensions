import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { BehaviorSubject } from 'rxjs';
import { DocumentService } from '../services/document.service';
import { Document } from '../models';
import { DocumentDialog } from '../document-dialog/document-dialog';
import { TagDialog } from '../tag-dialog/tag-dialog';

@Component({
  selector: 'app-documents-list',
  standalone: true,
  imports: [
    CommonModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatCardModule,
    MatChipsModule,
    MatSelectModule,
    MatFormFieldModule,
    MatDialogModule
  ],
  templateUrl: './documents-list.html',
  styleUrl: './documents-list.scss'
})
export class DocumentsList implements OnInit {
  private documentsSubject = new BehaviorSubject<Document[]>([]);
  private tagsSubject = new BehaviorSubject<string[]>([]);
  private selectedTagSubject = new BehaviorSubject<string>('');

  documents$ = this.documentsSubject.asObservable();
  tags$ = this.tagsSubject.asObservable();
  selectedTag$ = this.selectedTagSubject.asObservable();

  displayedColumns = ['name', 'size', 'tags', 'uploadedAt', 'actions'];

  constructor(
    private documentService: DocumentService,
    private dialog: MatDialog
  ) {}

  ngOnInit(): void {
    this.loadTags();
    this.loadDocuments();
  }

  private loadTags(): void {
    this.documentService.getTags().subscribe(tags => this.tagsSubject.next(tags));
  }

  private loadDocuments(): void {
    const tag = this.selectedTagSubject.value;
    this.documentService.getAll(tag || undefined).subscribe(docs => this.documentsSubject.next(docs));
  }

  onFilterChange(tag: string): void {
    this.selectedTagSubject.next(tag);
    this.loadDocuments();
  }

  openUploadDialog(): void {
    const dialogRef = this.dialog.open(DocumentDialog, { width: '500px' });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.documentService.upload(result.file, result.tags).subscribe(() => {
          this.loadDocuments();
          this.loadTags();
        });
      }
    });
  }

  openTagDialog(document: Document): void {
    const dialogRef = this.dialog.open(TagDialog, {
      width: '400px',
      data: { tags: document.tags }
    });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.documentService.updateTags(document.id, result).subscribe(() => {
          this.loadDocuments();
          this.loadTags();
        });
      }
    });
  }

  downloadDocument(document: Document): void {
    window.open(this.documentService.getDownloadUrl(document.id), '_blank');
  }

  deleteDocument(document: Document): void {
    this.documentService.delete(document.id).subscribe(() => {
      this.loadDocuments();
      this.loadTags();
    });
  }

  formatSize(bytes: number): string {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
    return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
  }
}
