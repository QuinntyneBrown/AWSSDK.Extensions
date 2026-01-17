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
import { BehaviorSubject, combineLatest } from 'rxjs';
import { ConfigService } from '../services/config.service';
import { ConfigFile } from '../models';
import { ConfigDialog } from '../config-dialog/config-dialog';
import { LoadDialog } from '../load-dialog/load-dialog';

@Component({
  selector: 'app-config-list',
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
  templateUrl: './config-list.html',
  styleUrl: './config-list.scss'
})
export class ConfigList implements OnInit {
  private configsSubject = new BehaviorSubject<ConfigFile[]>([]);
  private fileTypesSubject = new BehaviorSubject<string[]>([]);
  private selectedTypeSubject = new BehaviorSubject<string>('');

  configs$ = this.configsSubject.asObservable();
  fileTypes$ = this.fileTypesSubject.asObservable();
  selectedType$ = this.selectedTypeSubject.asObservable();

  displayedColumns = ['name', 'fileType', 'environment', 'modifiedAt', 'actions'];

  constructor(
    private configService: ConfigService,
    private dialog: MatDialog
  ) {}

  ngOnInit(): void {
    this.loadFileTypes();
    this.loadConfigs();
  }

  private loadFileTypes(): void {
    this.configService.getFileTypes().subscribe(types => this.fileTypesSubject.next(types));
  }

  private loadConfigs(): void {
    const type = this.selectedTypeSubject.value;
    this.configService.getAll(type || undefined).subscribe(configs => this.configsSubject.next(configs));
  }

  onFilterChange(type: string): void {
    this.selectedTypeSubject.next(type);
    this.loadConfigs();
  }

  openCreateDialog(): void {
    const dialogRef = this.dialog.open(ConfigDialog, { width: '600px' });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.configService.create(result).subscribe(() => {
          this.loadConfigs();
          this.loadFileTypes();
        });
      }
    });
  }

  openLoadDialog(config: ConfigFile): void {
    this.configService.getContent(config.id).subscribe(content => {
      this.dialog.open(LoadDialog, {
        width: '800px',
        data: { config, content }
      });
    });
  }

  deleteConfig(config: ConfigFile): void {
    this.configService.delete(config.id).subscribe(() => {
      this.loadConfigs();
      this.loadFileTypes();
    });
  }
}
