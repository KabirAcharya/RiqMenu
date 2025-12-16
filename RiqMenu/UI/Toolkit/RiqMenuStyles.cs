namespace RiqMenu.UI.Toolkit
{
    /// <summary>
    /// USS styles matching the Riqs & Mods website aesthetic
    /// </summary>
    public static class RiqMenuStyles
    {
        // Color palette from the website
        public const string Cream = "#FFF8F0";
        public const string WarmWhite = "#FFFCF7";
        public const string SoftPeach = "#FFF0E6";

        public const string Cyan = "#00C8FF";
        public const string CyanLight = "#7DE3FF";
        public const string CyanDark = "#00A3D9";

        public const string Coral = "#FF7B7B";
        public const string CoralLight = "#FFB3B3";

        public const string Yellow = "#FFD93D";
        public const string YellowLight = "#FFEB99";

        public const string Lavender = "#E8D5FF";
        public const string Purple = "#9B6DD7";

        public const string Mint = "#7DDBA3";
        public const string Green = "#4CAF7D";

        public const string Charcoal = "#2D3748";
        public const string Gray = "#718096";
        public const string GrayLight = "#A0AEC0";
        public const string GrayLighter = "#E2E8F0";

        /// <summary>
        /// Main USS stylesheet for RiqMenu UI Toolkit
        /// </summary>
        public static string MainStylesheet => $@"
/* Root and overlay */
.riq-overlay {{
    position: absolute;
    left: 0;
    right: 0;
    top: 0;
    bottom: 0;
    background-color: rgba(0, 0, 0, 0.85);
    align-items: center;
    justify-content: center;
}}

.riq-card {{
    width: 700px;
    height: 580px;
    background-color: {WarmWhite};
    border-radius: 24px;
    border-width: 4px;
    border-color: {GrayLighter};
    padding: 0;
    overflow: hidden;
}}

/* Tabs */
.riq-tabs {{
    flex-direction: row;
    background-color: {Cream};
    border-bottom-width: 3px;
    border-bottom-color: {GrayLighter};
    padding: 12px 16px 0 16px;
}}

.riq-tab {{
    padding: 12px 24px;
    margin-right: 8px;
    border-top-left-radius: 12px;
    border-top-right-radius: 12px;
    background-color: transparent;
    border-width: 0;
    -unity-font-style: bold;
    font-size: 14px;
    color: {Gray};
    transition-duration: 0.15s;
}}

.riq-tab:hover {{
    background-color: {GrayLighter};
    color: {Charcoal};
}}

.riq-tab.active {{
    background-color: {WarmWhite};
    color: {CyanDark};
    border-width: 3px;
    border-bottom-width: 0;
    border-color: {GrayLighter};
    margin-bottom: -3px;
    padding-bottom: 15px;
}}

.riq-tab-hint {{
    flex-grow: 1;
    -unity-text-align: middle-right;
    font-size: 11px;
    color: {GrayLight};
    padding-right: 8px;
}}

/* Content area */
.riq-content {{
    flex-grow: 1;
    padding: 20px;
}}

.riq-title {{
    font-size: 20px;
    -unity-font-style: bold;
    color: {Charcoal};
    margin-bottom: 16px;
}}

/* Search box */
.riq-search-container {{
    margin-bottom: 16px;
}}

.riq-search-input {{
    height: 44px;
    padding: 0 20px;
    border-radius: 22px;
    border-width: 3px;
    border-color: {GrayLighter};
    background-color: white;
    font-size: 14px;
    -unity-font-style: bold;
    color: {Charcoal};
}}

.riq-search-input:focus {{
    border-color: {Cyan};
}}

/* Song list */
.riq-song-list {{
    flex-grow: 1;
    background-color: transparent;
}}

.riq-song-item {{
    flex-direction: column;
    padding: 12px 16px;
    margin-bottom: 8px;
    background-color: white;
    border-radius: 16px;
    border-width: 3px;
    border-color: {GrayLighter};
    transition-duration: 0.15s;
}}

.riq-song-item:hover {{
    border-color: {Cyan};
    translate: 0 -2px;
}}

.riq-song-item.selected {{
    background-color: {CyanLight};
    border-color: {Cyan};
}}

.riq-song-header {{
    flex-direction: row;
    align-items: center;
    margin-bottom: 4px;
}}

.riq-song-title {{
    font-size: 15px;
    -unity-font-style: bold;
    color: {Charcoal};
    flex-grow: 1;
}}

.riq-song-meta {{
    flex-direction: row;
    align-items: center;
}}

.riq-song-creator {{
    font-size: 12px;
    color: {Gray};
    -unity-font-style: bold;
}}

/* Tags/Badges */
.riq-tag {{
    padding: 4px 12px;
    border-radius: 50px;
    font-size: 11px;
    -unity-font-style: bold;
    margin-left: 8px;
    border-width: 2px;
}}

.riq-tag-riq {{
    background-color: {CyanLight};
    color: {CyanDark};
    border-color: {Cyan};
}}

.riq-tag-bop {{
    background-color: {YellowLight};
    color: #B8860B;
    border-color: {Yellow};
}}

.riq-tag-bpm {{
    background-color: {Lavender};
    color: {Purple};
    border-color: {Purple};
}}

.riq-tag-downloads {{
    background-color: {SoftPeach};
    color: {Coral};
    border-color: {CoralLight};
}}

/* Buttons */
.riq-btn {{
    padding: 14px 28px;
    border-radius: 50px;
    font-size: 14px;
    -unity-font-style: bold;
    border-width: 3px;
    transition-duration: 0.15s;
}}

.riq-btn-primary {{
    background-color: {Cyan};
    color: white;
    border-color: {CyanDark};
}}

.riq-btn-primary:hover {{
    background-color: {CyanLight};
    translate: 0 -2px;
}}

.riq-btn-secondary {{
    background-color: white;
    color: {Charcoal};
    border-color: {GrayLighter};
}}

.riq-btn-secondary:hover {{
    background-color: {SoftPeach};
    border-color: {CoralLight};
}}

.riq-btn-coral {{
    background-color: {Coral};
    color: white;
    border-color: #E05A5A;
}}

/* Footer */
.riq-footer {{
    padding: 16px 20px;
    background-color: {Cream};
    border-top-width: 3px;
    border-top-color: {GrayLighter};
}}

.riq-footer-text {{
    font-size: 11px;
    color: {Gray};
    -unity-text-align: middle-center;
}}

/* Status messages */
.riq-status {{
    padding: 12px 16px;
    border-radius: 12px;
    margin-bottom: 12px;
    font-size: 13px;
    -unity-font-style: bold;
}}

.riq-status-loading {{
    background-color: {CyanLight};
    color: {CyanDark};
}}

.riq-status-success {{
    background-color: {Mint};
    color: {Green};
}}

.riq-status-error {{
    background-color: {CoralLight};
    color: {Coral};
}}

/* Progress bar */
.riq-progress-container {{
    height: 8px;
    background-color: {GrayLighter};
    border-radius: 4px;
    margin-top: 8px;
    overflow: hidden;
}}

.riq-progress-bar {{
    height: 100%;
    background-color: {Cyan};
    border-radius: 4px;
    transition-duration: 0.2s;
}}

/* Empty state */
.riq-empty {{
    flex-grow: 1;
    align-items: center;
    justify-content: center;
}}

.riq-empty-icon {{
    font-size: 48px;
    color: {GrayLight};
    margin-bottom: 16px;
}}

.riq-empty-text {{
    font-size: 16px;
    -unity-font-style: bold;
    color: {Gray};
}}

/* Scrollbar styling */
.unity-scroller--vertical {{
    width: 12px;
}}

.unity-scroller--vertical .unity-base-slider__tracker {{
    background-color: {GrayLighter};
    border-radius: 6px;
}}

.unity-scroller--vertical .unity-base-slider__dragger {{
    background-color: {GrayLight};
    border-radius: 6px;
}}

.unity-scroller--vertical .unity-base-slider__dragger:hover {{
    background-color: {Cyan};
}}
";
    }
}
