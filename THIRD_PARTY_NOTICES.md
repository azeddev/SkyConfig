# Third-Party Notices

SkyConfig includes data and behavior derived from the following GPL-2.0-or-later projects:

- Dolphin Emulator, Skylander implementation and figure catalog: <https://github.com/dolphin-emu/dolphin/tree/master/Source/Core/Core/IOS/USB/Emulated/Skylanders>
- RPCS3, Skylander portal implementation and figure catalog: <https://github.com/RPCS3/rpcs3/blob/master/rpcs3/rpcs3qt/skylander_dialog.cpp>

The embedded catalog in `src/SkyConfig.Core/Data/skylanders.tsv` records its provenance in its header. SkyConfig is distributed under GPL-2.0-or-later to remain compatible with these sources.

RPCS3-only IDs in the known Imaginators Sensei range (601-631) are classified as standard character figures by SkyConfig; the upstream RPCS3 catalog provides names and IDs but no type metadata for those entries.

The upgrade bit layout was researched against the Runes Skylander format documentation:

- Runes format documentation: <https://github.com/NefariousTechSupport/Runes/blob/master/Docs/SkylanderFormat.md>

The short upgrade and path names in `src/SkyConfig.Core/Data/upgrades.tsv` were generated from SkyRipper's character JSON, which records data collected from the Skylanders Wiki. SkyConfig does not redistribute descriptions, images, or other game assets from that dataset.

- SkyRipper: <https://github.com/AnthonyKalampogias/SkyRipper>
- Skylanders Wiki: <https://skylanders.fandom.com/>

Skylanders names and related trademarks belong to their respective owners. No game assets are distributed with SkyConfig.
