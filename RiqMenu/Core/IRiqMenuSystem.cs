using UnityEngine;

namespace RiqMenu.Core
{
    /// <summary>
    /// Base interface for all RiqMenu system components
    /// </summary>
    public interface IRiqMenuSystem
    {
        /// <summary>
        /// Initialize the system component
        /// </summary>
        void Initialize();
        
        /// <summary>
        /// Clean up system resources
        /// </summary>
        void Cleanup();
        
        /// <summary>
        /// Update the system (called from main update loop)
        /// </summary>
        void Update();
        
        /// <summary>
        /// Whether the system is currently active
        /// </summary>
        bool IsActive { get; }
    }
}