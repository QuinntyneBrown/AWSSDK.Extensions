import { Component } from '@angular/core';
import { Header } from '../header/header';
import { ConfigList } from '../config-list/config-list';

@Component({
  selector: 'app-main-layout',
  standalone: true,
  imports: [Header, ConfigList],
  templateUrl: './main-layout.html',
  styleUrl: './main-layout.scss'
})
export class MainLayout {}
