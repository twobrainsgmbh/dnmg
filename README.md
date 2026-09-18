DNMG - Dot(Net) Matrix Game
====
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/twobrainsgmbh/dnmg/blob/main/LICENSE)
![CI](https://github.com/twobrainsgmbh/dnmg/actions/workflows/ci.yml/badge.svg)

DNMG is a minimalistic, yet functional Game Boy emulator written in C# for educational purposes. With a focus on simplicity, it makes shortcuts and compromises where necessary to provide a dense and understandable implementation. Some features and behaviors may not fully match the original hardware, other features like sound emulation and link cable support are missing entirely.

That said, it is passing [`dmg-acid2`](https://github.com/mattcurrie/dmg-acid2), Blargg's [`cpu_instrs`](https://github.com/retrio/gb-test-roms/tree/master/cpu_instrs/individual) and [`instr_timing`](https://github.com/retrio/gb-test-roms/tree/master/instr_timing) testsuites and capable of running the majority of Homebrew Games and even some demoscene productions available.

Xbox Controller support is available on Windows. On any supported .NET platform, any [Sixel-compatible terminal](https://www.arewesixelyet.com/) (like Windows Terminal) can be used to display the Game Boy screen in a terminal window and to interact with the emulator using a keyboard.

## Contributing
This project welcomes contributions and suggestions.  Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution.

## Legal Disclaimer

This project is a clean-room implementation and was developed for educational purposes and software preservation only. It does not contain any copyrighted material, proprietary code, or ROMs owned by Nintendo.

- **Nintendo** and **Game Boy** are registered trademarks of Nintendo Co., Ltd. This project is not affiliated with, authorized, or endorsed by Nintendo in any way.
- To use this emulator, users must provide their own legally obtained game ROMs.
