import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { FileApiService } from '../../services/file-api.service';
import { FileItem } from '../../models/file-item.model';

/**
 * Component for browsing files and folders.
 */
@Component({
  selector: 'app-file-browser',
  imports: [CommonModule, FormsModule],
  templateUrl: './file-browser.html',
  styleUrl: './file-browser.scss',
})
export class FileBrowser implements OnInit {
  items: FileItem[] = [];
  currentPath: string = '';
  loading: boolean = false;
  error: string | null = null;
  newFileName: string = '';
  newFolderName: string = '';
  showNewFileInput: boolean = false;
  showNewFolderInput: boolean = false;

  constructor(
    private readonly fileApiService: FileApiService,
    private readonly router: Router
  ) { }

  ngOnInit(): void {
    this.loadItems();
  }

  /**
   * Loads items in the current path.
   */
  loadItems(): void {
    this.loading = true;
    this.error = null;

    this.fileApiService.listItems(this.currentPath).subscribe({
      next: (items: FileItem[]) => {
        this.items = items;
        this.loading = false;
      },
      error: (err: Error) => {
        this.error = 'Failed to load items: ' + err.message;
        this.loading = false;
      }
    });
  }

  /**
   * Gets the path segments for breadcrumb navigation.
   */
  get pathSegments(): string[] {
    if (!this.currentPath) {
      return [];
    }
    return this.currentPath.split('/').filter((segment: string) => segment.length > 0);
  }

  /**
   * Navigates to the specified path.
   */
  navigateToPath(index: number): void {
    if (index === -1) {
      this.currentPath = '';
    } else {
      this.currentPath = this.pathSegments.slice(0, index + 1).join('/');
    }
    this.loadItems();
  }

  /**
   * Opens a folder.
   */
  openFolder(item: FileItem): void {
    this.currentPath = item.path;
    this.loadItems();
  }

  /**
   * Opens a file in the editor.
   */
  openFile(item: FileItem): void {
    this.router.navigate(['/editor'], { queryParams: { path: item.path } });
  }

  /**
   * Shows the new file input.
   */
  showNewFile(): void {
    this.showNewFileInput = true;
    this.showNewFolderInput = false;
    this.newFileName = '';
  }

  /**
   * Creates a new file.
   */
  createFile(): void {
    if (!this.newFileName.trim()) {
      return;
    }

    const filePath: string = this.currentPath 
      ? `${this.currentPath}/${this.newFileName}` 
      : this.newFileName;

    this.fileApiService.writeFile(filePath, '').subscribe({
      next: () => {
        this.showNewFileInput = false;
        this.newFileName = '';
        this.loadItems();
      },
      error: (err: Error) => {
        this.error = 'Failed to create file: ' + err.message;
      }
    });
  }

  /**
   * Shows the new folder input.
   */
  showNewFolder(): void {
    this.showNewFolderInput = true;
    this.showNewFileInput = false;
    this.newFolderName = '';
  }

  /**
   * Creates a new folder.
   */
  createFolder(): void {
    if (!this.newFolderName.trim()) {
      return;
    }

    const folderPath: string = this.currentPath 
      ? `${this.currentPath}/${this.newFolderName}` 
      : this.newFolderName;

    this.fileApiService.createFolder(folderPath).subscribe({
      next: () => {
        this.showNewFolderInput = false;
        this.newFolderName = '';
        this.loadItems();
      },
      error: (err: Error) => {
        this.error = 'Failed to create folder: ' + err.message;
      }
    });
  }

  /**
   * Deletes an item (file or folder).
   */
  deleteItem(item: FileItem): void {
    if (!confirm(`Are you sure you want to delete ${item.name}?`)) {
      return;
    }

    const deleteObservable = item.isDirectory 
      ? this.fileApiService.deleteFolder(item.path) 
      : this.fileApiService.deleteFile(item.path);

    deleteObservable.subscribe({
      next: () => {
        this.loadItems();
      },
      error: (err: Error) => {
        this.error = `Failed to delete ${item.name}: ` + err.message;
      }
    });
  }

  /**
   * Cancels input forms.
   */
  cancelInput(): void {
    this.showNewFileInput = false;
    this.showNewFolderInput = false;
    this.newFileName = '';
    this.newFolderName = '';
  }

  /**
   * Formats file size for display.
   */
  formatSize(size: number | null): string {
    if (size === null) {
      return '-';
    }
    if (size < 1024) {
      return `${size} B`;
    }
    if (size < 1024 * 1024) {
      return `${(size / 1024).toFixed(2)} KB`;
    }
    return `${(size / (1024 * 1024)).toFixed(2)} MB`;
  }
}

