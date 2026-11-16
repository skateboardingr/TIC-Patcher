"""Builds src/TICPatcher.Core/Resources/names.json from FFTPatcher's PSP name lists.

    python tools/names-from-fftpatcher.py <FFTPatcher checkout> src/TICPatcher.Core/Resources/names.json

FFTPatcher (https://github.com/Glain/FFTPatcher, GPL-3.0) by Joe Davidson and Glain.
"""
import json
import sys
import xml.etree.ElementTree as ET

LISTS = {  # name: (file under PatcherLib.Resources/Resources/PSP, element, id attribute, number base)
    "Jobs": ("Jobs.xml", "Job", "offset", 16),
    "SkillSets": ("SkillSets.xml", "SkillSet", "byte", 16),
    "Statuses": ("StatusNames.xml", "Status", "offset", 10),
    "Abilities": ("Abilities/Abilities.xml", "Ability", "value", 10),
    "Items": ("Items/Items.xml", "Item", "offset", 10),
}


def main(checkout, out):
    names = {"_source": "Names from FFTPatcher (https://github.com/Glain/FFTPatcher, GPL-3.0) by Joe Davidson and Glain: "
                        "PatcherLib.Resources/Resources/PSP (War of the Lions names). Regenerate with tools/names-from-fftpatcher.py."}
    for key, (file, element, attr, base) in LISTS.items():
        root = ET.parse(f"{checkout}/PatcherLib.Resources/Resources/PSP/{file}").getroot()
        found = {int(e.get(attr), base): e.get("name").strip() for e in root.iter(element)}
        names[key] = {str(k): v for k, v in sorted(found.items())}
    with open(out, "w", encoding="utf-8") as f:
        json.dump(names, f, indent=1, ensure_ascii=False)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
