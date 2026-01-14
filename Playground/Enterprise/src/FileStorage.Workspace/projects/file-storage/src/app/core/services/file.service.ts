import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { BehaviorSubject, Observable, tap, map, catchError, of, finalize, shareReplay } from 'rxjs';
import { environment } from '../environment';
import { FileVersion, StoredFile, UploadResult } from '../models/stored-file.model';

interface FileState {
  files: StoredFile[];
  loading: boolean;
  error: string | null;
}

const initialState: FileState = {
  files: [],
  loading: false,
  error: null
};

@Injectable({ providedIn: 'root' })
export class FileService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiBaseUrl;
  private readonly defaultBucket = environment.defaultBucket;

  private readonly state$ = new BehaviorSubject<FileState>(initialState);

  readonly files$ = this.state$.pipe(map(state => state.files));
  readonly loading$ = this.state$.pipe(map(state => state.loading));
  readonly error$ = this.state$.pipe(map(state => state.error));

  loadFiles(bucket: string = this.defaultBucket, prefix?: string): Observable<StoredFile[]> {
    this.updateState({ loading: true, error: null });

    let params = new HttpParams();
    if (prefix) {
      params = params.set('prefix', prefix);
    }

    return this.http.get<StoredFile[]>(`${this.baseUrl}/files/${bucket}`, { params }).pipe(
      tap(files => this.updateState({ files, loading: false })),
      catchError(error => {
        this.updateState({ loading: false, error: error.message || 'Failed to load files' });
        return of([]);
      }),
      shareReplay(1)
    );
  }

  uploadFile(file: File, key: string, bucket: string = this.defaultBucket): Observable<UploadResult | null> {
    this.updateState({ loading: true, error: null });

    return this.http.post<UploadResult>(
      `${this.baseUrl}/files/${bucket}/${key}`,
      file,
      {
        headers: {
          'Content-Type': file.type || 'application/octet-stream'
        }
      }
    ).pipe(
      tap(result => {
        const currentFiles = this.state$.value.files;
        const existingIndex = currentFiles.findIndex(f => f.key === result.key);

        if (existingIndex >= 0) {
          const updatedFiles = [...currentFiles];
          updatedFiles[existingIndex] = result;
          this.updateState({ files: updatedFiles, loading: false });
        } else {
          this.updateState({ files: [...currentFiles, result], loading: false });
        }
      }),
      catchError(error => {
        this.updateState({ loading: false, error: error.message || 'Failed to upload file' });
        return of(null);
      })
    );
  }

  downloadFile(key: string, bucket: string = this.defaultBucket, versionId?: string): Observable<Blob | null> {
    let params = new HttpParams();
    if (versionId) {
      params = params.set('versionId', versionId);
    }

    return this.http.get(`${this.baseUrl}/files/${bucket}/${key}`, {
      params,
      responseType: 'blob'
    }).pipe(
      catchError(error => {
        this.updateState({ error: error.message || 'Failed to download file' });
        return of(null);
      })
    );
  }

  deleteFile(key: string, bucket: string = this.defaultBucket, versionId?: string): Observable<boolean> {
    this.updateState({ loading: true, error: null });

    let params = new HttpParams();
    if (versionId) {
      params = params.set('versionId', versionId);
    }

    return this.http.delete(`${this.baseUrl}/files/${bucket}/${key}`, { params }).pipe(
      map(() => {
        const currentFiles = this.state$.value.files;
        const updatedFiles = currentFiles.filter(f => f.key !== key);
        this.updateState({ files: updatedFiles, loading: false });
        return true;
      }),
      catchError(error => {
        this.updateState({ loading: false, error: error.message || 'Failed to delete file' });
        return of(false);
      })
    );
  }

  getFileVersions(key: string, bucket: string = this.defaultBucket): Observable<FileVersion[]> {
    return this.http.get<FileVersion[]>(`${this.baseUrl}/files/versions/${bucket}/${key}`).pipe(
      catchError(error => {
        this.updateState({ error: error.message || 'Failed to load file versions' });
        return of([]);
      })
    );
  }

  private updateState(partial: Partial<FileState>): void {
    this.state$.next({ ...this.state$.value, ...partial });
  }

  clearError(): void {
    this.updateState({ error: null });
  }
}
