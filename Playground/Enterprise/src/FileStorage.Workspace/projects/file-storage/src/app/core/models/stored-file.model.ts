export interface StoredFile {
  bucketName: string;
  key: string;
  versionId?: string;
  eTag?: string;
  size: number;
  contentType?: string;
  lastModified: string;
}

export interface FileVersion {
  versionId: string;
  key: string;
  isLatest: boolean;
  lastModified: string;
  size: number;
  isDeleteMarker: boolean;
}

export interface UploadResult {
  bucketName: string;
  key: string;
  versionId?: string;
  eTag?: string;
  size: number;
  contentType?: string;
  lastModified: string;
}
