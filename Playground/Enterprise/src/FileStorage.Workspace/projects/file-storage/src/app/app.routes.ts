import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./features/file-list/file-list').then(m => m.FileList)
  },
  {
    path: '**',
    redirectTo: ''
  }
];
