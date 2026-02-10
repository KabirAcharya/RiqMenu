using System.Reflection;
using HarmonyLib;

namespace RiqMenu.Patches
{
    /// <summary>
    /// Fixes TempoSound.IsPlaying crashing on invalid sound IDs.
    /// </summary>
    internal static class AudioFixPatches
    {
        [HarmonyPatch(typeof(TempoSound), "IsPlaying", MethodType.Getter)]
        private static class TempoSoundIsPlayingPatch {
            private static bool Prefix(TempoSound __instance, ref bool __result) {
                var idField = __instance.GetType().GetField("id", BindingFlags.NonPublic | BindingFlags.Instance);
                if (idField != null) {
                    uint id = (uint)idField.GetValue(__instance);
                    if (id == uint.MaxValue) {
                        __result = false;
                        return false;
                    }
                }
                return true;
            }
        }
    }
}
