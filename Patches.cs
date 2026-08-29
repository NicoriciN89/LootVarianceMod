using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace LootVarianceMod
{
    [HarmonyPatch]
    internal static class Patches
    {
        // ── Story item lists ──────────────────────────────────────────────────────

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
        };

        private static readonly string[] StoryTools =
        {
            "GEAR_SurvivalKnife",
            "GEAR_CougarClawKnife",
            "GEAR_KeroseneLamp_Spelunkers",
            "GEAR_Flashlight_LongLasting",
        };

        private static readonly string[] StoryClothing =
        {
            "GEAR_GreyMotherBoots",
            "GEAR_BedRoll_Down",
        };

        // ── Survivor Cache ────────────────────────────────────────────────────────

        private static readonly string[] ChestKeywords    = { "chest", "trunk", "footlocker" };
        private static readonly string[] WardrobeKeywords = { "wardrobe", "dresser", "closet", "cabinet", "drawer" };

        // Returns 0 = not a cache container, 1 = chest, 2 = wardrobe
        private static int CacheContainerType(Container c)
        {
            string name = c.gameObject.name;
            foreach (string kw in ChestKeywords)
                if (name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return 1;
            foreach (string kw in WardrobeKeywords)
                if (name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            return 0;
        }

        // Varied survival supplies a previous survivor might have stashed.
        // Prefabs that don't exist are skipped gracefully.
        private static readonly string[] SurvivorCachePool =
        {
            // Food
            "GEAR_CannedBeans",
            "GEAR_CannedPeaches",
            "GEAR_CannedSardines",
            "GEAR_CannedDogFood",
            "GEAR_Pemmican",
            "GEAR_EnergyBar",
            "GEAR_Crackers",
            // Medical
            "GEAR_PainKillers",
            "GEAR_AntibioticsPills",
            "GEAR_Bandage",
            // Fire / light
            "GEAR_MatchBook",
            "GEAR_WoodMatches",
            // Tools / repair
            "GEAR_SewingKit",
            "GEAR_WhetstoneKnife",
            "GEAR_Hacksaw",
            // Clothing
            "GEAR_WoolSocks",
            "GEAR_WoolMittens",
            "GEAR_TuqueBeanie",
            // Other
            "GEAR_RopeLashing",
        };

        private static void TryAddSurvivorCache(Container container)
        {
            if (!Settings.enableSurvivorCache) return;

            // Safes are handled by the story-items system; skip them here.
            bool isSafe = container.gameObject.name.IndexOf("safe", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isSafe) return;

            int ctype = CacheContainerType(container);
            if (ctype == 0) return;

            float chance = ctype == 1
                ? Settings.survivorCacheChestChancePct   / 100f
                : Settings.survivorCacheWardrobeChancePct / 100f;
            if (!Utils.RollChance(chance)) return;

            // Shuffle so every cache has a different mix of items.
            var pool = new List<string>(SurvivorCachePool);
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            int cacheMin = Settings.survivorCacheMinItems;
            int cacheMax = Math.Max(Settings.survivorCacheMaxItems, cacheMin);
            int target   = UnityEngine.Random.Range(cacheMin, cacheMax + 1);
            int spawned = 0;

            foreach (string gearName in pool)
            {
                if (spawned >= target) break;

                GameObject go = null;
                try
                {
                    GearItem prefab = LoadPrefab(gearName);
                    if (prefab == null) continue;

                    go = GameObject.Instantiate(prefab.gameObject, container.transform.position, Quaternion.identity);
                    go.name = prefab.name;

                    var gi = go.GetComponent<GearItem>();
                    if (gi == null) { GameObject.Destroy(go); continue; }

                    gi.RollGearCondition(GearStartCondition.Random);
                    container.AddGear(gi);
                    spawnedObjects.Add(go);
                    spawned++;
                }
                catch (Exception e)
                {
                    if (go != null) GameObject.Destroy(go);
                    Core.Log.Warning($"[LVM] Cache item failed ({gearName}): {e.Message}");
                }
            }

            if (spawned > 0)
                Core.Log.Msg(Loc.SpawnedCache(container.name, spawned));
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

        // Containers that have already been processed this zone.
        // Key: "name@x,y,z" — stable across multiple PopulateWithRandomGear calls on the same object.
        private static readonly HashSet<string> handledContainers = new();

        private static string ContainerKey(Container c)
        {
            var p = c.transform.position;
            return string.Intern($"{c.gameObject.name}@{p.x:F1},{p.y:F1},{p.z:F1}");
        }

        // Story items that were spawned this session but not yet picked up
        // Prevents the same item appearing twice before the player collects the first one
        private static readonly HashSet<string> spawnedThisSession = new();

        // All GameObjects spawned by this mod — destroyed on exit to main menu
        private static readonly List<GameObject> spawnedObjects = new();

        // Bonus ammo fires at most once per zone load
        private static bool _ammoSpawnedThisZone = false;

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

        // ── Story Items in Containers ─────────────────────────────────────────────

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Container), nameof(Container.PopulateWithRandomGear))]
        private static void Container_PopulateWithRandomGear(Container __instance)
        {
            if (__instance == null) return;

            string key = ContainerKey(__instance);
            if (handledContainers.Contains(key)) return;
            handledContainers.Add(key);

            bool isSafe = __instance.gameObject.name.IndexOf("safe", StringComparison.OrdinalIgnoreCase) >= 0;

            if (Settings.enableStoryItems)
            {
                float chance = isSafe
                    ? Settings.storyItemSafeChancePct / 100f
                    : Settings.storyItemChancePct / 100f;
                if (Utils.RollChance(chance))
                {
                    string itemName = PickAvailableStoryItem();
                    if (itemName != null)
                        TryAddStoryItemToContainer(__instance, itemName);
                }
            }

            TryAddSurvivorCache(__instance);
            TryAddBonusAmmo(__instance);
        }

        // All rifle/revolver/bow gear names — used to detect what weapons the player carries.
        private static readonly string[] RifleGearNames    = { "GEAR_Rifle", "GEAR_Rifle_Trader", "GEAR_Rifle_Barbs", "GEAR_Rifle_Curators", "GEAR_Rifle_Vaughns" };
        private static readonly string[] RevolverGearNames = { "GEAR_Revolver", "GEAR_RevolverStubNosed", "GEAR_RevolverFancy", "GEAR_RevolverGreen" };
        private static readonly string[] BowGearNames      = { "GEAR_Bow", "GEAR_Bow_Woodwrights", "GEAR_Bow_Manufactured", "GEAR_Bow_Bushcraft" };

        // Adds ammo directly to the player's inventory — no world spawn, no geometry issues.
        private static void GiveAmmoToPlayer(string gearName, int amount)
        {
            GearItem prefab = LoadPrefab(gearName);
            if (prefab == null) { Core.Log.Warning($"[LVM] Ammo prefab not found: {gearName}"); return; }

            GameObject go = null;
            try
            {
                go = GameObject.Instantiate(prefab.gameObject);
                go.name = prefab.name;

                var gi = go.GetComponent<GearItem>();
                if (gi == null) { GameObject.Destroy(go); return; }

                if (gi.m_StackableItem != null)
                    gi.m_StackableItem.m_Units = amount;
                else
                    gi.RollGearCondition(GearStartCondition.Random);

                GameManager.GetInventoryComponent().AddGear(gi);
                Core.Log.Msg($"[LVM] Added ammo to inventory: {gearName} x{amount}");
            }
            catch (Exception e)
            {
                if (go != null) GameObject.Destroy(go);
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

            if (HasAny(RifleGearNames)    && !inventory.GearInInventory("GEAR_RifleAmmoSingle",    thr)) candidates.Add("GEAR_RifleAmmoSingle");
            if (HasAny(RevolverGearNames) && !inventory.GearInInventory("GEAR_RevolverAmmoSingle", thr)) candidates.Add("GEAR_RevolverAmmoSingle");
            if (HasAny(BowGearNames)      && !inventory.GearInInventory("GEAR_Arrow",              thr)) candidates.Add("GEAR_Arrow");
            return candidates;
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
            if (GameManager.GetPlayerTransform() == null) return;

            var candidates = CollectLowAmmoCandidates();
            if (candidates == null || candidates.Count == 0) return;
            if (!Utils.RollChance(Settings.bonusAmmoChancePct / 100f)) return;

            string chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            int amount = UnityEngine.Random.Range(5, 8);
            GiveAmmoToPlayer(chosen, amount);
        }

        // Returns a story item the player doesn't already own and that hasn't been
        // spawned yet this session, or null if all eligible items are accounted for.
        private static string PickAvailableStoryItem()
        {
            var pool = new List<string>();

            if (Settings.includeStoryWeapons)
                pool.AddRange(StoryWeapons);
            if (Settings.includeStoryTools)
                pool.AddRange(StoryTools);
            if (Settings.includeStoryClothing)
                pool.AddRange(StoryClothing);

            if (pool.Count == 0) return null;

            // Shuffle so we don't always prefer the first items
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            var inventory = GameManager.GetInventoryComponent();

            foreach (string item in pool)
            {
                bool inInventory = inventory != null && inventory.GearInInventory(item, 1);

                // Skip if player already has this exact item
                if (inInventory) continue;

                // If spawned this session but no longer in inventory (broken/lost) — allow again
                if (spawnedThisSession.Contains(item))
                {
                    if (inInventory) continue;
                    spawnedThisSession.Remove(item);
                }

                // Skip items whose prefab isn't loaded (e.g. DLC-exclusive gear on a base-game
                // install) — try the next candidate instead of stalling story items entirely.
                if (LoadPrefab(item) == null) continue;

                return item;
            }

            Core.Log.Msg(Loc.AllItemsOwned);
            return null;
        }

        // Safe: item appears inside the container loot UI.
        private static void TryAddStoryItemToContainer(Container container, string gearName)
        {
            GameObject go = null;
            try
            {
                GearItem prefab = LoadPrefab(gearName);
                if (prefab == null) { Core.Log.Warning(Loc.ItemNotFound(gearName)); return; }

                go = GameObject.Instantiate(prefab.gameObject, container.transform.position, Quaternion.identity);
                go.name = prefab.name;

                var gi = go.GetComponent<GearItem>();
                if (gi == null) { GameObject.Destroy(go); return; }

                gi.RollGearCondition(GearStartCondition.Random);
                container.AddGear(gi);
                spawnedObjects.Add(go);

                spawnedThisSession.Add(gearName);
                Core.Log.Msg(Loc.SpawnedSafeItem(container.name, gearName));
            }
            catch (Exception e)
            {
                if (go != null) GameObject.Destroy(go);
                Core.Log.Warning(Loc.SpawnFailed(gearName, e.Message));
            }
        }


        // ── Clear state on scene / save load ──────────────────────────────────────
        // handledContainers is NOT cleared on zone travel — a container processed
        // once this session must never be processed again (prevents duplicate caches).
        // spawnedThisSession is also kept across zone travel for the same reason.
        // Both are cleared only on new game (DestroyAllSpawnedObjects).

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
            // handledContainers and spawnedThisSession intentionally NOT cleared —
            // prevents duplicate caches/items across save/load cycles within a session.
            _ammoSpawnedThisZone = false;
        }

        internal static void DestroyAllSpawnedObjects()
        {
            foreach (var go in spawnedObjects)
            {
                if (go != null) GameObject.Destroy(go);
            }
            spawnedObjects.Clear();
            handledContainers.Clear();
            spawnedThisSession.Clear();
            prefabCache.Clear();
        }

        // ── Debug helpers (used by console commands) ──────────────────────────────

        internal static int DEBUG_SpawnedCount()  => spawnedObjects.Count;
        internal static int DEBUG_HandledCount()  => handledContainers.Count;
        internal static int DEBUG_SessionItemCount() => spawnedThisSession.Count;

        internal static string DEBUG_PickStoryItem() => PickAvailableStoryItem();

        internal static void DEBUG_GiveStoryItem(string gearName)
        {
            GameObject go = null;
            try
            {
                GearItem prefab = LoadPrefab(gearName);
                if (prefab == null) { Core.Log.Warning(Loc.ItemNotFound(gearName)); return; }

                go = GameObject.Instantiate(prefab.gameObject);
                go.name = prefab.name;

                var gi = go.GetComponent<GearItem>();
                if (gi == null) { GameObject.Destroy(go); return; }

                gi.RollGearCondition(GearStartCondition.Random);
                GameManager.GetInventoryComponent().AddGear(gi);
                spawnedObjects.Add(go);
                spawnedThisSession.Add(gearName);
            }
            catch (Exception e)
            {
                if (go != null) GameObject.Destroy(go);
                Core.Log.Warning(Loc.SpawnFailed(gearName, e.Message));
            }
        }

        internal static void DEBUG_SpawnCacheIn(Container container)
        {
            TryAddSurvivorCache(container);
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
