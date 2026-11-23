using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using TICPatcher.Core.Archives;

namespace TICPatcher.Loader;

/// <summary>
/// Sees what the game decompresses from its .pac archives: the game unpacks GDeflate blocks
/// through DirectStorage's codec, whose function table every codec in the process shares, so
/// wrapping its decompress entry catches the game's calls. After the game's own decompression
/// runs, <see cref="Output"/> gets the unpacked bytes, where a mod's data-file edits (battles,
/// sprites) can be made before the game uses them.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe class DecompressHook
{
    private static delegate* unmanaged[Stdcall]<void*, byte*, nuint, byte*, nuint, nuint*, int> _original;
    private static GDeflate? _codec; // kept alive: its function table is the one wrapped

    /// <summary>Called after each successful decompression with the output (the game's buffer).</summary>
    public static Action<IntPtr, int>? Output { get; set; }

    public static bool Installed => _original != null;

    public static void Install(string gameFolder)
    {
        if (Installed)
            return;
        _codec = new GDeflate(gameFolder);
        var slot = (void**)_codec.FunctionTable + GDeflate.DecompressSlot;
        const uint ReadWrite = 0x04;
        if (!VirtualProtect((IntPtr)slot, (nuint)sizeof(void*), ReadWrite, out var old))
            throw new InvalidOperationException($"VirtualProtect failed ({Marshal.GetLastWin32Error()})");
        _original = (delegate* unmanaged[Stdcall]<void*, byte*, nuint, byte*, nuint, nuint*, int>)*slot;
        *slot = (delegate* unmanaged[Stdcall]<void*, byte*, nuint, byte*, nuint, nuint*, int>)&Decompress;
        VirtualProtect((IntPtr)slot, (nuint)sizeof(void*), old, out _);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Decompress(void* self, byte* source, nuint sourceSize, byte* target, nuint targetSize, nuint* written)
    {
        var result = _original(self, source, sourceSize, target, targetSize, written);
        if (result >= 0 && Output is { } output)
        {
            try
            {
                output((IntPtr)target, (int)(written != null ? *written : targetSize));
            }
            catch
            {
                // Never let a mod's handler break the game's loading.
            }
        }
        return result;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(IntPtr address, nuint size, uint protect, out uint old);
}
