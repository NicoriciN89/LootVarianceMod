using ModSettings;

namespace LootVarianceMod
{
    internal static class Settings
    {
        internal static LVMSettings instance;

        internal static bool enableConditionVariance  => instance.enableConditionVariance;
        internal static int  minConditionPct          => instance.minConditionPct;
        internal static int  maxConditionPct          => instance.maxConditionPct;
        internal static bool enableStoryItems         => instance.enableStoryItems;
        internal static int  storyItemChancePct       => instance.storyItemChancePct;
        internal static int  storyItemSafeChancePct   => instance.storyItemSafeChancePct;
        internal static bool includeStoryWeapons      => instance.includeStoryWeapons;
        internal static bool includeStoryTools        => instance.includeStoryTools;
        internal static bool includeStoryClothing     => instance.includeStoryClothing;

        internal static bool enableBonusAmmo              => instance.enableBonusAmmo;
        internal static int  bonusAmmoChancePct           => instance.bonusAmmoChancePct;
        internal static int  bonusAmmoThreshold           => instance.bonusAmmoThreshold;

        internal static bool enableSurvivorCache          => instance.enableSurvivorCache;
        internal static int  survivorCacheChestChancePct  => instance.survivorCacheChestChancePct;
        internal static int  survivorCacheWardrobeChancePct => instance.survivorCacheWardrobeChancePct;
        internal static int  survivorCacheMinItems        => instance.survivorCacheMinItems;
        internal static int  survivorCacheMaxItems        => instance.survivorCacheMaxItems;

        internal static void OnLoad()
        {
            instance = new LVMSettings();
            instance.AddToModSettings("Loot Variance");
        }
    }

    internal class LVMSettings : JsonModSettings
    {
        [Section("LVM.SECTION_CONDITION", Localize = true)]
        [Name("LVM.ENABLE_CONDITION", Localize = true)]
        [Description("LVM.DESC_ENABLE_CONDITION", Localize = true)]
        public bool enableConditionVariance = true;

        [Name("LVM.MIN_CONDITION", Localize = true)]
        [Description("LVM.DESC_MIN_CONDITION", Localize = true)]
        [Slider(1, 49, 49)]
        public int minConditionPct = 2;

        [Name("LVM.MAX_CONDITION", Localize = true)]
        [Description("LVM.DESC_MAX_CONDITION", Localize = true)]
        [Slider(51, 100, 50)]
        public int maxConditionPct = 98;

        [Section("LVM.SECTION_STORY", Localize = true)]
        [Name("LVM.ENABLE_STORY", Localize = true)]
        [Description("LVM.DESC_ENABLE_STORY", Localize = true)]
        public bool enableStoryItems = true;

        [Name("LVM.CHANCE_CONTAINERS", Localize = true)]
        [Description("LVM.DESC_CHANCE_CONTAINERS", Localize = true)]
        [Slider(1, 100, 100)]
        public int storyItemChancePct = 5;

        [Name("LVM.CHANCE_SAFES", Localize = true)]
        [Description("LVM.DESC_CHANCE_SAFES", Localize = true)]
        [Slider(1, 100, 100)]
        public int storyItemSafeChancePct = 25;

        [Name("LVM.INCLUDE_WEAPONS", Localize = true)]
        [Description("LVM.DESC_INCLUDE_WEAPONS", Localize = true)]
        public bool includeStoryWeapons = true;

        [Name("LVM.INCLUDE_TOOLS", Localize = true)]
        [Description("LVM.DESC_INCLUDE_TOOLS", Localize = true)]
        public bool includeStoryTools = true;

        [Name("LVM.INCLUDE_CLOTHING", Localize = true)]
        [Description("LVM.DESC_INCLUDE_CLOTHING", Localize = true)]
        public bool includeStoryClothing = true;

        [Section("LVM.SECTION_BONUS_AMMO", Localize = true)]
        [Name("LVM.ENABLE_BONUS_AMMO", Localize = true)]
        [Description("LVM.DESC_ENABLE_BONUS_AMMO", Localize = true)]
        public bool enableBonusAmmo = true;

        [Name("LVM.BONUS_AMMO_CHANCE", Localize = true)]
        [Description("LVM.DESC_BONUS_AMMO_CHANCE", Localize = true)]
        [Slider(1, 100, 100)]
        public int bonusAmmoChancePct = 90;

        [Name("LVM.BONUS_AMMO_THRESHOLD", Localize = true)]
        [Description("LVM.DESC_BONUS_AMMO_THRESHOLD", Localize = true)]
        [Slider(1, 20, 20)]
        public int bonusAmmoThreshold = 5;

        [Section("LVM.SECTION_CACHE", Localize = true)]
        [Name("LVM.ENABLE_CACHE", Localize = true)]
        [Description("LVM.DESC_ENABLE_CACHE", Localize = true)]
        public bool enableSurvivorCache = true;

        [Name("LVM.CACHE_CHANCE_CHEST", Localize = true)]
        [Description("LVM.DESC_CACHE_CHANCE_CHEST", Localize = true)]
        [Slider(1, 25, 25)]
        public int survivorCacheChestChancePct = 10;

        [Name("LVM.CACHE_CHANCE_WARDROBE", Localize = true)]
        [Description("LVM.DESC_CACHE_CHANCE_WARDROBE", Localize = true)]
        [Slider(1, 25, 25)]
        public int survivorCacheWardrobeChancePct = 6;

        [Name("LVM.CACHE_MIN_ITEMS", Localize = true)]
        [Description("LVM.DESC_CACHE_MIN_ITEMS", Localize = true)]
        [Slider(1, 5, 5)]
        public int survivorCacheMinItems = 3;

        [Name("LVM.CACHE_MAX_ITEMS", Localize = true)]
        [Description("LVM.DESC_CACHE_MAX_ITEMS", Localize = true)]
        [Slider(2, 8, 7)]
        public int survivorCacheMaxItems = 5;

    }
}
