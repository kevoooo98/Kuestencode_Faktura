using MudBlazor;

namespace InvoiceApp.Shared;

public static class KuestenCodeTheme
{
    public static MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            // Primärfarbe: #0F2A3D
            Primary = "#0F2A3D",
            PrimaryContrastText = "#FFFFFF",
            PrimaryDarken = "#081820",
            PrimaryLighten = "#1A3D55",

            // Sekundärfarbe: #2E3440
            Secondary = "#2E3440",
            SecondaryContrastText = "#FFFFFF",
            SecondaryDarken = "#1E2430",
            SecondaryLighten = "#3E4450",

            // Akzent: #3FA796
            Tertiary = "#3FA796",
            TertiaryContrastText = "#FFFFFF",

            // Hintergrund hell: #F4F6F8
            Background = "#F4F6F8",
            BackgroundGray = "#E8ECEF",
            Surface = "#FFFFFF",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#1A1A1A",
            AppbarBackground = "#0F2A3D",
            AppbarText = "#FFFFFF",

            // Text primär: #1A1A1A
            TextPrimary = "#1A1A1A",
            // Text sekundär: #6B7280
            TextSecondary = "#6B7280",
            TextDisabled = "#9CA3AF",

            // Warnung: #D1A93B
            Warning = "#D1A93B",
            WarningContrastText = "#1A1A1A",
            WarningDarken = "#B8942F",
            WarningLighten = "#D9B653",

            // Fehler: #C2413A
            Error = "#C2413A",
            ErrorContrastText = "#FFFFFF",
            ErrorDarken = "#A3352F",
            ErrorLighten = "#CF5D56",

            // Success (aus Akzent abgeleitet)
            Success = "#3FA796",
            SuccessContrastText = "#FFFFFF",
            SuccessDarken = "#358C7E",
            SuccessLighten = "#5BB5A6",

            // Info (aus Primär abgeleitet)
            Info = "#1A3D55",
            InfoContrastText = "#FFFFFF",
            InfoDarken = "#0F2A3D",
            InfoLighten = "#2A4D65",

            // Divider & Lines
            Divider = "#E5E7EB",
            DividerLight = "#F3F4F6",

            // Actions & States
            ActionDefault = "#6B7280",
            ActionDisabled = "#9CA3AF",
            ActionDisabledBackground = "#E5E7EB",

            // Hover & Focus
            HoverOpacity = 0.08,
            RippleOpacity = 0.12,
        },

        PaletteDark = new PaletteDark
        {
            // Primärfarbe: #0F2A3D (deutlich aufgehellt für Dark Mode)
            Primary = "#4A9FD8",
            PrimaryContrastText = "#000000",
            PrimaryDarken = "#2A7DB8",
            PrimaryLighten = "#6AB5E0",

            // Sekundärfarbe: #2E3440 (aufgehellt)
            Secondary = "#5E6470",
            SecondaryContrastText = "#FFFFFF",
            SecondaryDarken = "#3E4450",
            SecondaryLighten = "#7E8490",

            // Akzent: #3FA796 (aufgehellt)
            Tertiary = "#6BC5B5",
            TertiaryContrastText = "#000000",

            // Hintergrund dunkel - hellere Oberflächen für besseren Kontrast
            Background = "#121212",
            BackgroundGray = "#1E1E1E",
            Surface = "#1E1E1E",
            DrawerBackground = "#1E1E1E",
            DrawerText = "#E5E7EB",
            AppbarBackground = "#0F2A3D",
            AppbarText = "#FFFFFF",

            // Text - deutlich hellere Sekundärfarbe für besseren Kontrast
            TextPrimary = "#FFFFFF",
            TextSecondary = "#B8BDC5",
            TextDisabled = "#757575",

            // Warnung: #D1A93B
            Warning = "#E5C56F",
            WarningContrastText = "#000000",

            // Fehler: #C2413A (aufgehellt)
            Error = "#E57373",
            ErrorContrastText = "#000000",

            // Success (aufgehellt)
            Success = "#6BC5B5",
            SuccessContrastText = "#000000",

            // Info (aufgehellt)
            Info = "#4A9FD8",
            InfoContrastText = "#000000",

            // Divider - heller für bessere Sichtbarkeit
            Divider = "#3A3A3A",
            DividerLight = "#2A2A2A",

            // Actions - hellere Farben
            ActionDefault = "#B8BDC5",
            ActionDisabled = "#757575",
            ActionDisabledBackground = "#2A2A2A",

            HoverOpacity = 0.10,
            RippleOpacity = 0.15,
        },

        LayoutProperties = new LayoutProperties
        {
            DrawerWidthLeft = "260px",
            DrawerWidthRight = "300px",
            AppbarHeight = "64px"
        },

        ZIndex = new ZIndex
        {
            Drawer = 1300,
            AppBar = 1200,
            Dialog = 1400,
            Popover = 1500,
            Snackbar = 1600,
            Tooltip = 1700
        }
    };
}
