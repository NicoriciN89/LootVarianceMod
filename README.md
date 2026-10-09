# Loot Variance Mod

A MelonLoader mod for **The Long Dark** that makes finding gear more exciting and unpredictable.

## Features

### Condition Variance
Items found in containers spawn with a much wider durability spread — from nearly destroyed to nearly brand new. Configurable min/max range.

### Story Mode Items in Survival
Unique named weapons, tools and gear from the story episodes (Barb's Rifle, the Trader's and Curator's Rifles, the Survival Knife, the Spelunker's Lamp, etc.) can now appear in containers during a Survival run.
- Rolled the first time you search a container; higher chance inside safes
- Each story item appears at most once per playthrough, even across saves and reloads
- Per-area limit (default 1) so a single region doesn't hand out the whole set
- Never in trash cans, fridges, stoves, toilets or mailboxes
- Toggle weapons / tools / clothing independently
- **Note:** several story items (Vaughn's Rifle, Fancy/Green Revolver, Cougar Claw Knife, Kerosene Lamp - Spelunkers, Down BedRoll, Manufactured/Bushcraft Bow, Woodwright's Bow) are exclusive to the **Tales From the Far Territory** DLC and will only appear if you own it. Without the DLC, the pool falls back to the items that are available.
- **Not included:** Grey Mother's Boots, Jeremiah's Knife and Astrid's / Will's outfits — the game can't keep them in a Survival save, so they would disappear on reload. The Episode 5 shotgun isn't available in Survival.

### Bonus Ammo
When you are low on ammo for a weapon you carry, containers may occasionally produce a few extra rounds as a lucky find. Extra rounds go straight into your existing stack (rifles, revolvers, bows, shotgun).

### Survivor Cache
Rare chance to open a chest or wardrobe and find a hidden stash of survival supplies left by a previous survivor — mostly food, medicine and matches, sometimes warm clothing, rarely a tool. Every cache has a random mix of 3–5 items in varied condition.
- Separate chance sliders for chests and wardrobes
- Configurable item count range

## Installation

1. Install [MelonLoader](https://melonwiki.xyz/) for The Long Dark
2. Install [ModSettings](https://github.com/STBlade/ModSettings) (required dependency)
3. Drop `LootVarianceMod.dll` into your `Mods/` folder

## Configuration

All options are adjustable in-game via the **Options → Mods → Loot Variance** menu.

The list of story items already found is kept per save in `UserData/LootVarianceMod/<save>.txt`; the mod never modifies your save files.

## Requirements

- The Long Dark (Steam)
- MelonLoader 0.7+ (tested on 0.7.3)
- ModSettings 2.2+

## Building from Source

```
dotnet build --configuration Release -p:GameDir="D:\Games\TheLongDark"
```

Output: `bin/Release/net6.0/LootVarianceMod.dll`

## Changelog

### v1.2.0
- Fixed: story items vanished after saving and reloading — they are now spawned through the game's own `InstantiateGearItem` path (the one saves use to restore gear) and stripped of mission-object data
- Fixed: invisible models on the pickup screen when taking a story item (e.g. Cougar Claw Knife) from a container
- Fixed: survivor caches used item names that don't exist in the game, so caches were filled from the few valid ones (mostly hacksaws). New weighted pool: mostly food, medicine and matches; tools are rare
- Fixed: the mod placed items in the main menu background scene, which used up story items for the session (thanks qqqbbb)
- Fixed: bonus ammo created a separate stack every time — it now merges into the existing stack
- Changed: story items are rolled when you first search a container instead of on area load, and each one appears at most once per playthrough (persisted per save)
- Changed: story items never spawn in trash cans, fridges, stoves, toilets or mailboxes
- Removed: Grey Mother's Boots from the pool (can't be kept in a Survival save)
- Added: "Max Story Items per Area" setting (default 1)
- Added: shotgun support for bonus ammo; survivor cache contents in the log; `lvm_scan` console command

### v1.1.5
- Fixed: story items and survivor cache could spam multiple rolls onto composite world objects (e.g. vehicles, which are made of several sub-containers sharing one name/position) that share a container name and near-identical position
- Fixed: a story item's container key no longer breaks across zone reloads, so the same container isn't treated as "new" and re-rolled every time you re-enter an area
- Changed: each story item now spawns at most once per game session — including after it breaks or is lost, so you can no longer end up with two of the same story item at once. It becomes available again on your next session.

### v1.1.4
- Fixed: story items could fail to spawn on installs without the Tales From the Far Territory DLC — the mod now skips DLC-exclusive items and picks another available one instead of giving up
- Docs: noted which story items require the Tales From the Far Territory DLC

### v1.1.3
- Fixed: duplicate story items and survivor cache items appearing across zone transitions
- Fixed: all story weapons now drop independently — one of each rifle, revolver, and bow
- Fixed: story item can drop again if the previous one was broken or lost
- Removed: Jeremiah's Knife (inaccessible in game files)
- Removed: Accelerant and Flares from survivor cache pool (caused item spam)

### v1.1.2
- Fixed: dead object references accumulated in memory across zone transitions
- Fixed: prefab cache not cleared on new game load
- Optimized: gear prefabs cached after first load — fewer Unity asset lookups per container open
- Optimized: container keys use string interning to reduce allocations

### v1.1.1
- Fixed: spawned weapons/items could remain visible in the main menu scene after exiting a session
- Fixed: bonus ammo could spam dozens of stacks on zone load — now spawns at most once per zone
- Fixed: bonus ammo and story items now go directly into containers/inventory instead of being placed in the world (no more floating items or items clipping into geometry)
- Fixed: story items could appear as duplicates across save/load cycles

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
