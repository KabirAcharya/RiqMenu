using System;
using HarmonyLib;
using RiqMenu.Core;
using RiqMenu.Gameplay;

namespace RiqMenu.Patches
{
    /// <summary>
    /// Patches for capturing gameplay events (hits, misses, song start/end).
    /// Thin wrappers that delegate to GameplaySystem for processing.
    /// </summary>
    internal static class GameplayPatches
    {
        // Called by GameplaySystem.OnSceneChanged to reset miss tracking
        public static void ResetMissState() {
            JudgeHandleMissesPatch._lastMissCount = -1;
        }

        // Runs on native input thread via TempoManager.OnButtonDown
        [HarmonyPatch(typeof(Judge), "JudgeInput")]
        private static class JudgeInputPatch {
            private static void Postfix(ValueTuple<float, Judgement, float, bool> __result) {
                if (RiqMenuState.IsQuitting || RiqMenuState.IsTransitioning || RiqMenuState.IsInGameEditor()) return;

                var gameplay = GameplaySystem.Instance;
                if (gameplay == null || !gameplay.GameplayReady) return;

                try {
                    float target = __result.Item1;
                    Judgement judgement = __result.Item2;
                    float delta = __result.Item3;
                    bool taken = __result.Item4;

                    bool hasBeat = !float.IsInfinity(target);

                    if (taken) {
                        gameplay.EnqueueHit(delta, judgement);
                        gameplay.SignalAutoRestart(judgement);
                    }
                    else if (hasBeat && judgement == Judgement.Miss) {
                        gameplay.EnqueueHit(delta, Judgement.Miss);
                        gameplay.SignalAutoRestart(Judgement.Miss);
                    }
                } catch (Exception) {
                }
            }
        }

        // Runs on native input thread via TempoManager.OnButtonDown
        [HarmonyPatch(typeof(Judge), "HandleMisses", new Type[] { typeof(BeatQueue), typeof(double), typeof(global::Action), typeof(Judge.OnMiss) })]
        internal static class JudgeHandleMissesPatch {
            internal static int _lastMissCount = -1;
            private const float MISS_DISPLAY_DELTA = 0.2f;

            private static void Prefix(Judge __instance) {
                if (RiqMenuState.IsQuitting || RiqMenuState.IsTransitioning || RiqMenuState.IsInGameEditor()) return;
                try {
                    if (__instance == null || __instance.implicitJudgements == null) {
                        _lastMissCount = -1;
                        return;
                    }
                    _lastMissCount = __instance.implicitJudgements[Judgement.Miss];
                } catch (Exception) {
                    _lastMissCount = -1;
                }
            }

            private static void Postfix(Judge __instance) {
                if (RiqMenuState.IsQuitting || RiqMenuState.IsTransitioning || RiqMenuState.IsInGameEditor()) return;

                var gameplay = GameplaySystem.Instance;
                if (gameplay == null || !gameplay.GameplayReady) return;

                try {
                    if (__instance == null || __instance.implicitJudgements == null) return;
                    if (_lastMissCount < 0) return;

                    int currentCount = __instance.implicitJudgements[Judgement.Miss];
                    int newMisses = currentCount - _lastMissCount;

                    if (newMisses > 0) {
                        for (int i = 0; i < newMisses; i++) {
                            gameplay.EnqueueHit(MISS_DISPLAY_DELTA, Judgement.Miss);
                        }
                        gameplay.SignalAutoRestart(Judgement.Miss);
                    }
                } catch (Exception) {
                }
            }
        }

        [HarmonyPatch(typeof(JukeboxScript), "Play")]
        private static class JukeboxScriptPlayPatch {
            private static void Postfix() {
                GameplaySystem.Instance?.OnSongStarted();
            }
        }

        [HarmonyPatch(typeof(JudgementScript), "Play")]
        private static class JudgementScriptPlayPatch {
            private static void Prefix() {
                GameplaySystem.Instance?.OnOutroStarted();
            }
        }
    }
}
