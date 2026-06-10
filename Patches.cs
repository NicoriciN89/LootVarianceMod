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
            "GEAR_JeremiahKnife",
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
            "GEAR_AccelerantTinder",
            "GEAR_Candle",
            // Tools / repair
            "GEAR_SewingKit",
            "GEAR_WhetstoneKnife",
            "GEAR_Hacksaw",
            // Clothing
            "GEAR_WoolSocks",
            "GEAR_WoolMittens",
            "GEAR_TuqueBeanie",
            // Other
            "GEAR_FlareA",
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
                    GearItem prefab = GearItem.LoadGearItemPrefab(gearName);
                    if (prefab == null) continue;

                    go = GameObject.Instantiate(prefab.gameObject, container.transform.position, Quaternion.identity);
                    go.name = prefab.name;

                    var gi = go.GetComponent<GearItem>();
                    if (gi == null) { GameObject.Destroy(go); continue; }

                    gi.RollGearCondition(GearStartCondition.Random);
                    container.AddGear(gi);
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

        // Containers that have already been processed this session
        private static readonly HashSet<int> handledContainers = new();

        // Story items that were spawned this session but not yet picked up
        // Prevents the same item appearing twice before the player collects the first one
        private static readonly HashSet<string> spawnedThisSession = new();

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

            int id = __instance.GetInstanceID();
            if (handledContainers.Contains(id)) return;
            handledContainers.Add(id);

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
                    {
                        if (isSafe) TryAddStoryItemToContainer(__instance, itemName);
                        else        TrySpawnLooseItemNearContainer(__instance, itemName);
                    }
                }
            }

            TryAddSurvivorCache(__instance);
            TryAddBonusAmmo(__instance);
        }

        // All rifle/revolver/bow gear names — used to detect what weapons the player carries.
        private static readonly string[] RifleGearNames    = { "GEAR_Rifle", "GEAR_Rifle_Trader", "GEAR_Rifle_Barbs", "GEAR_Rifle_Curators", "GEAR_Rifle_Vaughns" };
        private static readonly string[] RevolverGearNames = { "GEAR_Revolver", "GEAR_RevolverStubNosed", "GEAR_RevolverFancy", "GEAR_RevolverGreen" };
        private static readonly string[] BowGearNames      = { "GEAR_Bow", "GEAR_Bow_Woodwrights", "GEAR_Bow_Manufactured", "GEAR_Bow_Bushcraft" };

        // Spawns ammo near a world position.
        // Stackable ammo (bullets): one object with m_Units = amount.
        // Non-stackable (arrows): one object per arrow, scattered slightly.
        private static void SpawnAmmoNear(string gearName, int amount, Vector3 basePos)
        {
            GearItem prefab = GearItem.LoadGearItemPrefab(gearName);
            if (prefab == null) { Core.Log.Warning($"[LVM] Ammo prefab not found: {gearName}"); return; }

            bool isStackable = prefab.m_StackableItem != null;

            if (isStackable)
            {
                Vector2 off = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(0.6f, 1.4f);
                Vector3 pos = new Vector3(basePos.x + off.x, basePos.y + 0.3f, basePos.z + off.y);
                var go = GameObject.Instantiate(prefab.gameObject, pos, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
                go.name = prefab.name;
                var xpb = go.GetComponent<DisableObjectForXPMode>();
                if (xpb != null) GameObject.Destroy(xpb);
                var gi = go.GetComponent<GearItem>();
                if (gi == null) { GameObject.Destroy(go); return; }
                gi.m_StackableItem.m_Units = amount;
            }
            else
            {
                // Arrows: spawn individually, scattered in a small cluster.
                for (int i = 0; i < amount; i++)
                {
                    Vector2 off = UnityEngine.Random.insideUnitCircle * 0.3f;
                    Vector3 pos = new Vector3(basePos.x + off.x, basePos.y + 0.3f, basePos.z + off.y);
                    var go = GameObject.Instantiate(prefab.gameObject, pos, Quaternion.Euler(UnityEngine.Random.Range(-15f, 15f), UnityEngine.Random.Range(0f, 360f), 0f));
                    go.name = prefab.name;
                    var xpb = go.GetComponent<DisableObjectForXPMode>();
                    if (xpb != null) GameObject.Destroy(xpb);
                    var gi = go.GetComponent<GearItem>();
                    if (gi == null) { GameObject.Destroy(go); continue; }
                    gi.RollGearCondition(GearStartCondition.Random);
                }
            }

            Core.Log.Msg($"[LVM] Spawned ammo: {gearName} x{amount}");
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

            var candidates = CollectLowAmmoCandidates();
            if (candidates == null || candidates.Count == 0) return;
            if (!Utils.RollChance(Settings.bonusAmmoChancePct / 100f)) return;

            string chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            int amount = UnityEngine.Random.Range(5, 8);

            // Always use the player's Y as floor reference — containers can be at any height.
            var playerT = GameManager.GetPlayerTransform();
            float floorY = playerT != null ? playerT.position.y : container.transform.position.y;
            Vector2 off = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(0.6f, 1.4f);
            Vector3 spawnPos = new Vector3(
                container.transform.position.x + off.x,
                floorY + 0.3f,
                container.transform.position.z + off.y);

            try { SpawnAmmoNear(chosen, amount, spawnPos); }
            catch (Exception e) { Core.Log.Warning($"[LVM] Bonus ammo failed ({chosen}): {e.Message}"); }
        }

        // Periodic check (called every ~2 min): spawn ammo near the player if they're low.
        internal static void TryPeriodicBonusAmmo()
        {
            if (!Settings.enableBonusAmmo) return;

            var playerT = GameManager.GetPlayerTransform();
            if (playerT == null) return;

            var candidates = CollectLowAmmoCandidates();
            if (candidates == null || candidates.Count == 0) return;
            if (!Utils.RollChance(Settings.bonusAmmoChancePct / 100f)) return;

            string chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            int amount = UnityEngine.Random.Range(5, 8);

            // Drop a few metres away from the player — close enough to find, not immediately obvious.
            Vector2 offset = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(2.0f, 4.0f);
            Vector3 spawnPos = new Vector3(
                playerT.position.x + offset.x,
                playerT.position.y + 0.02f,
                playerT.position.z + offset.y);

            try { SpawnAmmoNear(chosen, amount, spawnPos); }
            catch (Exception e) { Core.Log.Warning($"[LVM] Periodic ammo failed ({chosen}): {e.Message}"); }
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
                // Skip items the player currently carries
                if (inventory != null && inventory.GearInInventory(item, 1))
                    continue;

                // Skip items already spawned this session (sitting in the world uncollected)
                if (spawnedThisSession.Contains(item))
                    continue;

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
                GearItem prefab = GearItem.LoadGearItemPrefab(gearName);
                if (prefab == null) { Core.Log.Warning(Loc.ItemNotFound(gearName)); return; }

                go = GameObject.Instantiate(prefab.gameObject, container.transform.position, Quaternion.identity);
                go.name = prefab.name;

                var gi = go.GetComponent<GearItem>();
                if (gi == null) { GameObject.Destroy(go); return; }

                gi.RollGearCondition(GearStartCondition.Random);
                container.AddGear(gi);

                spawnedThisSession.Add(gearName);
                Core.Log.Msg(Loc.SpawnedSafeItem(container.name, gearName));
            }
            catch (Exception e)
            {
                if (go != null) GameObject.Destroy(go);
                Core.Log.Warning(Loc.SpawnFailed(gearName, e.Message));
            }
        }

        // House / fishing hut: item lies on the floor near the container.
        private static void TrySpawnLooseItemNearContainer(Container container, string gearName)
        {
            GameObject go = null;
            try
            {
                GearItem prefab = GearItem.LoadGearItemPrefab(gearName);
                if (prefab == null) { Core.Log.Warning(Loc.ItemNotFound(gearName)); return; }

                // Spawn near the player, not the container — containers are often against walls.
                var playerT = GameManager.GetPlayerTransform();
                Vector3 basePos = playerT != null ? playerT.position : container.transform.position;
                Vector2 offset = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(0.4f, 1.0f);
                Vector3 spawnPos = new Vector3(basePos.x + offset.x, basePos.y + 0.05f, basePos.z + offset.y);
                go = GameObject.Instantiate(prefab.gameObject, spawnPos, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
                go.name = prefab.name;

                var xpBlock = go.GetComponent<DisableObjectForXPMode>();
                if (xpBlock != null) GameObject.Destroy(xpBlock);

                var gi = go.GetComponent<GearItem>();
                if (gi == null) { GameObject.Destroy(go); return; }
                gi.RollGearCondition(GearStartCondition.Random);

                spawnedThisSession.Add(gearName);
                Core.Log.Msg(Loc.SpawnedItem(container.name, gearName));
            }
            catch (Exception e)
            {
                if (go != null) GameObject.Destroy(go);
                Core.Log.Warning(Loc.SpawnFailed(gearName, e.Message));
            }
        }

        // ── Clear state on scene / save load ──────────────────────────────────────
        // handledContainers resets every zone so containers re-roll on revisit.
        // spawnedThisSession is intentionally NOT cleared on zone travel — once
        // this mod has spawned a story item anywhere this session it will not
        // spawn the same item again, even if the player dropped it.
        // It is cleared only when a new save is loaded (fresh game).

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SaveGameSystem), nameof(SaveGameSystem.LoadSceneData),
            new Type[] { typeof(string), typeof(string) })]
        private static void SaveGameSystem_LoadSceneData()
        {
            handledContainers.Clear();
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SaveGameSystem), nameof(SaveGameSystem.RestoreGlobalData))]
        private static void SaveGameSystem_RestoreGlobalData()
        {
            handledContainers.Clear();
            spawnedThisSession.Clear();
        }
    }

    // Must be top-level classes — Harmony doesn't scan nested types inside a [HarmonyPatch] class.

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
