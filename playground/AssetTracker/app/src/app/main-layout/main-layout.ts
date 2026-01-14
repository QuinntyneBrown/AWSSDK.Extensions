import { Component } from '@angular/core';
import { Header } from '../header/header';
import { AssetsList } from '../assets-list/assets-list';

@Component({
  selector: 'app-main-layout',
  standalone: true,
  imports: [Header, AssetsList],
  templateUrl: './main-layout.html',
  styleUrl: './main-layout.scss'
})
export class MainLayout {}
