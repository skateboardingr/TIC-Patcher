using System.Runtime.InteropServices;
using TICPatcher.Core.Tables.Schemas;
using TICPatcher.Loader;

// A read-only "module" holding a fake ActionAbilities table at offset 0x100.
// Several threads read Cure's MPCost (byte 13) and Fire's Range (byte 0);
// nothing reads byte 16. Reads go through native code (RtlMoveMemory), as the
// game's do: .NET code can't run the handler for an exception it raised itself. The report must show exactly that, and the program must not crash.
const int Size = 0x4000;
var module = VirtualAlloc(0, Size, 0x3000, 0x04);
var table = module + 0x100;
for (var i = 0; i < Abilities.Action.Length; i++)
    Marshal.WriteByte(table + i, (byte)i);
VirtualProtect(module, Size, 0x02, out _); // read-only, like the game's .rdata

var report = Path.Combine(Path.GetTempPath(), "watch-test-report.txt");
var log = new List<string>();
ReadWatcher.Start(new ReadWatcher.Config(["ActionAbilities"], Minutes: 0.01),
    new Dictionary<string, long> { ["ActionAbilities"] = 0x100 }, module, module + Size, report, log.Add);

long sum = 0;
var readers = Enumerable.Range(0, 4).Select(_ => new Thread(() =>
{
    for (var n = 0; n < 2000; n++)
    {
        Interlocked.Add(ref sum, NativeRead(table + 1 * 20 + 13));
        Interlocked.Add(ref sum, NativeRead(table + 0x10 * 20 + 0));
    }
})).ToList();
readers.ForEach(t => t.Start());
readers.ForEach(t => t.Join());

Thread.Sleep(TimeSpan.FromSeconds(33));
log.ForEach(Console.WriteLine);
Console.WriteLine(File.ReadAllText(report));
Console.WriteLine($"sum {sum} (expected {4 * 2000 * (33 + 64)})");

static unsafe byte NativeRead(nint address)
{
    byte value;
    RtlMoveMemory(&value, address, 1);
    return value;
}

[DllImport("kernel32.dll")] static extern unsafe void RtlMoveMemory(byte* destination, nint source, nint length);
[DllImport("kernel32.dll")] static extern nint VirtualAlloc(nint address, nint size, uint type, uint protect);
[DllImport("kernel32.dll")] static extern bool VirtualProtect(nint address, nint size, uint protect, out uint old);
