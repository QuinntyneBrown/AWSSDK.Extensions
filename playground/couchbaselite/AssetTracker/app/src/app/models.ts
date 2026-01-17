export interface Asset {
  id: string;
  name: string;
  category: string;
  status: string;
  location: string;
  assignedTo: string;
  acquiredAt: string;
  createdAt: string;
}

export interface CreateAssetRequest {
  name: string;
  category: string;
  location: string;
  acquiredAt: string;
}

export interface UpdateAssetRequest {
  status: string;
  location: string;
  assignedTo: string;
}
