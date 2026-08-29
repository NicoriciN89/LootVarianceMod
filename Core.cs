using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(LootVarianceMod.Core), "LootVarianceMod", "1.1.5", "NnicolaeN")]
[assembly: MelonGame("Hinterland", "TheLongDark")]
[assembly: MelonColor(255, 220, 140, 30)]

namespace LootVarianceMod
{
    public class Core : MelonMod
    {
        internal static MelonLogger.Instance Log;

        // Check every 2 real-world minutes whether the player needs ammo.
        private float _lastPeriodicCheck = -999f;
        private const float PeriodicInterval = 120f;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            Settings.OnLoad();
            Log.Msg(Loc.ModLoaded);
        }

        public override void OnUpdate()
        {
            float now = Time.unscaledTime;
            if (now - _lastPeriodicCheck < PeriodicInterval) return;
            _lastPeriodicCheck = now;
            Patches.TryPeriodicBonusAmmo();
        }
    }
}
