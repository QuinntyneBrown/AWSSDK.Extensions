export interface ConfigFile {
  id: string;
  name: string;
  fileType: string;
  description: string;
  environment: string;
  createdAt: string;
  modifiedAt: string;
}

export interface CreateConfigFileRequest {
  name: string;
  fileType: string;
  description: string;
  environment: string;
  content: string;
}
