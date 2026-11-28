# UIB v10 (FFT:TIC) – job_tree.uib notes

Tool: `uib.py` (dump / jobs / verify / move / demo). Run it on a layout extracted from the game, e.g.
`TICScan --pac-extract <0008.pac> ffto/unit/ffto_job_tree.uib job_tree.uib`. No game data is kept here.

## Global rules (verified on the whole file)
- LE, 4-byte aligned up to the string table; string table (NUL-terminated) is last (0x2E360..EOF).
- Every offset is a signed int32 relative to the START OF THE STRUCT holding it.
  All offsets in vanilla are positive (forward). No backward refs exist.
- No file-size / checksum field.

## Layout
0x00 "UIB\0", 0x04 version 10, 0x18 = 0x2C (absolute offset of root struct)
Root @0x2C: +0 off->resources (0x58), +4 off->component table (0x284), +8 count (23)
Resources @0x58: +0 off tex list (0x8C), +4 n=7, +8 off ui list (0x214), +C n=2, +10 off end (=0x284)
  lists = n x int32 offsets (relative to list start) to 0x34-byte records:
  {+0 type (1 tex, 2 uib), +4 strref name, +8 off sub (=0x2C), sub@+2C {u32 0x3FF/0x403, +4 strref path}}
Component table @0x284: 23 x 0x40 {+0 strref name, +4 w, +8 h, +C off A, +10 off B(root list), +14 nroots,
  +18 off C(anims), +1C nanims, +24 off D}; bodies are contiguous A,B,C,D(8 bytes) in table order.
  A = settings (0x1C0; 0x1C8 for Widget) incl. state-name list (+4 off, +8 count) e.g. "HomePosition".
Elements (B): header common to all types
  +0 type (1 Layer,2 Image,3 Text,4 NineGrid,6 Rect,9 Collision,10 Instance)  +4 strref name
  +8 x, +C y (int32, top-left in parent coords)  +10 rotation (float rad)  +14/+18 scale (float)
  +1C/+20 pivot (int, normally w/2,h/2)  +44 w, +48 h  +4C off common block, +50 1
  fixed sizes: Image 0x1B0, Text 0x130, NineGrid 0x1A4, Rect 0x110, Collision 0xF0, Instance 0x17C
  Layer: +90 off child list, +94 child count; list = n x int32 (relative to list start);
         children stored contiguously right after the list (subtrees nested inline).
  Instance: +70 strref component(class) name, +74 off (0x148) to 0x34 resource-ref (names "" = local),
            +108 = 0x14 constant (not a component index).
Animations (C): n x 0x60 headers {+0 strref name, +4 0x80000000, body@+8: {+0 3, +4 off keys, +8 nkeys,
  +C off 5x{idx,off,cnt} table, +10 5, +14 off track list, +18 ntracks, +1C length}} ;
  tracks: 0x2C {0xFA1, ..., +28 strref element name} (one per element + one 0xFA2 "self");
  keys: 0x20 {+8 property id (0x1391/0x138C/0x13A4=play sub-anim), +14 track index, +1C off data}.

## JobTree
LayerRoot @0x23BD8 (offset 0,32): list @0x23CD0, count @0x23C6C = 21 (20 UnitJob instances + LayerBranches)
LayerBranches @0x25AD4: list @0x25BCC, count @0x25B68 = 32 (30 lines, 2 Lv02 forks, Cross, Cross2)
Nodes tied to jobs ONLY by element name (Squire, ..., White, Black, Time, ...). Order != generaljob order.

## Insert rule
Insert N bytes at P (element boundary): for every offset field whose struct base < P and target >= P add N.
= all string refs located before P + component-table C/D (and later comps) + parent child-list entries
  after P + root-list entries after P. Append new strings at EOF (no shift).
