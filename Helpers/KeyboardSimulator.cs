using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Serilog;

namespace SmartIDReader.Helpers
{
    public static class KeyboardSimulator
    {
        #region P/Invoke

        [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] p, int cb);

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public InputUnion U; }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx, dy, mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        struct HARDWAREINPUT { public uint uMsg; public ushort wParamL, wParamH; }

        const uint INPUT_KEYBOARD   = 1;
        const uint KEYEVENTF_KEYUP  = 0x0002;
        const ushort VK_CONTROL = 0x11, VK_V = 0x56, VK_RETURN = 0x0D;

        #endregion

        static INPUT Down(ushort vk) => new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk } } };
        static INPUT Up(ushort vk)   => new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP } } };

        public static void PasteAndEnterAsync() => Task.Run(async () =>
        {
            try
            {
                await Task.Delay(150);
                SendInput(4, new[] { Down(VK_CONTROL), Down(VK_V), Up(VK_V), Up(VK_CONTROL) }, Marshal.SizeOf<INPUT>());
                await Task.Delay(250);
                SendInput(2, new[] { Down(VK_RETURN), Up(VK_RETURN) }, Marshal.SizeOf<INPUT>());
            }
            catch (Exception ex) { Log.Error(ex, "Lỗi giả lập bàn phím"); }
        });
    }
}
