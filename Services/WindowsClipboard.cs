using System.Runtime.InteropServices;

namespace OSSStudio.Services;

internal static partial class WindowsClipboard
{
    private const uint GmemMoveable = 0x0002;
    private const uint CfUnicodeText = 13;

    public static bool TrySetText(string text)
    {
        if (!TryOpenClipboard())
        {
            return false;
        }

        nint memory = 0;
        try
        {
            if (EmptyClipboard() == 0)
            {
                return false;
            }

            var characters = (text + '\0').ToCharArray();
            memory = GlobalAlloc(GmemMoveable, (nuint)(characters.Length * sizeof(char)));
            if (memory == 0)
            {
                return false;
            }

            var pointer = GlobalLock(memory);
            if (pointer == 0)
            {
                return false;
            }

            try
            {
                Marshal.Copy(characters, 0, pointer, characters.Length);
            }
            finally
            {
                GlobalUnlock(memory);
            }

            if (SetClipboardData(CfUnicodeText, memory) == 0)
            {
                return false;
            }

            memory = 0;
            return true;
        }
        finally
        {
            if (memory != 0)
            {
                GlobalFree(memory);
            }
            CloseClipboard();
        }
    }

    private static bool TryOpenClipboard()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (OpenClipboard(0) != 0)
            {
                return true;
            }
            Thread.Sleep(20);
        }
        return false;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int OpenClipboard(nint owner);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetClipboardData(uint format, nint memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int GlobalUnlock(nint memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalFree(nint memory);
}
