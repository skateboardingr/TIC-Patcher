using Iced.Intel;

// Disasm <image.bin> <module offset hex>[:before bytes]... prints the code around each offset,
// marking the instruction that starts exactly there. Offsets come from TIC Loader's watch-report.txt.
var image = File.ReadAllBytes(args[0]);
if (args[1] == "--find-disp")
{
    // --find-disp <hex displacement>[,<hex>...] [from hex] [to hex]: every instruction using
    // [reg+disp], found by a linear sweep (data between functions can produce a few false hits).
    var wanted = args[2].Split(',').Select(d => Convert.ToUInt64(d, 16)).ToHashSet();
    var from = args.Length > 3 ? Convert.ToInt32(args[3], 16) : 0x1000;
    var to = args.Length > 4 ? Convert.ToInt32(args[4], 16) : image.Length;
    var sweep = Decoder.Create(64, new ByteArrayCodeReader(image, from, to - from));
    sweep.IP = (ulong)from;
    var text = new IntelFormatter();
    var line = new StringOutput();
    while (sweep.IP < (ulong)to)
    {
        var instruction = sweep.Decode();
        if (instruction.IsInvalid || instruction.MemoryBase == Register.None || instruction.MemoryBase == Register.RIP)
            continue;
        if (wanted.Contains(instruction.MemoryDisplacement64))
        {
            text.Format(instruction, line);
            Console.WriteLine($"+0x{instruction.IP:X}  {line.ToStringAndReset()}");
        }
    }
    return;
}
if (args[1] == "--find-addr")
{
    // --find-addr <hex module offset>[,...]: instructions that address it RIP-relative (lea/mov [rip+x]).
    var targets = args[2].Split(',').Select(d => Convert.ToUInt64(d, 16)).ToHashSet();
    var sweep = Decoder.Create(64, new ByteArrayCodeReader(image, 0x1000, Math.Min(image.Length, 0x1000000) - 0x1000));
    sweep.IP = 0x1000;
    var text = new IntelFormatter();
    var line = new StringOutput();
    while (sweep.IP < (ulong)Math.Min(image.Length, 0x1000000))
    {
        var instruction = sweep.Decode();
        if (instruction.IsInvalid || !instruction.IsIPRelativeMemoryOperand)
            continue;
        if (targets.Contains(instruction.IPRelativeMemoryAddress))
        {
            text.Format(instruction, line);
            Console.WriteLine($"+0x{instruction.IP:X}  {line.ToStringAndReset()}");
        }
    }
    return;
}
foreach (var spec in args[1..])
{
    var parts = spec.Split(':');
    var target = Convert.ToInt64(parts[0], 16);
    var before = parts.Length > 1 ? int.Parse(parts[1]) : 48;
    Console.WriteLine($"--- +0x{target:X}");
    // Decode from a little before the target; x86 resynchronises within a few instructions.
    var start = target - before;
    var decoder = Decoder.Create(64, new ByteArrayCodeReader(image, (int)start, before + 48));
    decoder.IP = (ulong)start;
    var formatter = new IntelFormatter();
    var output = new StringOutput();
    while (decoder.IP < (ulong)(target + 40))
    {
        var instruction = decoder.Decode();
        formatter.Format(instruction, output);
        var mark = instruction.IP == (ulong)target ? ">>" : "  ";
        Console.WriteLine($"{mark} +0x{instruction.IP:X}  {output.ToStringAndReset()}");
    }
}
