using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Il2Cpp;
using MelonLoader;

namespace LootVarianceMod
{
    internal static class LVMLocalization
    {
        private static Dictionary<string, Dictionary<string, string>> _data;

        private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Brazilian"]           = "Brazilian",
            ["Portuguese"]          = "Brazilian",
            ["Simplified Chinese"]  = "ChineseSimplified",
            ["Traditional Chinese"] = "ChineseTraditional",
        };

        private static Dictionary<string, Dictionary<string, string>> Data =>
            _data ??= Load();

        private static Dictionary<string, Dictionary<string, string>> Load()
        {
            Stream stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("LootVarianceMod.localization.json");
            if (stream == null)
            {
                MelonLogger.Warning("[LVM] localization.json not found in assembly — English only.");
                return new();
            }
            using var reader = new StreamReader(stream, Encoding.UTF8);
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(reader.ReadToEnd())
                    ?? new();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[LVM] localization.json parse error: {ex.Message}");
                return new();
            }
        }

        internal static string Get(string key)
        {
            string lang = Localization.Language ?? "English";
            if (Aliases.TryGetValue(lang, out string mapped)) lang = mapped;

            var data = Data;
            if (data.TryGetValue(lang, out var langDict) && langDict.TryGetValue(key, out var val))
                return val;
            if (data.TryGetValue("English", out var enDict) && enDict.TryGetValue(key, out var enVal))
                return enVal;
            return key;
        }
    }
}
