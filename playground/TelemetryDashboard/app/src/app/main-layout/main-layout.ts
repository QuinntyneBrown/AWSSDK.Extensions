import { Component } from '@angular/core';
import { Header } from '../header/header';
import { Dashboard } from '../dashboard/dashboard';

@Component({
  selector: 'app-main-layout',
  standalone: true,
  imports: [Header, Dashboard],
  templateUrl: './main-layout.html',
  styleUrl: './main-layout.scss'
})
export class MainLayout {}
