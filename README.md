DNMG - Dot(Net) Matrix Game
====
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/twobrainsgmbh/dnmg/blob/main/LICENSE)
![CI](https://github.com/twobrainsgmbh/dnmg/actions/workflows/ci.yml/badge.svg)

DNMG is a minimalistic, yet functional Game Boy emulator written in C# for educational purposes. With a focus on simplicity, it makes shortcuts and compromises where necessary to provide a dense and understandable implementation. Some features and behaviors may not fully match the original hardware and its quirks, other features like link cable support are missing entirely.

That said, it is passing [`dmg-acid2`](https://github.com/mattcurrie/dmg-acid2), Blargg's [`cpu_instrs`](https://github.com/retrio/gb-test-roms/tree/master/cpu_instrs/individual) and [`instr_timing`](https://github.com/retrio/gb-test-roms/tree/master/instr_timing) testsuites and capable of running the majority of Homebrew Games and even some demoscene productions available.

## How to run it

The emulator core is portable and can be used as a library in other projects.

We provide a simple console application for [Sixel-compatible terminals](https://www.arewesixelyet.com/) (like Windows Terminal) that can be used to run games, but it is not intended to be a full-featured emulator frontend. It is meant to be a starting point for developers who want to build their own emulator frontends or integrate the emulator core into their own projects. It supports Xbox Controller input on Windows.

## Contributing
This project welcomes contributions and suggestions. See [CONTRIBUTING.md](CONTRIBUTING.md) for more information.

## Legal Disclaimer

This project is a clean-room implementation and was developed for educational purposes and software preservation only. It does not contain any copyrighted material, proprietary code, or ROMs owned by Nintendo.

- **Nintendo** and **Game Boy** are registered trademarks of Nintendo Co., Ltd. This project is not affiliated with, authorized, or endorsed by Nintendo in any way.
- To use this emulator, users must provide their own legally obtained game ROMs.
