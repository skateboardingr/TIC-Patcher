"""Builds src/TICPatcher.Core/Resources/names.json from FFTPatcher's PSP name lists.

    python tools/names-from-fftpatcher.py <FFTPatcher checkout> src/TICPatcher.Core/Resources/names.json

FFTPatcher (https://github.com/Glain/FFTPatcher, GPL-3.0) by Joe Davidson and Glain.
"""
import json
import re
import sys
import xml.etree.ElementTree as ET

RESOURCES = "PatcherLib.Resources/Resources"

# name: (file under RESOURCES, element path, id attribute, number base, name attribute or None for the element text)
XML_LISTS = {
    "Jobs": ("PSP/Jobs.xml", "Job", "offset", 16, "name"),
    "SkillSets": ("PSP/SkillSets.xml", "SkillSet", "byte", 16, "name"),
    "Statuses": ("PSP/StatusNames.xml", "Status", "offset", 10, "name"),
    "Abilities": ("PSP/Abilities/Abilities.xml", "Ability", "value", 10, "name"),
    "Items": ("PSP/Items/Items.xml", "Item", "offset", 10, "name"),
    "Effects": ("PSP/Abilities/Effects.xml", "Effect", "value", 16, "name"),
    "Formulas": ("AbilityFormulas.xml", "Ability", "value", 16, None),
    "ShopAvailability": ("PSP/Items/Strings.xml", "ShopAvailabilities/string", "value", 10, "name"),
    "Errands": ("PSP/Errands.xml", "entry", "value", 10, "name"),
    # Battles (ENTD): TIC's battle files are the PSX game's, so its names.
    "Battles": ("PSX-US/EventNames.xml", "Event", "value", 16, "name"),
    "SpriteSets": ("PSX-US/SpriteSets.xml", "Sprite", "byte", 16, "name"),
    "SpecialNames": ("PSX-US/SpecialNames.xml", "SpecialName", "byte", 16, "name"),
}

# name: (C# file, enum name). Members count up from 0; [PSPDescription] or [Description] gives the display name.
ENUM_LISTS = {
    "ItemTypes": ("Datatypes/Items/Item.cs", "ItemSubType"),
    "MenuActions": ("Datatypes/Job/ActionMenu.cs", "MenuAction"),
    "AbilityTypes": ("Datatypes/Abilities/Ability.cs", "AbilityType"),
    "ErrandTypes": ("Datatypes/Propositions/Proposition.cs", "PropositionType"),
    "ErrandStats": ("Datatypes/Propositions/Proposition.cs", "BraveFaithNeutral"),
    "ErrandRewards": ("Datatypes/Propositions/Proposition.cs", "Reward"),
    "ErrandPrereqs": ("Datatypes/Propositions/Proposition.cs", "PrereqType"),
    "ErrandClasses": ("Datatypes/Propositions/AllPropositions.cs", "RandomSuccessClass"),
    "Towns": ("PatcherLib/Datatypes/Shops.cs", "Town"),
    "Months": ("Datatypes/ENTD/Months.cs", "Month"),
    "PrerequisiteJobs": ("Datatypes/ENTD/EventUnit.cs", "PreRequisiteJob"),
    "Facing": ("Datatypes/ENTD/EventUnit.cs", "Facing"),
    "TeamColors": ("Datatypes/ENTD/EventUnit.cs", "TeamColor"),
}


def read_enum(path, enum):
    with open(path, encoding="latin-1") as f:
        source = f.read()
    body = re.search(r"enum\s+" + enum + r"\s*\{(.*?)\}", source, re.S).group(1)
    names, pending = {}, {}
    for line in body.splitlines():
        line = line.split("//")[0].strip()
        for kind, text in re.findall(r"\[(PSPDescription|Description)\(\s*\"([^\"]*)\"\s*\)\]", line):
            pending[kind] = text.strip()
        member = re.match(r"^([A-Za-z_]\w*)\s*(?:=\s*(\w+))?\s*,?$", line)
        if member:
            value = int(member.group(2), 0) if member.group(2) else len(names)
            names[value] = pending.get("PSPDescription") or pending.get("Description") or member.group(1)
            pending = {}
    return names


def main(checkout, out):
    names = {"_source": "Names from FFTPatcher (https://github.com/Glain/FFTPatcher, GPL-3.0) by Joe Davidson and Glain: "
                        "PatcherLib.Resources/Resources (War of the Lions names) and its Datatypes enums. "
                        "Regenerate with tools/names-from-fftpatcher.py."}
    for key, (file, element, attr, base, name_attr) in XML_LISTS.items():
        root = ET.parse(f"{checkout}/{RESOURCES}/{file}").getroot()
        found = {}
        for e in (root.iterfind(element) if "/" in element else root.iter(element)):
            if e.get(attr) is None:
                continue
            text = e.get(name_attr) if name_attr else (e.text or "")
            found[int(e.get(attr), base)] = text.strip()
        names[key] = {str(k): v for k, v in sorted(found.items())}
    for key, (file, enum) in ENUM_LISTS.items():
        found = read_enum(f"{checkout}/{file}", enum)
        names[key] = {str(k): v for k, v in sorted(found.items())}
    with open(out, "w", encoding="utf-8") as f:
        json.dump(names, f, indent=1, ensure_ascii=False)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
