using System.Runtime.InteropServices;

namespace CameraSuite.ViewerHost;

internal static class NativeConsole
{
#if WINDOWS
    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    private static bool _allocated;

    public static void Allocate()
    {
        if (_allocated)
        {
            return;
        }

        try
        {
            _allocated = AllocConsole();
        }
        catch
        {
            // ignore
        }
    }
#else
    public static void Allocate()
    {
    }
#endif
}
