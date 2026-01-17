import { Component } from '@angular/core';
import { Header } from '../header/header';
import { BookmarksList } from '../bookmarks-list/bookmarks-list';

@Component({
  selector: 'app-main-layout',
  standalone: true,
  imports: [Header, BookmarksList],
  templateUrl: './main-layout.html',
  styleUrl: './main-layout.scss'
})
export class MainLayout {}
