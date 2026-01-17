export interface Note {
  id: string;
  title: string;
  content: string;
  color: string;
  createdAt: string;
  modifiedAt: string;
}

export interface CreateNoteRequest {
  title: string;
  content: string;
  color: string;
}
