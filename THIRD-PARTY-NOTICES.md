# Third-party notices

TIC Patcher is licensed under GPL-3.0-or-later (see [LICENSE](LICENSE)). It builds on the works below. Include this file with any binary release.

## FFTPatcher

- https://github.com/Glain/FFTPatcher, by Joe Davidson (melonhead), Glain and contributors.
- License: GNU General Public License, version 3 or (at your option) any later version.
- Used: table layouts, field and flag names (each schema in `src/TICPatcher.Core/Tables/Schemas` names its FFTPatcher source file), and the War of the Lions name lists in `src/TICPatcher.Core/Resources/names.json` (made with `tools/names-from-fftpatcher.py`).
- Version used: commit `26d45cc8c019194625f078398e0c6ef6b09c1a78` (2026-07-07). No AI-use restrictions were present in that version.

## Reloaded-II

- https://github.com/Reloaded-Project/Reloaded-II, by Sewer56 and contributors.
- License: GNU General Public License, version 3.
- Used: TIC Loader is a Reloaded-II mod and builds against the `Reloaded.Mod.Interfaces` package (GPL-3.0). The package is supplied by Reloaded-II at run time and isn't redistributed.

## Avalonia

- https://github.com/AvaloniaUI/Avalonia
- License: MIT. Copyright (c) AvaloniaUI OÜ.
- Used: the TIC Patcher editor's user interface (packages Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter, Avalonia.Headless, Avalonia.Skia).

## Iced

- https://github.com/icedland/iced
- License: MIT. Copyright (C) 2018-present iced project and contributors.
- Used: `tools/Disasm`, a research tool (not part of TIC Patcher or TIC Loader releases).

## xUnit.net

- https://github.com/xunit/xunit
- License: Apache-2.0.
- Used: the test project only.

---

The MIT License (applies to Avalonia and Iced as noted above):

> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
