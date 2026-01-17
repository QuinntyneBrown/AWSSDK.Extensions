import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatListModule } from '@angular/material/list';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatCardModule } from '@angular/material/card';
import { BehaviorSubject } from 'rxjs';
import { TodoService } from '../services/todo.service';
import { TodoItem } from '../models';
import { TodoDialog } from '../todo-dialog/todo-dialog';

@Component({
  selector: 'app-todos-list',
  standalone: true,
  imports: [
    CommonModule,
    MatListModule,
    MatCheckboxModule,
    MatButtonModule,
    MatIconModule,
    MatDialogModule,
    MatCardModule
  ],
  templateUrl: './todos-list.html',
  styleUrl: './todos-list.scss'
})
export class TodosList implements OnInit {
  private todosSubject = new BehaviorSubject<TodoItem[]>([]);
  todos$ = this.todosSubject.asObservable();

  constructor(
    private todoService: TodoService,
    private dialog: MatDialog
  ) {}

  ngOnInit(): void {
    this.loadTodos();
  }

  private loadTodos(): void {
    this.todoService.getAll().subscribe(todos => this.todosSubject.next(todos));
  }

  openAddDialog(): void {
    const dialogRef = this.dialog.open(TodoDialog, {
      width: '400px',
      data: { mode: 'create' }
    });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.todoService.create(result).subscribe(() => this.loadTodos());
      }
    });
  }

  openEditDialog(todo: TodoItem): void {
    const dialogRef = this.dialog.open(TodoDialog, {
      width: '400px',
      data: { mode: 'edit', todo }
    });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.todoService.update(todo.id, result).subscribe(() => this.loadTodos());
      }
    });
  }

  toggleComplete(todo: TodoItem): void {
    this.todoService.update(todo.id, {
      title: todo.title,
      description: todo.description,
      isCompleted: !todo.isCompleted
    }).subscribe(() => this.loadTodos());
  }

  deleteTodo(todo: TodoItem): void {
    this.todoService.delete(todo.id).subscribe(() => this.loadTodos());
  }
}
