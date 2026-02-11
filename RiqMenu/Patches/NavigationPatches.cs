using System;
using HarmonyLib;
using UnityEngine;
using RiqMenu.Core;
using RiqMenu.Gameplay;

namespace RiqMenu.Patches
{
    /// <summary>
    /// Patches for redirecting scene navigation when songs are launched from RiqMenu.
    /// </summary>
    internal static class NavigationPatches
    {
        [HarmonyPatch(typeof(Quitter), "Quit")]
        private static class QuitterQuitPatch {
            private static void Prefix(Quitter __instance, ref SceneKey? quitScene) {
                if (__instance.PreventQuit)
                    return;

                // Prevents judge patches from running during scene teardown
                RiqMenuState.IsTransitioning = true;

                Debug.Log($"[RiqMenu] Quitter.Quit called: quitScene={quitScene}, LaunchedFromRiqMenu={RiqMenuState.LaunchedFromRiqMenu}");

                if (quitScene.HasValue && quitScene.Value == SceneKey.RiqLoader)
                    return;

                if (quitScene.HasValue &&
                    (quitScene.Value == SceneKey.StageSelect || quitScene.Value == SceneKey.TitleScreen)) {
                    var gameplay = GameplaySystem.Instance;
                    if (gameplay != null) gameplay.HideGameplayUI();
                }

                if (RiqMenuState.LaunchedFromRiqMenu && quitScene.HasValue && quitScene.Value == SceneKey.StageSelect) {
                    quitScene = SceneKey.TitleScreen;
                    Debug.Log("[RiqMenu] Redirecting to TitleScreen");
                }
            }
        }

        [HarmonyPatch(typeof(TempoSceneManager), "LoadSceneAsync", new Type[] { typeof(SceneKey), typeof(float) })]
        private static class TempoSceneManagerLoadSceneAsyncPatch {
            private static void Prefix(ref SceneKey scene) {
                RiqMenuState.IsTransitioning = true;

                Debug.Log($"[RiqMenu] LoadSceneAsync called: scene={scene}, LaunchedFromRiqMenu={RiqMenuState.LaunchedFromRiqMenu}");

                if (scene == SceneKey.RiqLoader)
                    return;

                if (RiqMenuState.LaunchedFromRiqMenu && scene == SceneKey.StageSelect) {
                    scene = SceneKey.TitleScreen;
                    Debug.Log("[RiqMenu] Redirecting LoadSceneAsync to TitleScreen");
                }
            }
        }

        [HarmonyPatch(typeof(RiqLoader), "Awake", new Type[0])]
        private static class RiqLoaderAwakePatch {
            private static void Postfix() {
                if (!string.IsNullOrEmpty(RiqMenuState.LastLoadedSongPath) && string.IsNullOrEmpty(RiqLoader.path)) {
                    RiqLoader.path = RiqMenuState.LastLoadedSongPath;
                    Debug.Log($"[RiqMenu] Restored song path for restart: {RiqMenuState.LastLoadedSongPath}");
                }
                else if (!string.IsNullOrEmpty(RiqLoader.path)) {
                    RiqMenuState.LastLoadedSongPath = RiqLoader.path;
                    Debug.Log($"[RiqMenu] Stored song path: {RiqMenuState.LastLoadedSongPath}");
                }
            }
        }
    }
}
