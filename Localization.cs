using UnityEngine;

namespace LootVarianceMod
{
    internal static class Loc
    {
        private static readonly bool IsRu =
            Application.systemLanguage == SystemLanguage.Russian;

        internal static string Get(string en, string ru) => IsRu ? ru : en;

        // ── Mod init ──────────────────────────────────────────────────────────────

        private static string Version =>
            System.Reflection.Assembly.GetExecutingAssembly()
                .GetCustomAttributes(typeof(MelonLoader.MelonInfoAttribute), false)
                is MelonLoader.MelonInfoAttribute[] attrs && attrs.Length > 0
                ? attrs[0].Version : "?";

        internal static string ModLoaded =>
            Get($"LootVarianceMod v{Version} loaded", $"LootVarianceMod v{Version} загружен");

        // ── Spawn messages ────────────────────────────────────────────────────────

        internal static string SpawnedItem(string container, string item) =>
            Get($"[LVM] Spawned story item near [{container}]: {item}",
                $"[LVM] Сюжетный предмет появился рядом с [{container}]: {item}");

        internal static string SpawnedSafeItem(string container, string item) =>
            Get($"[LVM] Spawned story item (safe bonus) near [{container}]: {item}",
                $"[LVM] Сюжетный предмет (сейф-бонус) появился рядом с [{container}]: {item}");

        internal static string ItemNotFound(string item) =>
            Get($"[LVM] Story item prefab not found: {item}",
                $"[LVM] Префаб предмета не найден: {item}");

        internal static string SpawnFailed(string item, string err) =>
            Get($"[LVM] Failed to spawn story item {item}: {err}",
                $"[LVM] Ошибка при создании предмета {item}: {err}");

        internal static string AllItemsOwned =>
            Get("[LVM] All story items already owned/spawned — skipping.",
                "[LVM] Все сюжетные предметы уже собраны или лежат в мире — пропуск.");

        internal static string SpawnedCache(string container, int count) =>
            Get($"[LVM] Survivor cache ({count} items) added to [{container}]",
                $"[LVM] Тайник выжившего ({count} предм.) добавлен в [{container}]");
    }

    internal static class Utils
    {
        internal static bool RollChance(float probability) =>
            UnityEngine.Random.value < probability;
    }
}
