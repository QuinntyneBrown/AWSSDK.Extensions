import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { BehaviorSubject, combineLatest, map } from 'rxjs';
import { TelemetryService } from '../services/telemetry.service';
import { DashboardPanel, TelemetryPoint } from '../models';
import { TelemetryPanel } from '../telemetry-panel/telemetry-panel';
import { AddPanelDialog } from '../add-panel-dialog/add-panel-dialog';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatDialogModule, TelemetryPanel],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss'
})
export class Dashboard implements OnInit {
  private panelsSubject = new BehaviorSubject<DashboardPanel[]>([]);
  private pointsSubject = new BehaviorSubject<TelemetryPoint[]>([]);

  panels$ = this.panelsSubject.asObservable();
  points$ = this.pointsSubject.asObservable();

  panelsWithData$ = combineLatest([this.panels$, this.points$]).pipe(
    map(([panels, points]) => panels.map(panel => ({
      panel,
      point: points.find(p => p.id === panel.telemetryPointId)
    })))
  );

  constructor(
    private telemetryService: TelemetryService,
    private dialog: MatDialog
  ) {}

  ngOnInit(): void {
    this.loadData();
  }

  private loadData(): void {
    this.telemetryService.getPanels().subscribe(panels => this.panelsSubject.next(panels));
    this.telemetryService.getPoints().subscribe(points => this.pointsSubject.next(points));
  }

  openAddPanelDialog(): void {
    const dialogRef = this.dialog.open(AddPanelDialog, {
      width: '500px',
      data: { points: this.pointsSubject.value }
    });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.telemetryService.createPanel(result).subscribe(() => this.loadData());
      }
    });
  }

  deletePanel(panelId: string): void {
    this.telemetryService.deletePanel(panelId).subscribe(() => this.loadData());
  }
}
