export interface Bookmark {
  id: string;
  title: string;
  url: string;
  category: string;
  description: string;
  createdAt: string;
}

export interface CreateBookmarkRequest {
  title: string;
  url: string;
  category: string;
  description: string;
}
