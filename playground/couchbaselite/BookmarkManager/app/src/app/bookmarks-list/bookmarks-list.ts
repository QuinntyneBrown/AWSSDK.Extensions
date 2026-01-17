import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatListModule } from '@angular/material/list';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { BehaviorSubject } from 'rxjs';
import { BookmarkService } from '../services/bookmark.service';
import { Bookmark } from '../models';
import { BookmarkDialog } from '../bookmark-dialog/bookmark-dialog';

@Component({
  selector: 'app-bookmarks-list',
  standalone: true,
  imports: [
    CommonModule,
    MatListModule,
    MatButtonModule,
    MatIconModule,
    MatCardModule,
    MatChipsModule,
    MatSelectModule,
    MatFormFieldModule,
    MatDialogModule
  ],
  templateUrl: './bookmarks-list.html',
  styleUrl: './bookmarks-list.scss'
})
export class BookmarksList implements OnInit {
  private bookmarksSubject = new BehaviorSubject<Bookmark[]>([]);
  private categoriesSubject = new BehaviorSubject<string[]>([]);
  private selectedCategorySubject = new BehaviorSubject<string>('');

  bookmarks$ = this.bookmarksSubject.asObservable();
  categories$ = this.categoriesSubject.asObservable();
  selectedCategory$ = this.selectedCategorySubject.asObservable();

  constructor(
    private bookmarkService: BookmarkService,
    private dialog: MatDialog
  ) {}

  ngOnInit(): void {
    this.loadCategories();
    this.loadBookmarks();
  }

  private loadCategories(): void {
    this.bookmarkService.getCategories().subscribe(cats => this.categoriesSubject.next(cats));
  }

  private loadBookmarks(): void {
    const cat = this.selectedCategorySubject.value;
    this.bookmarkService.getAll(cat || undefined).subscribe(bm => this.bookmarksSubject.next(bm));
  }

  onFilterChange(category: string): void {
    this.selectedCategorySubject.next(category);
    this.loadBookmarks();
  }

  openAddDialog(): void {
    const dialogRef = this.dialog.open(BookmarkDialog, { width: '500px' });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.bookmarkService.create(result).subscribe(() => {
          this.loadBookmarks();
          this.loadCategories();
        });
      }
    });
  }

  openUrl(url: string): void {
    window.open(url, '_blank');
  }

  deleteBookmark(bookmark: Bookmark, event: Event): void {
    event.stopPropagation();
    this.bookmarkService.delete(bookmark.id).subscribe(() => {
      this.loadBookmarks();
      this.loadCategories();
    });
  }
}
