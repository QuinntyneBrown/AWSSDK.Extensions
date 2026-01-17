export interface TodoItem {
  id: string;
  title: string;
  description: string;
  isCompleted: boolean;
  createdAt: string;
  completedAt?: string;
}

export interface CreateTodoItemRequest {
  title: string;
  description: string;
}

export interface UpdateTodoItemRequest {
  title: string;
  description: string;
  isCompleted: boolean;
}
