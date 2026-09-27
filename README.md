# KI Simcity Cargo

A BepInEx 5 plugin that adds existing Nuclear Option vehicles to compatible cargo bays:

- **VL-49:** every eligible encyclopedia vehicle is offered automatically. Vehicles up to 12 t are added to the front and rear cargo bays; vehicles from 12 t up to 24 t are added to the full cargo bay. Vehicles already offered by a base game or mod cargo mount in that bay are left alone.
- **UH-90 Ibis:** every eligible encyclopedia vehicle is offered automatically. Vehicles up to 4 t are added to the front and rear cargo bays; vehicles from 4 t up to 8 t are added to the full cargo bay. Vehicles already offered by a base game or mod cargo mount in that bay are left alone.
- **Optional MC-260 Chimera:** every eligible encyclopedia vehicle is offered automatically. Vehicles under 45 t are added to both front and rear cargo bays; vehicles from 45 t up to 90 t are added to the mission bay. Vehicles already offered by a base game or mod cargo mount in that bay are left alone. The mission bay also offers a curated **16 Hexhounds and a Dream** loadout that drops 8 Hexhound GMGs and 8 Hexhound SAMs 0.6 s apart, behind the aircraft.

The game already provides LCV25 x2 and AFV6 AA cargo. MC-260 already provides SLMMR-S3 cargo; this plugin leaves those choices alone.
Vehicles above the VL-49's 24 t limit, the Ibis's 8 t limit, and the MC-260's 90 t mission bay limit are skipped.

Tall cargo (configurable, default above 3.6 m) is spawned a configurable distance ahead of the aircraft when deployed so it does not clip into the bay and get stuck. Both values are exposed under `[Cargo deployment]` in the BepInEx config.

The `[Cargo]` config option `VehicleSet` selects which logistics vehicle family is generated: `HLT` (default), `MSV`, or `Both`. `HLT` hides the matching MSV series and vice versa; `Both` offers every vehicle.

The `[Cargo]` config option `VehicleNameDenylist` (default `hypersonic,ballistic,nuclear`) hides any vehicle whose name contains one of the comma-separated words, case-insensitively.

## Build and install

Run `just build` or `just install`. The latter copies `bin/SimcityVL49Cargo.dll` into the game's `BepInEx/plugins/` directory. Restart the game after installation. Set `NUCLEAR_OPTION_GAME='/path/to/Nuclear Option'` for a nondefault installation. The plugin targets .NET Standard 2.0 with C# 7.3 and requires the installed game assemblies to build.

## Notes

The added cargo uses each bay's stock mount as a temporary in-bay model.

Cargo prices use each vehicle definition's `value` plus the stock mount's base cost. NOCommander reads the cargo choices and should see these additions. Use `just inspect Rearmer` to inspect game assembly types; see the [inspector guide](tools/AssemblyInspector/README.md).
