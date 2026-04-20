import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { FileItem } from '../models/file-item.model';

/**
 * Service for communicating with the backend file management API.
 */
@Injectable({
  providedIn: 'root'
})
export class FileApiService {
  private readonly apiUrl: string = 'http://localhost:5000/api';

  constructor(private readonly http: HttpClient) { }

  /**
   * Lists files and folders in the specified path.
   * @param path - Relative path to list contents of.
   * @returns Observable of file items.
   */
  listItems(path: string = ''): Observable<FileItem[]> {
    const params: HttpParams = new HttpParams().set('path', path);
    return this.http.get<FileItem[]>(`${this.apiUrl}/files`, { params });
  }

  /**
   * Reads the content of a file.
   * @param path - Relative path to the file.
   * @returns Observable of file content.
   */
  readFile(path: string): Observable<{ content: string }> {
    const params: HttpParams = new HttpParams().set('path', path);
    return this.http.get<{ content: string }>(`${this.apiUrl}/files/content`, { params });
  }

  /**
   * Creates or updates a file with the specified content.
   * @param path - Relative path to the file.
   * @param content - Content to write to the file.
   * @returns Observable of response.
   */
  writeFile(path: string, content: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/files`, { path, content });
  }

  /**
   * Deletes a file.
   * @param path - Relative path to the file.
   * @returns Observable of response.
   */
  deleteFile(path: string): Observable<{ message: string }> {
    const params: HttpParams = new HttpParams().set('path', path);
    return this.http.delete<{ message: string }>(`${this.apiUrl}/files`, { params });
  }

  /**
   * Creates a new folder.
   * @param path - Relative path to the folder.
   * @returns Observable of response.
   */
  createFolder(path: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/folders`, { path });
  }

  /**
   * Deletes a folder.
   * @param path - Relative path to the folder.
   * @returns Observable of response.
   */
  deleteFolder(path: string): Observable<{ message: string }> {
    const params: HttpParams = new HttpParams().set('path', path);
    return this.http.delete<{ message: string }>(`${this.apiUrl}/folders`, { params });
  }
}
