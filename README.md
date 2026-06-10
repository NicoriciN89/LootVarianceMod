# Loot Variance Mod

A MelonLoader mod for **The Long Dark** that makes finding gear more exciting and unpredictable.

## Features

### Condition Variance
Items found in containers spawn with a much wider durability spread — from nearly destroyed to nearly brand new. Configurable min/max range.

### Story Mode Items in Survival
Unique named weapons, tools and clothing from the story episodes (Barb's Rifle, Jeremiah's Knife, Grey Mother's Boots, etc.) can now appear in containers during a Survival run.
- Higher chance inside safes
- Toggle weapons / tools / clothing independently

### Bonus Ammo
When you are low on ammo for a weapon you carry, containers may occasionally produce a few extra rounds as a lucky find. Also checks periodically while you explore.

### Survivor Cache
Rare chance to open a chest or wardrobe and find a hidden stash of survival supplies left by a previous survivor — food, medicine, fire-starting gear, tools, clothing. Every cache has a random mix of 3–5 items in varied condition.
- Separate chance sliders for chests and wardrobes
- Configurable item count range

## Installation

1. Install [MelonLoader](https://melonwiki.xyz/) for The Long Dark
2. Install [ModSettings](https://github.com/STBlade/ModSettings) (required dependency)
3. Drop `LootVarianceMod.dll` into your `Mods/` folder

## Configuration

All options are adjustable in-game via the **Options → Mods → Loot Variance** menu.

## Requirements

- The Long Dark (Steam)
- MelonLoader 0.6+
- ModSettings 2.2+

## Building from Source

```
dotnet build --configuration Release
```

Output: `bin/Release/net6.0/LootVarianceMod.dll`

## Changelog

### v1.1.0
- Added **Survivor Cache** — rare hidden stash inside chests and wardrobes
- Added separate chance sliders for chests vs wardrobes
- Added missing Bonus Ammo settings (enable toggle, chance %, threshold)
- Fixed: bonus ammo threshold and chance were hardcoded, ignoring the settings UI
- Fixed: survivor cache item count undefined behavior when min > max
- Refactored: ammo candidate logic deduplicated

### v1.0.3
- Initial public release
- Condition variance, story items, bonus ammo

## Author

NicoriciN89
