namespace RiqMenu.Online
{
    /// <summary>
    /// Represents a song from the Riqs & Mods online database
    /// </summary>
    public class OnlineSong
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Creator { get; set; }
        public string Description { get; set; }
        public string Filename { get; set; }
        public string FileType { get; set; }
        public int FileSize { get; set; }
        public string FileHash { get; set; }
        public float? Bpm { get; set; }
        public float? Duration { get; set; }
        public string Difficulty { get; set; }
        public string GameTypes { get; set; }
        public int DownloadCount { get; set; }
        public string UploaderName { get; set; }

        public string DisplayTitle => string.IsNullOrEmpty(Artist) ? Title : $"{Artist} - {Title}";
        public string FileSizeDisplay => FileSize < 1024 * 1024
            ? $"{FileSize / 1024}KB"
            : $"{FileSize / (1024 * 1024f):F1}MB";

        /// <summary>
        /// Get the download filename in format: Title - Creator.ext
        /// </summary>
        public string DownloadFileName
        {
            get
            {
                string creator = Creator ?? UploaderName ?? "Unknown";
                return $"{Title} - {creator}.{FileType ?? "riq"}";
            }
        }
    }
}
