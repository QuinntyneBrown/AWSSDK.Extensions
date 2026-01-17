import { Component } from '@angular/core';
import { Header } from '../header/header';
import { TodosList } from '../todos-list/todos-list';

@Component({
  selector: 'app-main-layout',
  standalone: true,
  imports: [Header, TodosList],
  templateUrl: './main-layout.html',
  styleUrl: './main-layout.scss'
})
export class MainLayout {}
