# KI Simcity Cargo

A BepInEx 5 plugin that adds existing Nuclear Option vehicles to compatible cargo bays:

- **VL-49:** MSV R9 Stratolance Launcher, Fire Control, Munitions, CRAM, and LADS. Sky Sentry AAA is also available when the MC-260 Chimera mod is loaded.
- **Optional MC-260 Chimera:** AeroSentry SPAAG is added to both front and rear cargo bays when the Chimera mod is loaded.

The game already provides LCV25 x2 and AFV6 AA cargo. MC-260 already provides SLMMR-S3 cargo; this plugin leaves those choices alone.
Cargo above the VL-49's 20 t limit is skipped when the game loads its vehicle definitions. AeroSentry is limited to the MC-260's 45 t front and rear cargo bays.

## Build and install

Run `just build` or `just install`. The latter copies `bin/SimcityVL49Cargo.dll` into the game's `BepInEx/plugins/` directory. Restart the game after installation. Set `NUCLEAR_OPTION_GAME='/path/to/Nuclear Option'` for a nondefault installation. The plugin targets .NET Standard 2.0 with C# 7.3 and requires the installed game assemblies to build.

## Notes

The added cargo looks like an HLT-R truck inside the bay.

Cargo prices use each vehicle definition's `value` plus the stock HLT-R mount's base cost. NOCommander reads the VL-49's cargo choices and should see these additions. Use `just inspect Rearmer` to inspect game assembly types; see the [inspector guide](tools/AssemblyInspector/README.md).
