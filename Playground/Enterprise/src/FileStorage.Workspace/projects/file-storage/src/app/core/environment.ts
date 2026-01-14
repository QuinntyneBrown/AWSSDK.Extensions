import { InjectionToken } from '@angular/core';

export interface Environment {
  apiBaseUrl: string;
  defaultBucket: string;
}

export const ENVIRONMENT = new InjectionToken<Environment>('Environment');

export const environment: Environment = {
  apiBaseUrl: 'http://localhost:5000/api',
  defaultBucket: 'default'
};
