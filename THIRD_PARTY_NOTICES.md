# Third-party notices

## Codex Dream Skin

- Project: https://github.com/Fei-Away/Codex-Dream-Skin
- Interface baseline: v1.5.21
- License: MIT
- Copyright: 2026 Codex Dream Skin Studio contributors

The launcher integrates with a separately installed Codex Dream Skin engine. Its engine, Node runtime, injector, user themes, and local performance patches are not bundled. The startup adapter uses upstream Windows scripts and its result-file protocol; the hidden-document result check in `src/Launch-SavedSkin.ps1` was adapted for this integration from upstream readiness checks. Preserve this notice and the upstream MIT license for those portions. This project does not claim authorship of the upstream skin engine.

Original dependency license:

```text
MIT License

Copyright (c) 2026 Codex Dream Skin Studio contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Windows and OpenAI software

The project uses WPF, Win32, DWM, WMI and Windows PowerShell APIs provided by Windows/.NET Framework; those components are not redistributed. The official OpenAI desktop client is a separate prerequisite and is not redistributed or modified. Product names describe interoperability; this is an unofficial community project.

## Artwork

This repository distributes no photographs, character illustrations, wallpapers, or rendered previews. No character imagery, chat screenshots, account data, credentials, or runtime recordings are bundled.

`scripts/Generate-DefaultArtwork.ps1` generates an original abstract red/blue background locally from gradients, paths and deterministic particles. The build invokes it when a local background is absent. Generated artwork is ignored by Git. The generator and its abstract output are covered by the repository's MIT license. Any image a user supplies locally is outside this repository's distribution.
