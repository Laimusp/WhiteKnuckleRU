using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace WKRusHelper
{
    // Лёгкий плагин-хелпер рендера:
    //  1) авто-уменьшение шрифта ТОЛЬКО для коротких экранных UI-меток (3D world-space НЕ трогаем);
    //  2) ПОРТАТИВНАЯ загрузка кириллического шрифта.
    //
    // Зачем (2): XUAT грузит OverrideFontTextMeshPro строго из Paths.GameRoot (декомпил FontHelper.GetTextMeshProFont:
    //  Path.Combine(Paths.GameRoot, assetBundle)). При установке через мод-менеджер (r2modman/Gale) мод лежит в
    //  профиле, а Paths.GameRoot = реальная папка Steam, куда менеджер не пишет -> шрифт не найдётся -> кириллица = квадраты.
    //  Решение: если шрифта НЕТ в корне игры, грузим oswald_ru_sdf сами из папки плагина и добавляем в ГЛОБАЛЬНЫЙ
    //  TMP-fallback (TMP_Settings.fallbackFontAssets) — так кириллица рендерится у всех TMP-компонентов.
    //  Если шрифт В корне (ручная установка) — НЕ вмешиваемся: XUAT-override работает как раньше, ноль регрессии
    //  и нет конфликта двойной загрузки одного бандла.
    [BepInPlugin("wk.rus.helper", "WK Rus Helper", "1.3.0")]
    public class Plugin : BaseUnityPlugin
    {
        const string FontName = "oswald_ru_sdf";
        static bool _fontDone;
        static AssetBundle _fontBundle;
        static TMP_FontAsset _fontAsset;

        void Awake()
        {
            try { new Harmony("wk.rus.helper").PatchAll(); }
            catch (Exception e) { Logger.LogError("[WKRus] autosize patch failed: " + e.Message); }
            try { StartCoroutine(EnsureCyrillicFont()); }
            catch (Exception e) { Logger.LogError("[WKRus] font coroutine start failed: " + e.Message); }
            Logger.LogInfo("[WKRus] v1.3 loaded (autosize + portable cyrillic font fallback)");
        }

        IEnumerator EnsureCyrillicFont()
        {
            if (_fontDone) yield break;

            // (A) Ручная установка: бандл лежит в корне игры -> XUAT сам делает override как раньше. Не вмешиваемся.
            string atRoot = Path.Combine(Paths.GameRootPath, FontName);
            if (File.Exists(atRoot))
            {
                _fontDone = true;
                Logger.LogInfo("[WKRus] font at game root -> XUAT override handles it; fallback inject skipped");
                yield break;
            }

            // (B) Мод-менеджер / lite: ищем бандл рядом с плагином.
            string bundlePath = FindFontBundle();
            if (bundlePath == null)
            {
                Logger.LogWarning("[WKRus] cyrillic font '" + FontName + "' not found near plugin; cyrillic may not render");
                yield break;
            }

            // Ждём инициализацию TMP_Settings (подгружается из Resources при первом обращении).
            float t = 0f;
            while (TMP_Settings.fallbackFontAssets == null && t < 15f)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            var list = TMP_Settings.fallbackFontAssets;
            if (list == null)
            {
                Logger.LogWarning("[WKRus] TMP_Settings.fallbackFontAssets unavailable; cannot inject cyrillic font");
                yield break;
            }

            // Загрузка бандла и инъекция в глобальный fallback (без yield внутри try/catch).
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

        // Бандл рядом с DLL (обычная раскладка) либо где-то в BepInEx/plugins (страховка).
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
    }

    // авто-уменьшение шрифта ТОЛЬКО для коротких экранных UI-меток.
    // 3D world-space текст (TextMeshPro / WorldSpace Canvas) НЕ трогаем — autoSize ломает узкие таблички.
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
