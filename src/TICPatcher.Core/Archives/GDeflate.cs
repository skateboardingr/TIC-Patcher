using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TICPatcher.Core.Archives;

/// <summary>
/// GDeflate, the compression of the game's .pac archives, through Microsoft
/// DirectStorage's own codec (DStorageCreateCompressionCodec in dstorage.dll,
/// which the game ships). Windows only for now.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class GDeflate : IDisposable
{
    private const int FormatGDeflate = 1;
    private static readonly Guid CodecIid = new("84ef5121-9b43-4d03-b5c1-cc34606b262d");

    private void* _codec;

    /// <param name="dstorageFolder">A folder with dstorage.dll, e.g. the game's.</param>
    public GDeflate(string dstorageFolder, uint threads = 0)
    {
        // dstorage.dll finds its codec in dstoragecore.dll by name; load that first from the
        // same folder, or Windows searches next to the program and the codec is "not implemented".
        var core = System.IO.Path.Combine(dstorageFolder, "dstoragecore.dll");
        if (File.Exists(core))
            NativeLibrary.Load(core);
        var library = NativeLibrary.Load(System.IO.Path.Combine(dstorageFolder, "dstorage.dll"));
        var create = (delegate* unmanaged[Stdcall]<int, uint, Guid*, void**, int>)NativeLibrary.GetExport(library, "DStorageCreateCompressionCodec");
        var iid = CodecIid;
        void* codec;
        Marshal.ThrowExceptionForHR(create(FormatGDeflate, threads, &iid, &codec));
        _codec = codec;
    }

    private void** VTable => *(void***)_codec;

    public byte[] Decompress(ReadOnlySpan<byte> compressed, int size)
    {
        var output = new byte[size];
        nuint written;
        fixed (byte* source = compressed)
        fixed (byte* target = output)
        {
            var decompress = (delegate* unmanaged[Stdcall]<void*, byte*, nuint, byte*, nuint, nuint*, int>)VTable[4];
            Marshal.ThrowExceptionForHR(decompress(_codec, source, (nuint)compressed.Length, target, (nuint)size, &written));
        }
        if ((int)written != size)
            throw new InvalidDataException($"GDeflate gave {written} bytes, expected {size}.");
        return output;
    }

    public byte[] Compress(ReadOnlySpan<byte> data)
    {
        var bound = (delegate* unmanaged[Stdcall]<void*, nuint, nuint>)VTable[5];
        var output = new byte[(int)bound(_codec, (nuint)data.Length)];
        nuint written;
        fixed (byte* source = data)
        fixed (byte* target = output)
        {
            var compress = (delegate* unmanaged[Stdcall]<void*, byte*, nuint, int, byte*, nuint, nuint*, int>)VTable[3];
            Marshal.ThrowExceptionForHR(compress(_codec, source, (nuint)data.Length, 1 /* best ratio */, target, (nuint)output.Length, &written));
        }
        return output[..(int)written];
    }

    public void Dispose()
    {
        if (_codec is null)
            return;
        var release = (delegate* unmanaged[Stdcall]<void*, uint>)VTable[2];
        release(_codec);
        _codec = null;
    }
}
