# Notes - Mini File Manager

A simple file management application with an ASP.NET Core backend and Angular frontend. This application allows you to create, read, update, and delete text files within subdirectories through a web interface.

## Features

- **File Operations**: Create, read, edit, and delete text files
- **Folder Management**: Create and delete folders with subdirectories
- **Text Editor**: Simple browser-based text editor for file content
- **Breadcrumb Navigation**: Easy navigation through folder hierarchy
- **REST API**: Well-documented backend API for file operations

## Technology Stack

### Backend
- **ASP.NET Core 10.0** - Latest .NET framework
- **C# with explicit typing** - No `var` keyword, full type hints
- **RESTful API** - Clean endpoint design
- **File system management** - Secure access within predetermined folder

### Frontend
- **Angular 21** - Latest Angular framework
- **TypeScript with strict mode** - Full type safety
- **SCSS** - Styled with Sass
- **Bootstrap 5 & ng-bootstrap** - Responsive UI components
- **Reactive programming** - RxJS for async operations

## Project Structure

```
/Backend              # ASP.NET Core Web API
  /Controllers        # API controllers
  /Models            # Data transfer objects
  /Services          # Business logic services
  /Files             # File storage directory (created at runtime)
  
/Frontend            # Angular application
  /src
    /app
      /components    # UI components
      /models        # TypeScript interfaces
      /services      # API service layer
```

## Prerequisites

- **.NET SDK 10.0** or later
- **Node.js 20** or later
- **npm 10** or later

## Setup Instructions

### Backend Setup

1. Navigate to the Backend directory:
   ```bash
   cd Backend
   ```

2. Restore dependencies:
   ```bash
   dotnet restore
   ```

3. Build the project:
   ```bash
   dotnet build
   ```

4. Run the backend server:
   ```bash
   dotnet run
   ```

   The backend will start on `http://localhost:5000` (or `https://localhost:5001` for HTTPS).

### Frontend Setup

1. Navigate to the Frontend directory:
   ```bash
   cd Frontend
   ```

2. Install dependencies:
   ```bash
   npm install
   ```

3. Start the development server:
   ```bash
   npm start
   ```

   The frontend will start on `http://localhost:4200`.

## Configuration

### Backend Configuration

The backend stores files in a predetermined folder. By default, this is a `Files` folder in the backend application directory. You can configure this in `appsettings.json`:

```json
{
  "FileManager": {
    "RootPath": "Files"
  }
}
```

### Frontend Configuration

The frontend connects to the backend API at `http://localhost:5000` by default. This can be modified in `src/app/services/file-api.service.ts` if needed.

## API Endpoints

### Files

- **GET** `/api/files?path={path}` - List files and folders
- **GET** `/api/files/content?path={path}` - Read file content
- **POST** `/api/files` - Create or update a file
  ```json
  {
    "path": "example.txt",
    "content": "File content here"
  }
  ```
- **DELETE** `/api/files?path={path}` - Delete a file

### Folders

- **POST** `/api/folders` - Create a folder
  ```json
  {
    "path": "folder-name"
  }
  ```
- **DELETE** `/api/folders?path={path}` - Delete a folder

## Usage

1. Start both the backend and frontend servers
2. Open `http://localhost:4200` in your browser
3. Use the "New File" button to create text files
4. Use the "New Folder" button to create folders
5. Click on folders to navigate into them
6. Click on files to open them in the editor
7. Edit file content and click "Save" to persist changes
8. Use the breadcrumb navigation to move up the folder hierarchy
9. Use the "Delete" button to remove files or folders

## Development

### Code Style

- **Backend**: C# code uses explicit typing (no `var` keyword) with comprehensive XML documentation comments
- **Frontend**: TypeScript code uses explicit types with JSDoc comments for all methods and properties

### Building for Production

#### Backend
```bash
cd Backend
dotnet publish -c Release
```

#### Frontend
```bash
cd Frontend
npm run build
```

The production build will be in `Frontend/dist/Frontend`.

## Security Considerations

- The backend restricts file access to a predetermined root folder
- Path traversal attempts (using `..`) are blocked
- All file operations validate paths to prevent unauthorized access
- CORS is configured to only allow requests from the frontend origin

## License

See LICENSE file for details.
