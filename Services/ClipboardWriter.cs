using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ClipBoardX.Models;

namespace ClipBoardX.Services;

/// <summary>Кладёт содержимое записи обратно в системный буфер обмена.</summary>
internal static class ClipboardWriter
{
    private const int KeyEventKeyUp = 0x0002;
    private const uint InputKeyboard = 1;

    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;
    private const ushort VK_SHIFT = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public static void Write(ClipItem item)
    {
        // Помечаем как «свою» запись, чтобы слушатель её не перехватил.
        ClipboardReader.SetInternalWriteFlag(true);

        try
        {
            for (var attempt = 0; attempt < 8; attempt++)
            {
                try
                {
                    switch (item.Kind)
                    {
                        case ClipKind.Text:
                        case ClipKind.Html:
                            Clipboard.SetText(item.Text, TextDataFormat.UnicodeText);
                            break;

                        case ClipKind.Files:
                            var dropList = new StringCollection();
                            foreach (var file in item.Files) dropList.Add(file);
                            Clipboard.SetFileDropList(dropList);
                            break;

                        case ClipKind.Image:
                            WriteImage(item);
                            break;
                    }

                    return;
                }
                catch (ExternalException)
                {
                    Thread.Sleep(25 + attempt * 15);
                }
            }
        }
        finally
        {
            ClipboardReader.SetInternalWriteFlag(false);
        }
    }

    private static void WriteImage(ClipItem item)
    {
        if (string.IsNullOrEmpty(item.ImagePath) || !File.Exists(item.ImagePath))
        {
            return;
        }

        using var image = Image.FromFile(item.ImagePath);
        Clipboard.SetImage(new Bitmap(image));
    }

    /// <summary>
    /// Отправляет Ctrl+V (или Shift+Insert) в указанное окно после возврата фокуса.
    /// </summary>
    public static void SendPaste(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        NativeMethods.SetForegroundWindow(hwnd);
        if (NativeMethods.IsIconic(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, 9 /* SW_RESTORE */);
        }

        Thread.Sleep(80);
        SimulatePaste();
    }

    private static void SimulatePaste()
    {
        var inputs = new[]
        {
            MakeKey(VK_CONTROL, down: true),
            MakeKey(VK_V, down: true),
            MakeKey(VK_V, down: false),
            MakeKey(VK_CONTROL, down: false)
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT MakeKey(ushort vk, bool down) => new()
    {
        type = InputKeyboard,
        U = new INPUTUNION
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = down ? 0u : KeyEventKeyUp,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        }
    };
}