import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { DashboardPanel, TelemetryPoint } from '../models';

@Component({
  selector: 'app-telemetry-panel',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatButtonModule, MatIconModule, MatProgressBarModule],
  templateUrl: './telemetry-panel.html',
  styleUrl: './telemetry-panel.scss'
})
export class TelemetryPanel {
  @Input() panel!: DashboardPanel;
  @Input() point?: TelemetryPoint;
  @Output() delete = new EventEmitter<void>();

  get percentage(): number {
    if (!this.point) return 0;
    const range = this.point.maxValue - this.point.minValue;
    if (range === 0) return 0;
    return ((this.point.value - this.point.minValue) / range) * 100;
  }

  onDelete(): void {
    this.delete.emit();
  }
}
