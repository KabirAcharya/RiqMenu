using UnityEngine;

namespace RiqMenu {
    public class CustomSong {
        public string Type;
        public string SongTitle;
        public string Title;
        public string Creator;
        public string riq;
        public AudioClip audioClip;
        
        public bool IsBopFile => !string.IsNullOrEmpty(riq) && riq.EndsWith(".bop", System.StringComparison.OrdinalIgnoreCase);
    }
}