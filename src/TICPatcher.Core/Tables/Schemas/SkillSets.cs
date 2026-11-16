namespace TICPatcher.Core.Tables.Schemas;

/// <summary>
/// Skill sets, monster skills, action menus and job unlocks.
/// Ability ids in skill sets and monster skills are 9 bits (0x000-0x1FF):
/// the 9th bit lives in a separate flags byte, which TIC Patcher handles for you.
///
/// Source: FFTPatcher (https://github.com/Glain/FFTPatcher, GPL-3.0) by
/// Joe Davidson and Glain: Datatypes/Job/SkillSet.cs, MonsterSkills.cs,
/// ActionMenu.cs, JobLevels.cs. Field names follow FFTPatcher's.
/// </summary>
public static class SkillSets
{
    private static IReadOnlyList<FieldDef> SkillSetFields()
    {
        var fields = new List<FieldDef>();
        for (var i = 0; i < 16; i++)
            fields.Add(FieldDef.Ability($"Action{i + 1}", 3 + i, i / 8, (byte)(0x80 >> (i % 8))));
        for (var i = 0; i < 6; i++)
            fields.Add(FieldDef.Ability($"RSM{i + 1}", 19 + i, 2, (byte)(0x80 >> i)));
        return fields;
    }

    public static readonly TableSchema Main = new(
        "SkillSets", RecordSize: 25, RecordCount: 0xB0, Fields: SkillSetFields(),
        Source: "FFTPatcher Datatypes/Job/SkillSet.cs") { RecordNames = Names.SkillSets };

    /// <summary>Skill sets 0xE0-0xE2, stored right after 0x00-0xAF.</summary>
    public static readonly TableSchema Extra = new(
        "SkillSetsExtra", RecordSize: 25, RecordCount: 3, Fields: SkillSetFields(),
        Source: "FFTPatcher Datatypes/Job/SkillSet.cs (AllSkillSets)") { FirstId = 0xE0, RecordNames = Names.SkillSets };

    public static readonly TableSchema MonsterSkills = new(
        "MonsterSkills",
        RecordSize: 5,
        RecordCount: 48,
        Fields:
        [
            FieldDef.Ability("Ability1", 1, 0, 0x80),
            FieldDef.Ability("Ability2", 2, 0, 0x40),
            FieldDef.Ability("Ability3", 3, 0, 0x20),
            FieldDef.Ability("Beastmaster", 4, 0, 0x10),
        ],
        Source: "FFTPatcher Datatypes/Job/MonsterSkills.cs") { FirstId = 0xB0, RecordNames = Names.SkillSets };

    /// <summary>What each skill set's menu does (FFTPatcher's MenuAction numbers).</summary>
    public static readonly TableSchema ActionMenus = new(
        "ActionMenus", RecordSize: 1, RecordCount: 0xE3,
        Fields: [FieldDef.U8("MenuAction", 0)],
        Source: "FFTPatcher Datatypes/Job/ActionMenu.cs") { RecordNames = Names.SkillSets };

    /// <summary>
    /// Job unlock requirements: record N is the job in <see cref="RequirementJobs"/>[N];
    /// each field is the level needed in that job (0-8).
    /// </summary>
    public static readonly IReadOnlyList<string> RequirementJobs =
    [
        "Chemist", "Knight", "Archer", "Monk", "WhiteMage", "BlackMage", "TimeMage", "Summoner", "Thief", "Orator",
        "Mystic", "Geomancer", "Dragoon", "Samurai", "Ninja", "Arithmetician", "Bard", "Dancer", "Mime", "DarkKnight", "OnionKnight", "Unknown",
    ];

    private static readonly string[] RequirementFieldNames =
    [
        "Squire", "Chemist", "Knight", "Archer", "Monk", "WhiteMage", "BlackMage", "TimeMage", "Summoner", "Thief", "Orator", "Mystic",
        "Geomancer", "Dragoon", "Samurai", "Ninja", "Arithmetician", "Bard", "Dancer", "Mime", "DarkKnight", "OnionKnight", "Unknown1", "Unknown2",
    ];

    public static readonly TableSchema JobRequirements = new(
        "JobRequirements",
        RecordSize: 12,
        RecordCount: 22,
        Fields: RequirementFieldNames.Select((name, i) => i % 2 == 0 ? FieldDef.Upper(name, i / 2) : FieldDef.Lower(name, i / 2)).ToList(),
        Source: "FFTPatcher Datatypes/Job/JobLevels.cs (Requirements)")
    {
        RecordName = id => id >= 0 && id < RequirementJobs.Count ? RequirementJobs[id] : null,
    };

    /// <summary>
    /// JP needed for each job level; one record, stored right after the requirements
    /// on PSP. Not found in TIC's executable yet (those bytes are a pointer there),
    /// so it isn't in <see cref="AllTables"/>.
    /// </summary>
    public static readonly TableSchema JobLevels = new(
        "JobLevels", RecordSize: 16, RecordCount: 1,
        Fields: Enumerable.Range(1, 8).Select(level => FieldDef.U16($"Level{level}", (level - 1) * 2)).ToList(),
        Source: "FFTPatcher Datatypes/Job/JobLevels.cs (levels)");
}
