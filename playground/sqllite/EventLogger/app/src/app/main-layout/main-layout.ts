import { Component } from '@angular/core';
import { Header } from '../header/header';
import { EventsList } from '../events-list/events-list';

@Component({
  selector: 'app-main-layout',
  standalone: true,
  imports: [Header, EventsList],
  templateUrl: './main-layout.html',
  styleUrl: './main-layout.scss'
})
export class MainLayout {}
