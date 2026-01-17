import { Component } from '@angular/core';
import { Header } from '../header/header';
import { DocumentsList } from '../documents-list/documents-list';

@Component({
  selector: 'app-main-layout',
  standalone: true,
  imports: [Header, DocumentsList],
  templateUrl: './main-layout.html',
  styleUrl: './main-layout.scss'
})
export class MainLayout {}
