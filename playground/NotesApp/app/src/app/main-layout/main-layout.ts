import { Component } from '@angular/core';
import { Header } from '../header/header';
import { NotesList } from '../notes-list/notes-list';

@Component({
  selector: 'app-main-layout',
  standalone: true,
  imports: [Header, NotesList],
  templateUrl: './main-layout.html',
  styleUrl: './main-layout.scss'
})
export class MainLayout {}
