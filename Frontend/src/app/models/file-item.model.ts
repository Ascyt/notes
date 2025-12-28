/**
 * Represents a file or folder item.
 */
export interface FileItem {
  /**
   * Name of the file or folder.
   */
  name: string;

  /**
   * Relative path of the file or folder.
   */
  path: string;

  /**
   * Indicates whether this item is a directory.
   */
  isDirectory: boolean;

  /**
   * Size of the file in bytes (null for directories).
   */
  size: number | null;

  /**
   * Last modified date and time.
   */
  lastModified: Date;
}
