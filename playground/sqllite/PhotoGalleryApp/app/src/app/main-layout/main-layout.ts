import { Component } from '@angular/core';
import { Header } from '../header/header';
import { Gallery } from '../gallery/gallery';

@Component({
  selector: 'app-main-layout',
  standalone: true,
  imports: [Header, Gallery],
  templateUrl: './main-layout.html',
  styleUrl: './main-layout.scss'
})
export class MainLayout {}
