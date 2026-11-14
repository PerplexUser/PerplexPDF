using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PerplexPDF.App;

internal static class WindowsColorPicker
{
    private const uint InitializeRgb = 0x00000001;
    private const uint OpenCustomColors = 0x00000002;
    private static readonly uint[] Custom = Enumerable.Repeat(0x00FFFFFFu, 16).ToArray();

    internal static uint? Choose(Window owner, uint argb)
    {
        var buffer = GCHandle.Alloc(Custom, GCHandleType.Pinned);
        try
        {
            var dialog = new ColorDialogData
            {
                Size = (uint)Marshal.SizeOf<ColorDialogData>(),
                Owner = new WindowInteropHelper(owner).Handle,
                Rgb = SwapRedAndBlue(argb),
                Custom = buffer.AddrOfPinnedObject(),
                Flags = InitializeRgb | OpenCustomColors
            };
            return ChooseColorW(ref dialog) ? (argb & 0xFF000000) | SwapRedAndBlue(dialog.Rgb) : null;
        }
        finally { buffer.Free(); }
    }

    private static uint SwapRedAndBlue(uint value) =>
        ((value & 0xFF) << 16) | (value & 0xFF00) | ((value >> 16) & 0xFF);

    [StructLayout(LayoutKind.Sequential)]
    private struct ColorDialogData
    {
        internal uint Size;
        internal nint Owner;
        internal nint Instance;
        internal uint Rgb;
        internal nint Custom;
        internal uint Flags;
        internal nint CustomData;
        internal nint Hook;
        internal nint Template;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("comdlg32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChooseColorW(ref ColorDialogData dialog);
}
