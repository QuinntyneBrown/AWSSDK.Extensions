export interface LogEvent {
  id: string;
  message: string;
  level: string;
  source: string;
  timestamp: string;
  metadata: Record<string, string>;
}

export interface CreateLogEventRequest {
  message: string;
  level: string;
  source: string;
  metadata: Record<string, string>;
}
