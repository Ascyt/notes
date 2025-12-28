import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { FileApiService } from '../../services/file-api.service';

/**
 * Component for editing text files.
 */
@Component({
  selector: 'app-file-editor',
  imports: [CommonModule, FormsModule],
  templateUrl: './file-editor.html',
  styleUrl: './file-editor.scss',
})
export class FileEditor implements OnInit {
  filePath: string = '';
  fileContent: string = '';
  originalContent: string = '';
  loading: boolean = false;
  saving: boolean = false;
  error: string | null = null;
  successMessage: string | null = null;

  constructor(
    private readonly fileApiService: FileApiService,
    private readonly route: ActivatedRoute,
    private readonly router: Router
  ) { }

  ngOnInit(): void {
    this.route.queryParams.subscribe(params => {
      this.filePath = params['path'] || '';
      if (this.filePath) {
        this.loadFile();
      }
    });
  }

  /**
   * Loads the file content.
   */
  loadFile(): void {
    this.loading = true;
    this.error = null;

    this.fileApiService.readFile(this.filePath).subscribe({
      next: (response: { content: string }) => {
        this.fileContent = response.content;
        this.originalContent = response.content;
        this.loading = false;
      },
      error: (err: Error) => {
        this.error = 'Failed to load file: ' + err.message;
        this.loading = false;
      }
    });
  }

  /**
   * Saves the file content.
   */
  saveFile(): void {
    this.saving = true;
    this.error = null;
    this.successMessage = null;

    this.fileApiService.writeFile(this.filePath, this.fileContent).subscribe({
      next: () => {
        this.originalContent = this.fileContent;
        this.successMessage = 'File saved successfully!';
        this.saving = false;
        setTimeout(() => {
          this.successMessage = null;
        }, 3000);
      },
      error: (err: Error) => {
        this.error = 'Failed to save file: ' + err.message;
        this.saving = false;
      }
    });
  }

  /**
   * Checks if the file has been modified.
   */
  get isModified(): boolean {
    return this.fileContent !== this.originalContent;
  }

  /**
   * Navigates back to the file browser.
   */
  goBack(): void {
    if (this.isModified) {
      if (!confirm('You have unsaved changes. Are you sure you want to leave?')) {
        return;
      }
    }
    this.router.navigate(['/']);
  }

  /**
   * Gets the file name from the path.
   */
  get fileName(): string {
    return this.filePath.split('/').pop() || '';
  }
}

