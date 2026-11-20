using TICPatcher.Core.Memory;
using TICPatcher.Core.Mods;
using TICPatcher.Core.Tables;
using TICPatcher.Core.Tables.Schemas;

namespace TICPatcher.Core.Tests;

// Generated data only.
public class SchemaTests
{
    [Fact]
    public void EveryFieldFitsItsRecordAndNamesAreUnique()
    {
        foreach (var table in AllTables.List)
        {
            Assert.Equal(table.Fields.Count, table.Fields.Select(f => f.Name).Distinct().Count());
            foreach (var field in table.Fields)
            {
                Assert.True(field.Offset + field.Size <= table.RecordSize, $"{table.Name}.{field.Name}");
                if (field.Kind == FieldKind.AbilityId)
                    Assert.True(field.HighOffset < table.RecordSize, $"{table.Name}.{field.Name}");
            }
        }
    }

    [Fact]
    public void EveryKnownTableHasALocationAndViceVersa()
    {
        var build = GameBuilds.Known[0];
        // Battles come from the game's data files, not the executable.
        Assert.Equal(AllTables.ByName.Keys.Where(t => t != Battles.Units.Name).Order(), build.Tables.Keys.Order());
    }

    [Fact]
    public void NibbleSharesItsByteWithFlags()
    {
        var record = new byte[Abilities.Common.RecordSize];
        record[3] = 0b0100_0000; // DisplayAbilityName
        RecordCodec.Write(Abilities.Common.Field("AbilityType"), record, "5");
        Assert.Equal(0b0100_0101, record[3]);
        Assert.Equal("DisplayAbilityName", RecordCodec.Read(Abilities.Common.Field("Flags"), record));
        RecordCodec.Write(Abilities.Common.Field("Flags"), record, "LearnOnHit");
        Assert.Equal("5", RecordCodec.Read(Abilities.Common.Field("AbilityType"), record));
        Assert.Throws<OverflowException>(() => RecordCodec.Write(Abilities.Common.Field("AbilityType"), record, "16"));
    }

    [Fact]
    public void NineBitAbilityIdsUseTheirHighBit()
    {
        var record = new byte[SkillSets.Main.RecordSize];
        var action9 = SkillSets.Main.Field("Action9");
        RecordCodec.Write(action9, record, "421"); // 0x1A5
        Assert.Equal(0xA5, record[3 + 8]);
        Assert.Equal(0x80, record[1]);
        Assert.Equal("421", RecordCodec.Read(action9, record));
        RecordCodec.Write(action9, record, "7");
        Assert.Equal(0, record[1]);

        var rsm3 = SkillSets.Main.Field("RSM3");
        RecordCodec.Write(rsm3, record, "500");
        Assert.Equal(0x20, record[2]);
    }

    [Fact]
    public void PartTablesUseTheGamesIds()
    {
        var edits = TableEdits.Parse("""{ "Armor": { "0x90": { "HPBonus": 10 }, "0x91": { "MPBonus": 5 } } }""");
        var table = new byte[Items.Armor.Length];
        Assert.Equal(2, edits.ApplyTo("Armor", table, Items.Armor.RecordSize));
        Assert.Equal(10, table[0]);
        Assert.Equal(5, table[3]);
        var error = Assert.Throws<FormatException>(() => TableEdits.Parse("""{ "Armor": { "0x8F": { "HPBonus": 1 } } }"""));
        Assert.Contains("0x90-0xCF", error.Message);
    }

    [Fact]
    public void LinksFindTheTableHoldingTheRecord()
    {
        Assert.Equal("SkillSets", AllTables.Resolve("SkillSets", 0x05)?.Name);
        Assert.Equal("MonsterSkills", AllTables.Resolve("SkillSets", 0xB0)?.Name);
        Assert.Equal("SkillSetsExtra", AllTables.Resolve("SkillSets", 0xE1)?.Name);
        Assert.Equal("ItemAttributes", AllTables.Resolve("ItemAttributes", 0x01)?.Name);
        Assert.Null(AllTables.Resolve("ItemAttributes", 0x99));
        // Every link points at a real table.
        foreach (var field in AllTables.List.SelectMany(t => t.Fields).Where(f => f.LinksTo is not null))
            Assert.True(AllTables.ByName.ContainsKey(field.LinksTo!), field.LinksTo);

        var runeblade = AllTables.Related(Items.Common, 0x1E).Select(t => t.Name).ToList();
        Assert.Contains("Weapons", runeblade);
        Assert.Contains("ShopInventories", runeblade);
        Assert.DoesNotContain("Armor", runeblade);
        Assert.Contains("ActionAbilities", AllTables.Related(Abilities.Common, 0x01).Select(t => t.Name));
        Assert.Equal(["ExtraItems"], AllTables.Related(Items.ExtraWeapons, 0x101).Select(t => t.Name));
    }

    [Fact]
    public void IdentifiesBuildsByTheirPeHeader()
    {
        var known = GameBuilds.Known[0];
        var header = new byte[0x200];
        header[0x3C] = 0x80;
        "PE\0\0"u8.CopyTo(header.AsSpan(0x80));
        BitConverter.GetBytes(known.TimeDateStamp).CopyTo(header, 0x80 + 8);
        BitConverter.GetBytes(known.SizeOfImage).CopyTo(header, 0x80 + 24 + 56);
        Assert.Same(known, GameBuilds.Identify(header));

        header[0x80 + 8] ^= 1;
        Assert.Null(GameBuilds.Identify(header));
        Assert.Null(GameBuilds.Identify(new byte[0x200]));
    }
}
