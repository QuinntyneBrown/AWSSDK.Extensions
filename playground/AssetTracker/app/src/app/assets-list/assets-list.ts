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
import { AssetService } from '../services/asset.service';
import { Asset } from '../models';
import { AssetDialog } from '../asset-dialog/asset-dialog';

@Component({
  selector: 'app-assets-list',
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
  templateUrl: './assets-list.html',
  styleUrl: './assets-list.scss'
})
export class AssetsList implements OnInit {
  private assetsSubject = new BehaviorSubject<Asset[]>([]);
  private selectedStatusSubject = new BehaviorSubject<string>('');

  assets$ = this.assetsSubject.asObservable();
  selectedStatus$ = this.selectedStatusSubject.asObservable();

  displayedColumns = ['name', 'category', 'status', 'location', 'assignedTo', 'actions'];
  statuses = ['available', 'in-use', 'maintenance', 'retired'];

  constructor(
    private assetService: AssetService,
    private dialog: MatDialog
  ) {}

  ngOnInit(): void {
    this.loadAssets();
  }

  private loadAssets(): void {
    const status = this.selectedStatusSubject.value;
    this.assetService.getAll(status || undefined).subscribe(assets => this.assetsSubject.next(assets));
  }

  onFilterChange(status: string): void {
    this.selectedStatusSubject.next(status);
    this.loadAssets();
  }

  openAddDialog(): void {
    const dialogRef = this.dialog.open(AssetDialog, {
      width: '500px',
      data: { mode: 'create' }
    });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.assetService.create(result).subscribe(() => this.loadAssets());
      }
    });
  }

  openEditDialog(asset: Asset): void {
    const dialogRef = this.dialog.open(AssetDialog, {
      width: '500px',
      data: { mode: 'edit', asset }
    });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.assetService.update(asset.id, result).subscribe(() => this.loadAssets());
      }
    });
  }

  deleteAsset(asset: Asset): void {
    this.assetService.delete(asset.id).subscribe(() => this.loadAssets());
  }

  getStatusClass(status: string): string {
    return `assets-list__status--${status}`;
  }
}
