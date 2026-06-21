# SkyConfig

SkyConfig is a C# application for editing and managing Skylanders that were saved in the .sky format in RPCS3. 

## Running

The exe SkyConfig.exe is prepackaged all in itself so feel free to just run that. But the basic steps for using the app are:
1. Remove the Skylander from the Portal of Power manager in RPCS3
2. Open the .sky file into SkyConfig
3. Edit the values
4. **APPLY** the changes to validate them
5. **SAVE** the file to make it usable in RPCS3. A previous copy of the Skylander can be saved to `<name>.sky.bak`

You can edit nickname, XP, level, gold, play time, hero rank, upgrades, upgrade paths, heroic flags, and hats.

The **Validation** panel shows header checksums. The hex viewer is just there to fill space I didn't know what else to put there.

## Build

You'll need .NET (I use .NET 8), then run:
```powershell
.\build.ps1
```

This will run some tests. To skip the tests use the -SkipTests flag (but that should really only be done when only changing UI stuff)

## Credits and file formatting

SkyConfig's 1,024-byte file handling follows the block behavior in [RPCS3's Skylander portal implementation](https://github.com/RPCS3/rpcs3/blob/master/rpcs3/Emu/Io/Skylander.cpp). Encryption, redundant-area selection, and checksum behavior were cross-checked against [Dolphin's Skylander implementation](https://github.com/dolphin-emu/dolphin/tree/master/Source/Core/Core/IOS/USB/Emulated/Skylanders). The 14-bit upgrade layout was cross-checked against the [Runes format documentation](https://github.com/NefariousTechSupport/Runes/blob/master/Docs/SkylanderFormat.md). The embedded figure catalog merges the names and identifiers maintained by RPCS3 and Dolphin; short upgrade names were generated from [SkyRipper's character data](https://github.com/AnthonyKalampogias/SkyRipper).

## License

SkyConfig is licensed under GPL-2.0-or-later.