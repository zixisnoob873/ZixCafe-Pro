using System.Runtime.InteropServices;

namespace ZixCafe.Client.Agent;

public static class DisplayRefreshRateEnforcer
{
    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int CDS_UPDATEREGISTRY = 0x01;
    private const int DISP_CHANGE_SUCCESSFUL = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public short dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    private const int DM_DISPLAYFREQUENCY = 0x400000;
    private const int DM_PELSWIDTH = 0x80000;
    private const int DM_PELSHEIGHT = 0x100000;

    [DllImport("user32.dll")]
    private static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    [DllImport("user32.dll")]
    private static extern int ChangeDisplaySettings(ref DEVMODE lpDevMode, int dwFlags);

    public static (int currentHz, int maxHz, string resolution) GetDisplayRefreshInfo()
    {
        try
        {
            var currentMode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref currentMode))
            {
                return (60, 60, "1920x1080");
            }

            var currentHz = currentMode.dmDisplayFrequency;
            var width = currentMode.dmPelsWidth;
            var height = currentMode.dmPelsHeight;
            var resolution = $"{width}x{height}";

            var maxHz = currentHz;
            var modeIndex = 0;
            var testMode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };

            while (EnumDisplaySettings(null, modeIndex, ref testMode))
            {
                // Look for supported modes at current native resolution
                if (testMode.dmPelsWidth == width && testMode.dmPelsHeight == height)
                {
                    if (testMode.dmDisplayFrequency > maxHz)
                    {
                        maxHz = testMode.dmDisplayFrequency;
                    }
                }
                modeIndex++;
            }

            return (currentHz, maxHz, resolution);
        }
        catch
        {
            return (60, 60, "1920x1080");
        }
    }

    public static bool EnforceMaximumNativeRefreshRate(out int previousHz, out int newHz)
    {
        var (curr, max, _) = GetDisplayRefreshInfo();
        previousHz = curr;
        newHz = max;

        if (max <= curr)
        {
            return false; // Already running at max native refresh rate
        }

        try
        {
            var targetMode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref targetMode))
            {
                targetMode.dmDisplayFrequency = max;
                targetMode.dmFields = DM_DISPLAYFREQUENCY | DM_PELSWIDTH | DM_PELSHEIGHT;

                var result = ChangeDisplaySettings(ref targetMode, CDS_UPDATEREGISTRY);
                if (result == DISP_CHANGE_SUCCESSFUL)
                {
                    newHz = max;
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    public static bool EnforceTargetRefreshRate(int targetHz)
    {
        try
        {
            var targetMode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref targetMode))
            {
                targetMode.dmDisplayFrequency = targetHz;
                targetMode.dmFields = DM_DISPLAYFREQUENCY | DM_PELSWIDTH | DM_PELSHEIGHT;

                var result = ChangeDisplaySettings(ref targetMode, CDS_UPDATEREGISTRY);
                return result == DISP_CHANGE_SUCCESSFUL;
            }
        }
        catch
        {
        }

        return false;
    }
}
