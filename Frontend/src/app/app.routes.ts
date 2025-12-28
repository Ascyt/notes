import { Routes } from '@angular/router';
import { FileBrowser } from './components/file-browser/file-browser';
import { FileEditor } from './components/file-editor/file-editor';

export const routes: Routes = [
  { path: '', component: FileBrowser },
  { path: 'editor', component: FileEditor },
  { path: '**', redirectTo: '' }
];
