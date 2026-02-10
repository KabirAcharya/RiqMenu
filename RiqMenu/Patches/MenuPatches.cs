using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RiqMenu.Core;

namespace RiqMenu.Patches
{
    /// <summary>
    /// Patches for integrating "Custom Songs" into the title screen and stage select menus.
    /// </summary>
    internal static class MenuPatches
    {
        [HarmonyPatch(typeof(TitleScript), "Awake", new Type[0])]
        private static class TitleScriptAwakePatch {
            private static void Postfix(TitleScript __instance) {
                FieldInfo prop = __instance.GetType().GetField("optionStrings", BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop == null) return;
                var options = (List<string>)prop.GetValue(__instance);
                if (options == null) return;
                if (!options.Contains("Custom Songs")) {
                    int insertIndex = Math.Min(1, options.Count);
                    options.Insert(insertIndex, "Custom Songs");
                    prop.SetValue(__instance, options);
                }
            }
        }

        [HarmonyPatch(typeof(TitleScript), "Update", new Type[0])]
        private static class TitleScriptUpdatePatch {
            private static bool Prefix(TitleScript __instance) {
                if (RiqMenuState.IsOverlayVisible())
                    return false;

                FieldInfo prop = __instance.GetType().GetField("selection", BindingFlags.NonPublic | BindingFlags.Instance);
                int selected = (int)prop.GetValue(__instance);

                FieldInfo prop2 = __instance.GetType().GetField("optionStrings", BindingFlags.NonPublic | BindingFlags.Instance);
                List<string> options = (List<string>)prop2.GetValue(__instance);

                if (TempoInput.GetActionDown<global::Action>(global::Action.Confirm)) {
                    if (selected < options.Count && options[selected] == "Custom Songs") {
                        if (!RiqMenuState.IsOverlayVisible()) {
                            RiqMenuMain.Instance.ToggleCustomSongsOverlay();
                        }
                        return false;
                    }
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(StageSelectScript), "Update", new Type[0])]
        private static class StageSelectScriptUpdatePatch {
            private static bool Prefix(StageSelectScript __instance) {
                if (RiqMenuState.IsOverlayVisible())
                    return false;
                return true;
            }
        }

        [HarmonyPatch(typeof(LocalisationStatic), "_", new Type[] { typeof(string) })]
        private static class LocalisationStaticPatch {
            private static bool Prefix(string text, ref LocalisedString __result) {
                if (text == "Custom Songs") {
                    __result = new LocalisedString("Custom Songs");
                    return false;
                }
                return true;
            }
        }
    }
}
