using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using Il2CppTLD.Gear;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace LootVarianceMod
{
    [HarmonyPatch]
    internal static class Patches
    {
        // ── Story item lists ──────────────────────────────────────────────────────
        // Names that don't exist in the current install (DLC not owned, episode not installed)
        // or that the game can't instantiate by name are skipped at runtime (see lvm_scan).
        // Not included: GreyMotherBoots, JeremiahKnife, Astrid*/Will* outfits — the game can't
        // instantiate them by name in Survival, which is also how saves restore gear, so they
        // vanished on reload.

        private static readonly string[] StoryWeapons =
        {
            "GEAR_Rifle_Trader",
            "GEAR_Rifle_Barbs",
            "GEAR_Rifle_Curators",
            "GEAR_Rifle_Vaughns",
            "GEAR_RevolverStubNosed",
            "GEAR_RevolverFancy",
            "GEAR_RevolverGreen",
            "GEAR_Bow_Woodwrights",
            "GEAR_Bow_Manufactured",
            "GEAR_Bow_Bushcraft",
            "GEAR_Shotgun",
        };

        private static readonly string[] StoryTools =
        {
            "GEAR_SurvivalKnife",
            "GEAR_CougarClawKnife",
            "GEAR_KeroseneLamp_Spelunkers",
            "GEAR_Flashlight_LongLasting",
            "GEAR_Flashlight_CrankLamp",
        };

        private static readonly string[] StoryClothing =
        {
            "GEAR_BedRoll_Down",
        };

        // Containers where a story item would break immersion (trash cans, appliances, mailboxes,
        // first aid kits, cash registers...).
        private static readonly string[] StoryExcludedKeywords =
        {
            "trash", "garbage", "dumpster", "waste", "recycl", "litter",
            "toilet", "fridge", "freezer", "oven", "stove", "microwave", "mailbox",
            "firstaid", "aidkit", "medkit", "medicine", "cashregister",
        };

        // Small containers can hold a knife or a revolver, but not a rifle, bow or bedroll.
        private static readonly string[] SmallContainerKeywords =
        {
            "glovebox", "briefcase", "lunchbox", "toolbox", "drawer",
        };

        private static readonly string[] BulkyStoryItems =
        {
            "GEAR_Rifle_Trader", "GEAR_Rifle_Barbs", "GEAR_Rifle_Curators", "GEAR_Rifle_Vaughns",
            "GEAR_Bow_Woodwrights", "GEAR_Bow_Manufactured", "GEAR_Bow_Bushcraft",
            "GEAR_Shotgun", "GEAR_BedRoll_Down",
        };

        // ── Survivor Cache ────────────────────────────────────────────────────────

        private static readonly string[] ChestKeywords    = { "chest", "trunk", "footlocker" };
        private static readonly string[] WardrobeKeywords = { "wardrobe", "dresser", "closet", "cabinet", "drawer" };

        // Safes belong to the story-item system; cash registers match "drawer" but aren't a stash.
        private static readonly string[] CacheExcludedKeywords = { "safe", "cashregister" };

        private static bool NameContainsAny(string name, string[] keywords)
        {
            foreach (string kw in keywords)
                if (name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // Returns 0 = not a cache container, 1 = chest, 2 = wardrobe
        private static int CacheContainerType(Container c)
        {
            string name = c.gameObject.name;
            if (NameContainsAny(name, ChestKeywords))    return 1;
            if (NameContainsAny(name, WardrobeKeywords)) return 2;
            return 0;
        }

        private static bool IsStoryItemContainer(Container c) =>
            !NameContainsAny(c.gameObject.name, StoryExcludedKeywords);

        // The main menu background is a real scene with containers; nothing placed there is ever reachable.
        private static bool IsInMainMenu()
        {
            string activeScene = GameManager.m_ActiveScene;
            return (activeScene != null && GameManager.MatchesMainMenuSceneName(activeScene))
                || InterfaceManager.IsMainMenuEnabled();
        }

        // Varied survival supplies a previous survivor might have stashed, weighted so common
        // consumables are frequent and tools (whetstone, hacksaw) are rare. Names are checked
        // against the game's catalog — the 1.1.x list had many that didn't exist, so caches
        // were filled from the few valid ones (mostly hacksaws).
        private static readonly (string Name, int Weight)[] SurvivorCachePool =
        {
            // Food
            ("GEAR_CannedBeans", 10),
            ("GEAR_PinnacleCanPeaches", 10),
            ("GEAR_CannedSardines", 10),
            ("GEAR_DogFood", 8),
            ("GEAR_BeefJerky", 8),
            ("GEAR_EnergyBar", 10),
            ("GEAR_GranolaBar", 10),
            ("GEAR_Crackers", 10),
            ("GEAR_CandyBar", 8),
            ("GEAR_PeanutButter", 6),
            // Medical
            ("GEAR_BottlePainKillers", 6),
            ("GEAR_BottleAntibiotics", 5),
            ("GEAR_HeavyBandage", 8),
            ("GEAR_WaterPurificationTablets", 5),
            // Fire / light
            ("GEAR_PackMatches", 8),
            ("GEAR_WoodMatches", 8),
            // Clothing
            ("GEAR_WoolSocks", 4),
            ("GEAR_Mittens", 4),
            ("GEAR_Toque", 4),
            // Tools / repair
            ("GEAR_SewingKit", 3),
            ("GEAR_SharpeningStone", 2),
            ("GEAR_Hacksaw", 1),
        };

        private static void TryAddSurvivorCache(Container container)
        {
            if (!Settings.enableSurvivorCache) return;
            if (NameContainsAny(container.gameObject.name, CacheExcludedKeywords)) return;

            int ctype = CacheContainerType(container);
            if (ctype == 0) return;

            float chance = ctype == 1
                ? Settings.survivorCacheChestChancePct   / 100f
                : Settings.survivorCacheWardrobeChancePct / 100f;
            if (!Utils.RollChance(chance)) return;

            int cacheMin = Settings.survivorCacheMinItems;
            int cacheMax = Math.Max(Settings.survivorCacheMaxItems, cacheMin);
            int target   = UnityEngine.Random.Range(cacheMin, cacheMax + 1);

            // Weighted draw without replacement; unavailable prefabs are dropped and don't count.
            var pool = new List<(string Name, int Weight)>(SurvivorCachePool);
            var added = new List<string>();

            while (added.Count < target && pool.Count > 0)
            {
                int index = PickWeightedIndex(pool);
                string gearName = pool[index].Name;
                pool.RemoveAt(index);

                try
                {
                    GearItem gi = SpawnGear(gearName);
                    if (gi == null) continue;

                    gi.transform.position = container.transform.position;
                    gi.RollGearCondition(GearStartCondition.Random);
                    container.AddGear(gi);
                    spawnedObjects.Add(gi.gameObject);
                    added.Add(gearName);
                }
                catch (Exception e)
                {
                    Core.Log.Warning($"[LVM] Cache item failed ({gearName}): {e.Message}");
                }
            }

            if (added.Count > 0)
                Core.Log.Msg($"{Loc.SpawnedCache(container.name, added.Count)}: {string.Join(", ", added)}");
        }

        private static int PickWeightedIndex(List<(string Name, int Weight)> pool)
        {
            int total = 0;
            foreach (var entry in pool) total += entry.Weight;

            int roll = UnityEngine.Random.Range(0, total);
            for (int i = 0; i < pool.Count; i++)
            {
                roll -= pool[i].Weight;
                if (roll < 0) return i;
            }
            return pool.Count - 1;
        }

        // Prefab cache — LoadGearItemPrefab is a Unity asset lookup; avoid repeating it.
        private static readonly Dictionary<string, GearItem> prefabCache = new();

        private static GearItem LoadPrefab(string gearName)
        {
            if (prefabCache.TryGetValue(gearName, out var cached)) return cached;
            var prefab = GearItem.LoadGearItemPrefab(gearName);
            prefabCache[gearName] = prefab; // null is valid (prefab doesn't exist)
            return prefab;
        }

        // Spawns gear through the game's own instantiation path — the same one used when a save
        // is loaded — so Addressables-backed (DLC / story) meshes load and the item survives reloads.
        // A plain Object.Instantiate of the prefab worked for some items but not others.
        private static GearItem SpawnGear(string gearName)
        {
            if (LoadPrefab(gearName) == null) return null;

            GearItem gi = GearItem.InstantiateGearItem(gearName);
            if (gi == null)
            {
                // Can't be created by name in this game mode, so it couldn't be restored from a save either.
                prefabCache[gearName] = null;
                Core.Log.Msg(Loc.ItemNotFound(gearName));
                return null;
            }

            gi.gameObject.name = gearName;
            StripMissionData(gi);
            DEBUG_RecordSpawn(gearName);
            return gi;
        }

        // Story prefabs carry a MissionObjectIdentifier: outside of its mission the save system
        // treats the item as mission-managed and drops it on reload. Turn it into regular gear.
        private static void StripMissionData(GearItem gi)
        {
            MissionObjectIdentifier mission = gi.m_MissionObject;
            if (mission == null) mission = gi.GetComponent<MissionObjectIdentifier>();
            if (mission != null)
            {
                mission.m_SkipUnregister = true;
                UnityEngine.Object.DestroyImmediate(mission);
            }
            gi.m_MissionObject = null;
            gi.m_DisableSerialization = false;
        }

        // Containers that have already been processed this zone.
        // Key: root object name + container name + rounded position. Must be content-based,
        // not instance-based (GetInstanceID/transform.root id): open-world zones like
        // MountainTownRegion tear down and re-instantiate every GameObject on each
        // enter/exit of an interior, so the *same* container gets a new instance ID every
        // time — an instance-based key would treat it as new and re-roll loot on every visit.
        // Rooting by the parent object's name (not just the container's own name) collapses
        // composite objects like cars, whose several sub-Containers (glovebox, trunk, bags)
        // all share one parent name and near-identical position, into a single key —
        // fixing repeated cache/item spam on a single vehicle without needing instance IDs.
        private static readonly HashSet<string> handledContainers = new();

        // Same idea for story-item rolls, which happen on first open rather than on populate.
        private static readonly HashSet<string> handledStoryContainers = new();

        private static string ContainerKey(Container c)
        {
            var p = c.transform.position;
            string rootName = c.transform.root.name;
            return string.Intern($"{rootName}/{c.gameObject.name}@{p.x:F0},{p.y:F0},{p.z:F0}");
        }

        // Story items already handed out in the current save. Persisted per save slot in
        // UserData/LootVarianceMod/<save>.txt so each story item appears at most once per
        // playthrough, not just per session (see the SaveGameSystem patches below).
        private static readonly HashSet<string> spawnedInSave = new();

        // The save the in-memory list belongs to. The game calls RestoreGlobalData on every scene
        // transition (reading from the 'autosave' slot) and for the last save while in the main menu,
        // so the list is keyed by SaveGameSystem.m_CurrentSaveName: the same save merges the file in,
        // any other save name starts a fresh list.
        private static string _spawnedListSaveName;

        // Set when a new Survival game starts. Until that game is first saved, m_CurrentSaveName can
        // still hold the previous save's name, so the list is adopted by the next real save name instead.
        private static bool _adoptNextSaveName;
        private static string _previousSaveName;

        // False after returning to the main menu until a save is restored or a new game's save name
        // is adopted; a container opened in that state can only belong to a new game.
        private static bool _restoredSinceMenu;

        // Containers are populated in bulk when a scene is first entered, so a per-container chance
        // alone could hand out most of the pool in one region. Story items are capped per scene.
        private static readonly Dictionary<string, int> storyItemsPerScene = new();

        // All GameObjects spawned by this mod — destroyed on exit to main menu
        private static readonly List<GameObject> spawnedObjects = new();

        // Debug: counts every successful spawn by item name, for the whole session
        // (survives zone travel, cleared only on new game). Used to diagnose item spam.
        private static readonly Dictionary<string, int> DEBUG_spawnCounts = new();

        private static void DEBUG_RecordSpawn(string gearName)
        {
            DEBUG_spawnCounts.TryGetValue(gearName, out int n);
            DEBUG_spawnCounts[gearName] = n + 1;
        }

        // Bonus ammo fires at most once per zone load
        private static bool _ammoSpawnedThisZone = false;

        // "All story items owned" is logged once per session instead of once per container.
        private static bool _loggedAllOwned = false;

        // ── Condition Variance ────────────────────────────────────────────────────

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GearItem), "RollGearCondition")]
        private static void GearItem_RollGearCondition(GearItem __instance)
        {
            RerollCondition(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GearItem), "RollGearConditionAndWeight")]
        private static void GearItem_RollGearConditionAndWeight(GearItem __instance)
        {
            RerollCondition(__instance);
        }

        // Items placed in the world (floors, shelves, tables) get their condition from the seeded
        // roll, not from the container loot path above.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GearItem), "RollSeededGearConditionAndWeight")]
        private static void GearItem_RollSeededGearConditionAndWeight(GearItem __instance)
        {
            RerollCondition(__instance);
        }

        private static void RerollCondition(GearItem gi)
        {
            if (!Settings.enableConditionVariance) return;
            if (gi == null) return;

            // Whitelist: only items that physically wear out get condition variance.
            // Everything else (food, ammo, books, currency, tinder, etc.) is excluded.
            bool isDurable = gi.m_ClothingItem != null
                          || gi.m_GunItem != null
                          || gi.m_BowItem != null
                          || gi.m_ToolsItem != null
                          || gi.m_KeroseneLampItem != null
                          || gi.m_FlashlightItem != null
                          || gi.m_BearSpearItem != null;
            if (!isDurable) return;

            float min = Settings.minConditionPct / 100f;
            float max = Settings.maxConditionPct / 100f;
            if (min >= max) return;
            float norm = UnityEngine.Random.Range(min, max);
            gi.SetNormalizedHP(norm, false);
        }

        // ── Container loot ────────────────────────────────────────────────────────

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Container), nameof(Container.PopulateWithRandomGear))]
        private static void Container_PopulateWithRandomGear(Container __instance)
        {
            if (__instance == null || IsInMainMenu()) return;

            string key = ContainerKey(__instance);
            if (handledContainers.Contains(key)) return;
            handledContainers.Add(key);

            TryAddSurvivorCache(__instance);
        }

        // ── Story Items in Containers ─────────────────────────────────────────────
        // Rolled when the player first opens a container, not in PopulateWithRandomGear: the game
        // repopulates every container on scene load and then overwrites visited ones from the save,
        // so rolling there "handed out" items the player never got to see.

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Container), nameof(Container.BeginContainerOpen))]
        private static void Container_BeginContainerOpen(Container __instance)
        {
            if (__instance == null || __instance.IsInspected()) return;
            if (!handledStoryContainers.Add(ContainerKey(__instance))) return;

            // Bonus ammo is checked here rather than on scene load: while a scene is loading the
            // inventory is still being restored, so the ammo count came out wrong.
            TryAddBonusAmmo(__instance);
            if (!Settings.enableStoryItems) return;

            // Regions are built from several additive sub-scenes; count against the active (main) scene.
            string scene = GameManager.m_ActiveScene ?? __instance.gameObject.scene.name ?? "";
            storyItemsPerScene.TryGetValue(scene, out int itemsInScene);
            if (itemsInScene >= Settings.maxStoryItemsPerZone) return;
            if (!IsStoryItemContainer(__instance)) return;

            bool isSafe = __instance.gameObject.name.IndexOf("safe", StringComparison.OrdinalIgnoreCase) >= 0;
            float chance = isSafe
                ? Settings.storyItemSafeChancePct / 100f
                : Settings.storyItemChancePct / 100f;
            if (!Utils.RollChance(chance)) return;

            // An item that turns out not to be spawnable is marked unavailable, so this loop ends.
            bool smallContainer = NameContainsAny(__instance.gameObject.name, SmallContainerKeywords);
            string itemName;
            while ((itemName = PickAvailableStoryItem(smallContainer)) != null)
            {
                if (TryAddStoryItemToContainer(__instance, itemName, isSafe))
                {
                    storyItemsPerScene[scene] = itemsInScene + 1;
                    break;
                }
            }
        }

        // Story items added to a container get disabled again during inspect (unlike the container's
        // own loot), so they'd sit in front of the inspect camera invisible. Keep the item enabled.
        private static GearItem _inspectedGear;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.EnterInspectGearMode),
            new Type[] { typeof(GearItem), typeof(Container), typeof(IceFishingHole), typeof(Harvestable), typeof(CookingPotItem) })]
        private static void PlayerManager_EnterInspectGearMode(GearItem __0)
        {
            _inspectedGear = __0;
            KeepInspectedGearActive();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.UpdateInspectGear))]
        private static void PlayerManager_UpdateInspectGear()
        {
            KeepInspectedGearActive();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.ExitInspectGearMode))]
        private static void PlayerManager_ExitInspectGearMode()
        {
            _inspectedGear = null;
        }

        private static void KeepInspectedGearActive()
        {
            if (_inspectedGear != null && !_inspectedGear.gameObject.activeSelf)
                _inspectedGear.gameObject.SetActive(true);
        }

        // ── Bonus Ammo ────────────────────────────────────────────────────────────

        // All rifle/revolver/bow/shotgun gear names — used to detect what weapons the player carries.
        private static readonly string[] RifleGearNames    = { "GEAR_Rifle", "GEAR_Rifle_Trader", "GEAR_Rifle_Barbs", "GEAR_Rifle_Curators", "GEAR_Rifle_Vaughns" };
        private static readonly string[] RevolverGearNames = { "GEAR_Revolver", "GEAR_RevolverStubNosed", "GEAR_RevolverFancy", "GEAR_RevolverGreen" };
        private static readonly string[] BowGearNames      = { "GEAR_Bow", "GEAR_Bow_Woodwrights", "GEAR_Bow_Manufactured", "GEAR_Bow_Bushcraft" };
        private static readonly string[] ShotgunGearNames  = { "GEAR_Shotgun" };

        // Adds ammo directly to the player's inventory — no world spawn, no geometry issues.
        private static void GiveAmmoToPlayer(string gearName, int amount)
        {
            try
            {
                GearItem gi = SpawnGear(gearName);
                if (gi == null) { Core.Log.Warning($"[LVM] Ammo prefab not found: {gearName}"); return; }

                if (gi.m_StackableItem != null)
                    gi.m_StackableItem.m_Units = amount;
                else
                    gi.RollGearCondition(GearStartCondition.Random);

                // Same path as picking an item up: merges into the existing stack and shows the HUD notification.
                GameManager.GetPlayerManagerComponent().AddItemToPlayerInventory(gi, false, true);
                Core.Log.Msg($"[LVM] Added ammo to inventory: {gearName} x{amount} (now {CountUnits(GameManager.GetInventoryComponent(), gearName)})");
            }
            catch (Exception e)
            {
                Core.Log.Warning($"[LVM] Bonus ammo failed ({gearName}): {e.Message}");
            }
        }

        // Shared: build list of ammo types the player is low on.
        private static List<string> CollectLowAmmoCandidates()
        {
            var inventory = GameManager.GetInventoryComponent();
            if (inventory == null) return null;

            int thr = Settings.bonusAmmoThreshold;
            var candidates = new List<string>();
            bool HasAny(string[] names) { foreach (string n in names) if (inventory.GearInInventory(n, 1)) return true; return false; }

            if (HasAny(RifleGearNames)    && CountUnits(inventory, "GEAR_RifleAmmoSingle")    < thr) candidates.Add("GEAR_RifleAmmoSingle");
            if (HasAny(RevolverGearNames) && CountUnits(inventory, "GEAR_RevolverAmmoSingle") < thr) candidates.Add("GEAR_RevolverAmmoSingle");
            if (HasAny(BowGearNames)      && CountUnits(inventory, "GEAR_Arrow")              < thr) candidates.Add("GEAR_Arrow");
            if (HasAny(ShotgunGearNames)  && CountUnits(inventory, "GEAR_ShotgunAmmoSingle")  < thr) candidates.Add("GEAR_ShotgunAmmoSingle");
            return candidates;
        }

        // Total rounds, counted straight from the inventory list. Loose rounds are stored as an ammo
        // box (GEAR_RifleAmmoSingle -> GEAR_RifleAmmoBox with N units), so both names count. Checking
        // only the single-round name made the mod hand out ammo every couple of minutes.
        private static int CountUnits(Inventory inventory, string gearName)
        {
            string boxName = gearName.EndsWith("AmmoSingle", StringComparison.Ordinal)
                ? gearName.Substring(0, gearName.Length - "Single".Length) + "Box"
                : null;

            int total = 0;
            foreach (GearItemObject entry in inventory.m_Items)
            {
                GearItem gi = entry?.m_GearItem;
                if (gi == null) continue;
                if (!IsGearNamed(gi, entry.m_GearItemName, gearName)
                    && (boxName == null || !IsGearNamed(gi, entry.m_GearItemName, boxName))) continue;

                total += gi.m_StackableItem != null ? gi.m_StackableItem.m_Units : 1;
            }
            return total;
        }

        private static bool IsGearNamed(GearItem gi, string listedName, string gearName)
        {
            if (string.Equals(listedName, gearName, StringComparison.OrdinalIgnoreCase)) return true;

            string name = gi.gameObject.name;
            int clone = name.IndexOf("(Clone)", StringComparison.Ordinal);
            if (clone >= 0) name = name.Substring(0, clone);
            return string.Equals(name.Trim(), gearName, StringComparison.OrdinalIgnoreCase);
        }

        private static void TryAddBonusAmmo(Container container)
        {
            if (!Settings.enableBonusAmmo) return;
            if (_ammoSpawnedThisZone) return;

            var candidates = CollectLowAmmoCandidates();
            if (candidates == null || candidates.Count == 0) return;
            if (!Utils.RollChance(Settings.bonusAmmoChancePct / 100f)) return;

            string chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            int amount = UnityEngine.Random.Range(5, 8);
            GiveAmmoToPlayer(chosen, amount);
            _ammoSpawnedThisZone = true;
        }

        // Periodic check (called every ~2 min): give ammo directly to player if they're low.
        internal static void TryPeriodicBonusAmmo()
        {
            if (!Settings.enableBonusAmmo) return;
            if (GameManager.GetPlayerTransform() == null || IsInMainMenu()) return;

            var candidates = CollectLowAmmoCandidates();
            if (candidates == null || candidates.Count == 0) return;
            if (!Utils.RollChance(Settings.bonusAmmoChancePct / 100f)) return;

            string chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            int amount = UnityEngine.Random.Range(5, 8);
            GiveAmmoToPlayer(chosen, amount);
        }

        // ── Story item selection ──────────────────────────────────────────────────

        private static void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // Returns a story item the player doesn't already own and that hasn't been handed out
        // in this save, or null if all eligible items are accounted for. Picks a random enabled
        // category first, then an item inside it, so no category crowds out the others.
        // Small containers (drawers, gloveboxes...) never get rifles, bows or the bedroll.
        private static string PickAvailableStoryItem(bool smallContainer = false)
        {
            if (!_restoredSinceMenu && !_adoptNextSaveName)
                BeginNewGameList("container opened before any save was loaded", _spawnedListSaveName);
            if (!_adoptNextSaveName)
                EnsureSpawnedListFor(SaveGameSystem.m_CurrentSaveName);

            var categories = new List<string[]>();
            if (Settings.includeStoryWeapons)  categories.Add(StoryWeapons);
            if (Settings.includeStoryTools)    categories.Add(StoryTools);
            if (Settings.includeStoryClothing) categories.Add(StoryClothing);
            if (categories.Count == 0) return null;

            Shuffle(categories);
            var inventory = GameManager.GetInventoryComponent();

            foreach (string[] category in categories)
            {
                var pool = new List<string>(category);
                Shuffle(pool);

                foreach (string item in pool)
                {
                    if (smallContainer && Array.IndexOf(BulkyStoryItems, item) >= 0) continue;

                    // Skip if player already has this exact item
                    if (inventory != null && inventory.GearInInventory(item, 1)) continue;

                    // Skip if this item was already given out in this save — at most one copy
                    // of each story item per playthrough, whatever happened to it since.
                    if (spawnedInSave.Contains(item)) continue;

                    // Skip items whose prefab isn't available (e.g. DLC-exclusive gear on a
                    // base-game install) — try the next candidate instead of stalling.
                    if (LoadPrefab(item) == null) continue;

                    return item;
                }
            }

            if (!_loggedAllOwned)
            {
                _loggedAllOwned = true;
                Core.Log.Msg(Loc.AllItemsOwned);
            }
            return null;
        }

        // Item appears inside the container loot UI. Returns false only when the item can't be
        // spawned, so the caller can try another one.
        private static bool TryAddStoryItemToContainer(Container container, string gearName, bool isSafe)
        {
            try
            {
                GearItem gi = SpawnGear(gearName);
                if (gi == null) return false;

                gi.transform.position = container.transform.position;
                gi.RollGearCondition(GearStartCondition.Random);
                container.AddGear(gi);
                spawnedObjects.Add(gi.gameObject);
                spawnedInSave.Add(gearName);

                Core.Log.Msg(isSafe
                    ? Loc.SpawnedSafeItem(container.name, gearName)
                    : Loc.SpawnedItem(container.name, gearName));
            }
            catch (Exception e)
            {
                Core.Log.Warning(Loc.SpawnFailed(gearName, e.Message));
                spawnedInSave.Add(gearName);
            }
            return true;
        }

        // ── Scene / save hooks ────────────────────────────────────────────────────
        // handledContainers is NOT cleared on zone travel — a container processed
        // once this session must never be processed again (prevents duplicate caches).
        // It is cleared only on exit to the main menu (DestroyAllSpawnedObjects).

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SaveGameSystem), nameof(SaveGameSystem.LoadSceneData),
            new Type[] { typeof(string), typeof(string) })]
        private static void SaveGameSystem_LoadSceneData()
        {
            _ammoSpawnedThisZone = false;
            // Remove dead Il2Cpp wrapper references left over from the previous zone.
            spawnedObjects.RemoveAll(go => go == null);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SaveGameSystem), nameof(SaveGameSystem.RestoreGlobalData))]
        private static void SaveGameSystem_RestoreGlobalData()
        {
            _ammoSpawnedThisZone = false;
        }

        // The restore argument doesn't always match the slot saves are written to ('autosave' on
        // scene transitions), so the list is always keyed by m_CurrentSaveName.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(SaveGameSystem), nameof(SaveGameSystem.RestoreGlobalData))]
        private static void SaveGameSystem_RestoreGlobalData_Postfix(string __0)
        {
            _restoredSinceMenu = true;
            string currentSaveName = SaveGameSystem.m_CurrentSaveName;
            if (__0 != currentSaveName)
                Core.Log.Msg($"[LVM] RestoreGlobalData('{__0}'), current save '{currentSaveName}'");
            LoadSpawnedStoryItems(currentSaveName);
        }

        // Written on every save (including autosaves), so an item spawned and then lost by
        // quitting without saving can appear again.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(SaveGameSystem), nameof(SaveGameSystem.SaveGlobalData))]
        private static void SaveGameSystem_SaveGlobalData()
        {
            SaveSpawnedStoryItems(SaveGameSystem.m_CurrentSaveName);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SaveGameSystem), nameof(SaveGameSystem.DeleteSaveFiles), new Type[] { typeof(string) })]
        private static void SaveGameSystem_DeleteSaveFiles(string __0)
        {
            try
            {
                string path = SpawnedStoryItemsPath(__0);
                if (path != null && File.Exists(path)) File.Delete(path);
                if (__0 == _spawnedListSaveName) spawnedInSave.Clear();
            }
            catch (Exception e)
            {
                Core.Log.Warning($"[LVM] Failed to delete story item list: {e.Message}");
            }
        }

        private static string SpawnedStoryItemsPath(string saveName)
        {
            if (string.IsNullOrEmpty(saveName)) return null;
            foreach (char c in Path.GetInvalidFileNameChars())
                saveName = saveName.Replace(c, '_');
            return Path.Combine(MelonEnvironment.UserDataDirectory, "LootVarianceMod", saveName + ".txt");
        }

        // A new game is recognised three independent ways (each logs which one fired): LaunchSandbox,
        // a new save slot id being requested from the main menu, or a container being opened after
        // returning to the menu without any save having been loaded.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(GameManager), nameof(GameManager.LaunchSandbox))]
        private static void GameManager_LaunchSandbox()
        {
            // m_CurrentSaveName may already be the new game's name here, so only the list's own save counts.
            BeginNewGameList("LaunchSandbox", _spawnedListSaveName);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SaveGameSlots), nameof(SaveGameSlots.GetUnusedGameId))]
        private static void SaveGameSlots_GetUnusedGameId()
        {
            // Still in the menu, so m_CurrentSaveName is the previous game's name.
            if (IsInMainMenu())
                BeginNewGameList("new save slot", _spawnedListSaveName ?? SaveGameSystem.m_CurrentSaveName);
        }

        private static void BeginNewGameList(string source, string previousSaveName)
        {
            if (!_adoptNextSaveName)
            {
                _previousSaveName = previousSaveName;
                Core.Log.Msg($"[LVM] New game detected ({source}), story item list reset");
            }
            spawnedInSave.Clear();
            _spawnedListSaveName = null;
            _adoptNextSaveName = true;
        }

        private static void EnsureSpawnedListFor(string saveName)
        {
            if (_adoptNextSaveName)
            {
                // Still the previous game's name: keep waiting for the new save's name.
                if (string.IsNullOrEmpty(saveName) || saveName == _previousSaveName) return;

                _adoptNextSaveName = false;
                _spawnedListSaveName = saveName;
                // The new game's save is known now, so the "container opened before any load" check
                // must not treat it as yet another new game.
                _restoredSinceMenu = true;
                return;
            }

            if (saveName == _spawnedListSaveName) return;
            spawnedInSave.Clear();
            _spawnedListSaveName = saveName;
        }

        private static void LoadSpawnedStoryItems(string saveName)
        {
            EnsureSpawnedListFor(saveName);
            try
            {
                string path = SpawnedStoryItemsPath(saveName);
                if (path != null && !_adoptNextSaveName && File.Exists(path))
                {
                    foreach (string line in File.ReadAllLines(path))
                        if (line.Length > 0) spawnedInSave.Add(line.Trim());
                }
                Core.Log.Msg($"[LVM] Story items already handed out in this save: {spawnedInSave.Count}");
            }
            catch (Exception e)
            {
                Core.Log.Warning($"[LVM] Failed to read story item list: {e.Message}");
            }
        }

        private static void SaveSpawnedStoryItems(string saveName)
        {
            try
            {
                EnsureSpawnedListFor(saveName);
                string path = SpawnedStoryItemsPath(saveName);
                if (path == null || _adoptNextSaveName) return;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, spawnedInSave);
            }
            catch (Exception e)
            {
                Core.Log.Warning($"[LVM] Failed to write story item list: {e.Message}");
            }
        }

        // spawnedInSave is intentionally NOT cleared here: a save-and-quit may still write it
        // after the exit. It is reset when a different save becomes current (EnsureSpawnedListFor).
        internal static void DestroyAllSpawnedObjects()
        {
            foreach (var go in spawnedObjects)
            {
                if (go != null) GameObject.Destroy(go);
            }
            spawnedObjects.Clear();
            handledContainers.Clear();
            handledStoryContainers.Clear();
            storyItemsPerScene.Clear();
            prefabCache.Clear();
            _restoredSinceMenu = false;
            _loggedAllOwned = false;
            DEBUG_spawnCounts.Clear();
        }

        // ── Debug helpers (used by console commands) ──────────────────────────────

        internal static int DEBUG_SpawnedCount()  => spawnedObjects.Count;
        internal static IEnumerable<KeyValuePair<string, int>> DEBUG_SpawnCounts() => DEBUG_spawnCounts;
        internal static int DEBUG_HandledCount()  => handledContainers.Count;
        internal static int DEBUG_SessionItemCount() => spawnedInSave.Count;

        internal static string DEBUG_PickStoryItem() => PickAvailableStoryItem();

        internal static void DEBUG_GiveStoryItem(string gearName)
        {
            try
            {
                GearItem gi = SpawnGear(gearName);
                if (gi == null) return;

                gi.RollGearCondition(GearStartCondition.Random);
                GameManager.GetInventoryComponent().AddGear(gi);
                spawnedObjects.Add(gi.gameObject);
                spawnedInSave.Add(gearName);
            }
            catch (Exception e)
            {
                Core.Log.Warning(Loc.SpawnFailed(gearName, e.Message));
            }
        }

        internal static void DEBUG_SpawnCacheIn(Container container)
        {
            TryAddSurvivorCache(container);
        }

        // Reports which pool entries exist in this install (DLC / episodes vary per player).
        internal static IEnumerable<string> DEBUG_ScanPools()
        {
            var pools = new (string Label, string[] Names)[]
            {
                ("Weapons",  StoryWeapons),
                ("Tools",    StoryTools),
                ("Clothing", StoryClothing),
                ("Cache",    Array.ConvertAll(SurvivorCachePool, p => p.Name)),
            };

            foreach (var (label, names) in pools)
            {
                var missing = new List<string>();
                foreach (string name in names)
                    if (LoadPrefab(name) == null) missing.Add(name);

                yield return $"[LVM] {label}: {names.Length - missing.Count} available, {missing.Count} missing";
                if (missing.Count > 0)
                    yield return "[LVM]   missing: " + string.Join(", ", missing);
            }
        }
    }

    // Must be top-level classes — Harmony doesn't scan nested types inside a [HarmonyPatch] class.

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.DoExitToMainMenu))]
    internal static class Patch_LVM_ExitToMainMenu
    {
        private static void Postfix()
        {
            Patches.DestroyAllSpawnedObjects();
        }
    }

    [HarmonyPatch(typeof(Localization), "Get")]
    internal static class Patch_LVM_LocalizationGet
    {
        private static void Postfix(string __0, ref string __result)
        {
            if (__0 != null && __0.StartsWith("LVM."))
                __result = LVMLocalization.Get(__0);
        }
    }

    [HarmonyPatch]
    internal static class Patch_LVM_DescriptionText
    {
        private static MethodBase TargetMethod() =>
            AccessTools.PropertyGetter(
                AccessTools.TypeByName("ModSettings.DescriptionHolder"), "Text");

        private static void Postfix(ref string __result)
        {
            if (__result != null && __result.StartsWith("LVM."))
                __result = LVMLocalization.Get(__result);
        }
    }
}
