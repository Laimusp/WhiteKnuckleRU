using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace WKRusHelper
{
    // Хелпер рендера и ПЕРЕНОСИМОСTИ (manual + любой мод-менеджер):
    //  1) авто-уменьшение шрифта ТОЛЬКО для коротких экранных UI-меток (3D world-space НЕ трогаем);
    //  2) портативная загрузка кириллического шрифта (если шрифта нет в корне игры — грузим сами в TMP-fallback);
    //  3) портативный путь к словарю XUAT.
    //
    // Зачем (3): имя папки мода под мод-менеджером НЕпредсказуемо (r2modman локальный импорт -> "<Name>-<Name>",
    // каталог -> "<Namespace>-<Name>"), а в конфиге XUAT путь Directory прописан жёстко -> XUAT не найдёт словарь
    // и почти весь текст останется английским. Наши плагины-то находят файлы рядом с DLL (Assembly.Location,
    // имя-независимо), а XUAT — нет. Решение: ПОСЛЕ инициализации XUAT (BepInDependency -> грузимся после него)
    // указываем ему директорию переводов = НАША папка и дёргаем reload. Если XUAT уже смотрит туда
    // (ручная установка / каталог, где имя совпало) — ничего не делаем (без лишнего reload/мигания).
    [BepInPlugin("wk.rus.helper", "WK Rus Helper", "1.4.0")]
    [BepInDependency("gravydevsupreme.xunity.autotranslator", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        const string FontName = "oswald_ru_sdf";
        static bool _fontDone;
        static bool _pathDone;
        static AssetBundle _fontBundle;
        static TMP_FontAsset _fontAsset;

        void Awake()
        {
            try { new Harmony("wk.rus.helper").PatchAll(); }
            catch (Exception e) { Logger.LogError("[WKRus] autosize patch failed: " + e.Message); }
            try { StartCoroutine(EnsureCyrillicFont()); }
            catch (Exception e) { Logger.LogError("[WKRus] font coroutine start failed: " + e.Message); }
            try { StartCoroutine(FixXuatTranslationDir()); }
            catch (Exception e) { Logger.LogError("[WKRus] xuat-path coroutine start failed: " + e.Message); }
            Logger.LogInfo("[WKRus] v1.4 loaded (autosize + portable font + portable XUAT dict path)");
        }

        // ---- (2) ПОРТАТИВНЫЙ ШРИФТ ----
        IEnumerator EnsureCyrillicFont()
        {
            if (_fontDone) yield break;

            // Ручная установка: бандл в корне игры -> XUAT сам делает override как раньше. Не вмешиваемся.
            string atRoot = Path.Combine(Paths.GameRootPath, FontName);
            if (File.Exists(atRoot))
            {
                _fontDone = true;
                Logger.LogInfo("[WKRus] font at game root -> XUAT override handles it; fallback inject skipped");
                yield break;
            }

            string bundlePath = FindFontBundle();
            if (bundlePath == null)
            {
                Logger.LogWarning("[WKRus] cyrillic font '" + FontName + "' not found near plugin; cyrillic may not render");
                yield break;
            }

            // TMP_Settings.fallbackFontAssets геттер дёргает TMP_Settings.instance и может кинуть NRE,
            // если инстанс ещё не подгружен из Resources -> доступ под try внутри ожидания.
            List<TMP_FontAsset> list = null;
            float t = 0f;
            while (t < 15f)
            {
                try { list = TMP_Settings.fallbackFontAssets; } catch { list = null; }
                if (list != null) break;
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            if (list == null)
            {
                Logger.LogWarning("[WKRus] TMP_Settings.fallbackFontAssets unavailable; cannot inject cyrillic font");
                yield break;
            }

            bool ok = false;
            try
            {
                _fontBundle = AssetBundle.LoadFromFile(bundlePath);
                if (_fontBundle != null)
                {
                    _fontAsset = _fontBundle.LoadAllAssets<TMP_FontAsset>().FirstOrDefault();
                    if (_fontAsset != null)
                    {
                        UnityEngine.Object.DontDestroyOnLoad(_fontAsset);
                        if (!list.Contains(_fontAsset)) list.Add(_fontAsset);
                        ok = true;
                    }
                }
            }
            catch (Exception e) { Logger.LogError("[WKRus] font inject error: " + e); }

            if (ok)
            {
                _fontDone = true;
                Logger.LogInfo("[WKRus] injected cyrillic font '" + _fontAsset.name + "' into global TMP fallback (mod-manager path)");
            }
            else
            {
                Logger.LogError("[WKRus] could not inject cyrillic font from: " + bundlePath);
            }
        }

        static string FindFontBundle()
        {
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(dir))
                {
                    string p = Path.Combine(dir, FontName);
                    if (File.Exists(p)) return p;
                }
            }
            catch { }
            try
            {
                foreach (string f in Directory.GetFiles(Paths.PluginPath, FontName, SearchOption.AllDirectories))
                    return f;
            }
            catch { }
            return null;
        }

        // ---- (3) ПОРТАТИВНЫЙ ПУТЬ СЛОВАРЯ XUAT ----
        IEnumerator FixXuatTranslationDir()
        {
            if (_pathDone) yield break;

            // переводы рядом с нашей DLL?
            string textDir = null, dictFile = null;
            try
            {
                string mydir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(mydir))
                {
                    textDir = Path.Combine(mydir, "Translation", "ru", "Text");
                    dictFile = Path.Combine(textDir, "_AutoGeneratedTranslations.txt");
                }
            }
            catch (Exception e) { Logger.LogWarning("[WKRus] xuat-path resolve: " + e.Message); yield break; }
            if (dictFile == null || !File.Exists(dictFile))
            {
                Logger.LogInfo("[WKRus] no bundled dict next to plugin -> XUAT uses its own config path");
                yield break;
            }

            var tSettings = AccessTools.TypeByName("XUnity.AutoTranslator.Plugin.Core.Configuration.Settings");
            var tPlugin = AccessTools.TypeByName("XUnity.AutoTranslator.Plugin.Core.AutoTranslationPlugin");
            if (tSettings == null || tPlugin == null)
            {
                Logger.LogWarning("[WKRus] XUAT types not found -> skip dict-path fix");
                yield break;
            }
            var fTransPath = AccessTools.Field(tSettings, "TranslationsPath");
            var fAutoFile = AccessTools.Field(tSettings, "AutoTranslationsFilePath");
            var fCurrent = AccessTools.Field(tPlugin, "Current");
            // LoadTranslations(true) НАПРЯМУЮ, а НЕ ReloadTranslations() — последний сначала зовёт
            // PruneMainTranslationFile(), который при изменениях ПЕРЕПИСЫВАЕТ наш курируемый словарь
            // на диске (+ плодит .bak). Нам нужна только перезагрузка из новой папки.
            var mLoad = AccessTools.Method(tPlugin, "LoadTranslations", new Type[] { typeof(bool) });
            if (fTransPath == null || fCurrent == null || mLoad == null)
            {
                Logger.LogWarning("[WKRus] XUAT members not found -> skip dict-path fix");
                yield break;
            }

            // дождаться инициализации XUAT (Current != null)
            float t = 0f; object cur = null;
            while (t < 60f)
            {
                try { cur = fCurrent.GetValue(null); } catch { }
                if (cur != null) break;
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            if (cur == null) { Logger.LogWarning("[WKRus] XUAT not initialized -> skip dict-path fix"); yield break; }

            // уже смотрит в нашу папку? (ручная/каталог установка, имя совпало) -> не трогаем
            bool already = false;
            try
            {
                string curPath = fTransPath.GetValue(null) as string;
                if (!string.IsNullOrEmpty(curPath) &&
                    string.Equals(Path.GetFullPath(curPath), Path.GetFullPath(textDir), StringComparison.OrdinalIgnoreCase))
                    already = true;
            }
            catch { }
            if (already)
            {
                _pathDone = true;
                Logger.LogInfo("[WKRus] XUAT dict path already correct -> no override");
                yield break;
            }

            // переустановить ВСЕ пути переводов (dir + спец-файлы) и перезагрузить (без yield внутри try)
            try
            {
                fTransPath.SetValue(null, textDir);
                if (fAutoFile != null) fAutoFile.SetValue(null, dictFile);
                var fSub = AccessTools.Field(tSettings, "SubstitutionFilePath");
                var fPre = AccessTools.Field(tSettings, "PreprocessorsFilePath");
                var fPost = AccessTools.Field(tSettings, "PostprocessorsFilePath");
                if (fSub != null) fSub.SetValue(null, Path.Combine(textDir, "_Substitutions.txt"));
                if (fPre != null) fPre.SetValue(null, Path.Combine(textDir, "_Preprocessors.txt"));
                if (fPost != null) fPost.SetValue(null, Path.Combine(textDir, "_Postprocessors.txt"));
                mLoad.Invoke(cur, new object[] { true });
                _pathDone = true;
                Logger.LogInfo("[WKRus] redirected XUAT translation paths -> " + textDir + " (+reloaded)");
            }
            catch (Exception e) { Logger.LogError("[WKRus] xuat dict-path fix failed: " + e); }
        }
    }

    // авто-уменьшение шрифта ТОЛЬКО для коротких экранных UI-меток.
    [HarmonyPatch(typeof(TMP_Text), "set_text", new Type[] { typeof(string) })]
    static class AutoSize
    {
        static void Postfix(TMP_Text __instance)
        {
            if (__instance == null || __instance.enableAutoSizing) return;
            if (__instance is TextMeshPro) return;
            Canvas cv = __instance.canvas;
            if (cv == null || cv.renderMode == RenderMode.WorldSpace) return;
            string t = __instance.text;
            if (string.IsNullOrEmpty(t) || t.Length > 40 || t.Contains("<size")) return;
            float cur = __instance.fontSize;
            if (cur <= 0f) cur = 36f;
            __instance.enableAutoSizing = true;
            __instance.fontSizeMax = cur;
            __instance.fontSizeMin = Mathf.Max(8f, cur * 0.5f);
        }
    }
}
