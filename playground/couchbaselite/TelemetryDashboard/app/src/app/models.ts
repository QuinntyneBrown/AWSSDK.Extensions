export interface TelemetryPoint {
  id: string;
  name: string;
  unit: string;
  category: string;
  value: number;
  minValue: number;
  maxValue: number;
  timestamp: string;
}

export interface DashboardPanel {
  id: string;
  title: string;
  telemetryPointId: string;
  displayType: string;
  position: number;
  createdAt: string;
}

export interface CreateTelemetryPointRequest {
  name: string;
  unit: string;
  category: string;
  minValue: number;
  maxValue: number;
}

export interface CreatePanelRequest {
  title: string;
  telemetryPointId: string;
  displayType: string;
}
