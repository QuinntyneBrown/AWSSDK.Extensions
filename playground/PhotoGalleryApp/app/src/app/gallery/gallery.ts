import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatGridListModule } from '@angular/material/grid-list';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { BehaviorSubject } from 'rxjs';
import { PhotoService } from '../services/photo.service';
import { Photo } from '../models';
import { UploadDialog } from '../upload-dialog/upload-dialog';
import { PhotoDialog } from '../photo-dialog/photo-dialog';

@Component({
  selector: 'app-gallery',
  standalone: true,
  imports: [
    CommonModule,
    MatGridListModule,
    MatButtonModule,
    MatIconModule,
    MatCardModule,
    MatDialogModule
  ],
  templateUrl: './gallery.html',
  styleUrl: './gallery.scss'
})
export class Gallery implements OnInit {
  private photosSubject = new BehaviorSubject<Photo[]>([]);
  photos$ = this.photosSubject.asObservable();

  constructor(
    private photoService: PhotoService,
    private dialog: MatDialog
  ) {}

  ngOnInit(): void {
    this.loadPhotos();
  }

  private loadPhotos(): void {
    this.photoService.getAll().subscribe(photos => this.photosSubject.next(photos));
  }

  getPhotoUrl(id: string): string {
    return this.photoService.getPhotoUrl(id);
  }

  openUploadDialog(): void {
    const dialogRef = this.dialog.open(UploadDialog, { width: '400px' });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.photoService.upload(result).subscribe(() => this.loadPhotos());
      }
    });
  }

  openPhotoDialog(photo: Photo): void {
    this.dialog.open(PhotoDialog, {
      data: { photo, photoUrl: this.getPhotoUrl(photo.id) },
      maxWidth: '90vw',
      maxHeight: '90vh'
    });
  }

  deletePhoto(photo: Photo, event: Event): void {
    event.stopPropagation();
    this.photoService.delete(photo.id).subscribe(() => this.loadPhotos());
  }
}
