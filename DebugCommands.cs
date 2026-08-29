using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace LootVarianceMod
{
    [HarmonyPatch(typeof(ConsoleManager), "RegisterCommands")]
    internal static class LVM_DebugCommands
    {
        private static void Postfix()
        {
            uConsole.RegisterCommand("lvm_story",   new System.Action(CMD_SpawnStoryItem));
            uConsole.RegisterCommand("lvm_ammo",    new System.Action(CMD_SpawnAmmo));
            uConsole.RegisterCommand("lvm_cache",   new System.Action(CMD_SpawnCache));
            uConsole.RegisterCommand("lvm_cleanup", new System.Action(CMD_Cleanup));
            uConsole.RegisterCommand("lvm_status",  new System.Action(CMD_Status));
        }

        // lvm_story — give a story item directly to the player's inventory
        private static void CMD_SpawnStoryItem()
        {
            string itemName = Patches.DEBUG_PickStoryItem();
            if (itemName == null) { uConsole.Log("[LVM] No story item available (all owned or spawned)."); return; }

            Patches.DEBUG_GiveStoryItem(itemName);
            uConsole.Log($"[LVM] Gave story item to inventory: {itemName}");
        }

        // lvm_ammo — force-spawn bonus ammo near the player
        private static void CMD_SpawnAmmo()
        {
            float saved = Settings.instance.bonusAmmoChancePct;
            Settings.instance.bonusAmmoChancePct = 100;
            Patches.TryPeriodicBonusAmmo();
            Settings.instance.bonusAmmoChancePct = (int)saved;
            uConsole.Log("[LVM] Bonus ammo triggered.");
        }

        // lvm_cache — force-spawn a survivor cache in the nearest container
        private static void CMD_SpawnCache()
        {
            var playerT = GameManager.GetPlayerTransform();
            if (playerT == null) { uConsole.Log("[LVM] No player found."); return; }

            var container = FindNearestContainer(playerT.position);
            if (container == null) { uConsole.Log("[LVM] No container nearby."); return; }

            float savedChest    = Settings.instance.survivorCacheChestChancePct;
            float savedWardrobe = Settings.instance.survivorCacheWardrobeChancePct;
            Settings.instance.survivorCacheChestChancePct    = 25;
            Settings.instance.survivorCacheWardrobeChancePct = 25;

            Patches.DEBUG_SpawnCacheIn(container);

            Settings.instance.survivorCacheChestChancePct    = (int)savedChest;
            Settings.instance.survivorCacheWardrobeChancePct = (int)savedWardrobe;
            uConsole.Log($"[LVM] Cache triggered in: {container.name}");
        }

        // lvm_cleanup — simulate DoExitToMainMenu cleanup
        private static void CMD_Cleanup()
        {
            int count = Patches.DEBUG_SpawnedCount();
            Patches.DestroyAllSpawnedObjects();
            uConsole.Log($"[LVM] Destroyed {count} spawned object(s).");
        }

        // lvm_status — print current mod state
        private static void CMD_Status()
        {
            uConsole.Log($"[LVM] Spawned objects tracked: {Patches.DEBUG_SpawnedCount()}");
            uConsole.Log($"[LVM] Handled containers this zone: {Patches.DEBUG_HandledCount()}");
            uConsole.Log($"[LVM] Story items spawned this session: {Patches.DEBUG_SessionItemCount()}");
        }

        private static Container FindNearestContainer(Vector3 origin)
        {
            Container best = null;
            float bestDist = float.MaxValue;
            foreach (var c in GameObject.FindObjectsOfType<Container>())
            {
                float d = Vector3.Distance(origin, c.transform.position);
                if (d < bestDist) { bestDist = d; best = c; }
            }
            return best;
        }
    }
}
