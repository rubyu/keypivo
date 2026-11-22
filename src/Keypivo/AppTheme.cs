namespace Keypivo;

internal static class AppTheme
{
    public static Color Surface => SystemInformation.HighContrast ? SystemColors.Window : Color.White;
    public static Color Chrome => SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(246, 248, 246);
    public static Color Text => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(32, 40, 36);
    public static Color Muted => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(104, 113, 108);
    public static Color Border => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(232, 236, 233);
    public static Color Hover => SystemInformation.HighContrast ? SystemColors.Control : Color.FromArgb(244, 246, 244);
    public static Color AccentSurface => SystemInformation.HighContrast ? SystemColors.Control : Color.FromArgb(234, 244, 239);
    public static Color AccentHover => SystemInformation.HighContrast ? SystemColors.ControlLight : Color.FromArgb(217, 234, 225);
    public static Color Error => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(169, 62, 40);
    public static Color ErrorSurface => SystemInformation.HighContrast ? SystemColors.Control : Color.FromArgb(249, 234, 234);
    public static Color Accent => SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(37, 109, 91);
    public static Color OnAccent => SystemInformation.HighContrast ? SystemColors.HighlightText : Color.White;
}
