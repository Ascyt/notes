namespace Notes.Core.Directories
{
    public record struct DirectoryListingDto
    {
        public required string VirtualPath { get; set; }
        public required string PhysicalPath { get; set; }
        public required List<FileItemDto> Files { get; set; }
    }

    public record struct FileItemDto
    {
        public required string Name { get; set; }
        public required long SizeBytes { get; set; }
        public required DateTime LastModified { get; set; }
        public required bool IsDirectory { get; set; }
    }
}