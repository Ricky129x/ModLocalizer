using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;

namespace ModLocalizer
{
    [ModInitializer("Initialize")]
    public static class TranslatorMain
    {
        public const string MOD_ID = "ModLocalizer";
        public const string PACK_MARKER = "modlocalizer.pack";

        public static Logger Logger { get; } = new Logger(MOD_ID, LogType.Generic);

        internal static string ModDirectory { get; private set; } = string.Empty;

        private static Harmony? _harmony;
        private static readonly HashSet<string> _packRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal static string? LastLanguage { get; set; }

        public static void Initialize()
        {
            ModDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                           ?? AppDomain.CurrentDomain.BaseDirectory;

            Logger.Info($"[{MOD_ID}] Initializing. Mod dir: {ModDirectory}");

            _harmony = new Harmony(MOD_ID);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());

            Logger.Info($"[{MOD_ID}] Harmony patches applied.");
        }

        private static void ScanTranslationPacks()
        {
            try
            {
                foreach (Mod mod in ModManager.Mods)
                {
                    if (File.Exists(Path.Combine(mod.path, PACK_MARKER)))
                    {
                        string root = Path.Combine(mod.path, "translations");
                        if (Directory.Exists(root) && _packRoots.Add(root))
                            Logger.Info($"[{MOD_ID}] Translation pack found: {mod.path}");
                    }
                }
                Logger.Info($"[{MOD_ID}] Scan complete. {_packRoots.Count} pack(s) found.");
            }
            catch (Exception ex)
            {
                Logger.Error($"[{MOD_ID}] Error scanning translation packs: {ex}");
            }
        }

        internal static void InjectTranslations(LocManager locManager, string language)
        {
            ScanTranslationPacks();

            // クラシック: translations/<mod名>/<lang>/<table>.json
            string classicRoot = Path.Combine(ModDirectory, "translations");
            if (Directory.Exists(classicRoot))
            {
                foreach (string modDir in Directory.GetDirectories(classicRoot))
                    InjectFromRoot(locManager, language, modDir);
            }

            foreach (string root in _packRoots)
                InjectFromRoot(locManager, language, root);
        }

        // 構造: <root>/<lang>/<table>.json
        private static void InjectFromRoot(LocManager locManager, string language, string translationsRoot)
        {
            string langRoot = Path.Combine(translationsRoot, language);
            if (!Directory.Exists(langRoot))
            {
                Logger.Info($"[{MOD_ID}] No '{language}' folder: {langRoot}");
                return;
            }

            foreach (string jsonPath in Directory.GetFiles(langRoot, "*.json", SearchOption.TopDirectoryOnly))
            {
                    string tableName = Path.GetFileNameWithoutExtension(jsonPath);

                    try
                    {
                        string content = File.ReadAllText(jsonPath, Encoding.UTF8);
                        Dictionary<string, string>? entries =
                            JsonSerializer.Deserialize<Dictionary<string, string>>(content);

                        if (entries == null || entries.Count == 0)
                        {
                            Logger.Warn($"[{MOD_ID}] Empty or invalid JSON: {jsonPath}");
                            continue;
                        }

                        LocTable table = locManager.GetTable(tableName);
                        table.MergeWith(entries);

                        Logger.Info($"[{MOD_ID}] lang='{language}' table='{tableName}' -> {entries.Count} entries injected. ({jsonPath})");
                    }
                    catch (LocException)
                    {
                        Logger.Warn($"[{MOD_ID}] Table '{tableName}' not found for lang='{language}'. Skipping.");
                    }
                    catch (JsonException je)
                    {
                        Logger.Error($"[{MOD_ID}] JSON parse error in '{jsonPath}': {je.Message}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"[{MOD_ID}] Unexpected error loading '{jsonPath}': {ex}");
                    }
            }
        }
    }

    [HarmonyPatch(typeof(LocManager), "SetLanguageInternal")]
    internal static class SetLanguagePatch
    {
        [HarmonyPostfix]
        private static void Postfix(LocManager __instance, string language)
        {
            try
            {
                TranslatorMain.LastLanguage = language;
                TranslatorMain.InjectTranslations(__instance, language);
            }
            catch (Exception ex)
            {
                TranslatorMain.Logger.Error($"[{TranslatorMain.MOD_ID}] Patch error: {ex}");
            }
        }
    }

    // BaseLib が ModelDb.Init で ENG モデルデータを注入した後に再注入する
    [HarmonyPatch(typeof(ModelDb), "Init")]
    internal static class ModelDbPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            try
            {
                if (string.IsNullOrEmpty(TranslatorMain.LastLanguage)) return;

                LocManager? locManager = LocManager.Instance;
                if (locManager == null) return;

                TranslatorMain.InjectTranslations(locManager, TranslatorMain.LastLanguage);
            }
            catch (Exception ex)
            {
                TranslatorMain.Logger.Error($"[{TranslatorMain.MOD_ID}] ModelDb patch error: {ex}");
            }
        }
    }
}
