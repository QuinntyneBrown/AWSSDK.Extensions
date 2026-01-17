import { Routes } from '@angular/router';
import { SessionsList } from './sessions-list/sessions-list';
import { SessionDetail } from './session-detail/session-detail';

export const routes: Routes = [
  { path: '', component: SessionsList },
  { path: 'session/:id', component: SessionDetail }
];
