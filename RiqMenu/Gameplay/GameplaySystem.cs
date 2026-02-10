using System;
using System.Collections.Concurrent;
using UnityEngine;
using RiqMenu.Core;
using RiqMenu.Patches;
using RiqMenu.UI;

namespace RiqMenu.Gameplay
{
    public class GameplaySystem : MonoBehaviour, IRiqMenuSystem {
        private static GameplaySystem _instance;
        public static GameplaySystem Instance => _instance;

        public bool IsActive { get; private set; }

        // Gameplay UI
        private AccuracyBar _accuracyBar;
        private ProgressBar _progressBar;

        // Thread-safe hit event queue (fed from native input thread via patches)
        public struct PendingHitEvent {
            public float delta;
            public Judgement judgement;
        }
        private readonly ConcurrentQueue<PendingHitEvent> _pendingHits = new ConcurrentQueue<PendingHitEvent>();
        private volatile Judgement _pendingAutoRestartJudgement = NO_PENDING_JUDGEMENT;
        private const Judgement NO_PENDING_JUDGEMENT = (Judgement)(-99);

        // Auto-restart state
        private bool _pendingRestart = false;
        private float _restartDelay = 0f;
        private bool _gameplayReady = false;
        private float _gameplayGraceTimer = 0f;
        private const float GAMEPLAY_GRACE_PERIOD = 1.0f;

        public bool GameplayReady => _gameplayReady;

        public void Initialize() {
            _instance = this;
            IsActive = true;
        }

        public void Cleanup() {
            IsActive = false;
        }

        public void Update() { }

        // Called from native input thread - thread safe
        public void EnqueueHit(float delta, Judgement judgement) {
            _pendingHits.Enqueue(new PendingHitEvent { delta = delta, judgement = judgement });
        }

        // Called from native input thread - thread safe
        public void SignalAutoRestart(Judgement judgement) {
            _pendingAutoRestartJudgement = judgement;
        }

        public void HideGameplayUI() {
            _accuracyBar?.Hide();
            _progressBar?.Hide();
        }

        // JukeboxScript.Play postfix logic
        public void OnSongStarted() {
            if (RiqMenuState.IsQuitting || RiqMenuState.IsInGameEditor()) return;

            RiqMenuState.IsTransitioning = false;
            _pendingRestart = false;
            _restartDelay = 0f;

            var sceneKey = TempoSceneManager.GetActiveSceneKey();
            bool isGameplay = TempoSceneManager.IsGameScene(sceneKey) ||
                              TempoSceneManager.IsTutorialScene(sceneKey) ||
                              sceneKey == SceneKey.RiqLoader ||
                              sceneKey == SceneKey.MixtapeCustom ||
                              RiqMenuState.CurrentScene.name.Contains("Mixtape");

            if (isGameplay) {
                _gameplayReady = false;
                _gameplayGraceTimer = GAMEPLAY_GRACE_PERIOD;
            } else {
                _gameplayReady = false;
                _gameplayGraceTimer = 0f;
            }

            if (!isGameplay)
                return;

            if (RiqMenuSettings.AccuracyBarEnabled) {
                InitializeAccuracyBar();
                _accuracyBar?.Show();
                _accuracyBar?.ClearIndicators();
            }

            if (RiqMenuSettings.ProgressBarEnabled) {
                InitializeProgressBar();
                _progressBar?.Show();
            }
        }

        // JudgementScript.Play prefix logic
        public void OnOutroStarted() {
            Debug.Log("[RiqMenu] JudgementScript.Play - hiding gameplay UI");
            HideGameplayUI();
        }

        // Called by RiqMenuMain when a scene loads
        public void OnSceneChanged(UnityEngine.SceneManagement.Scene scene) {
            RiqMenuState.IsTransitioning = false;
            _gameplayReady = false;
            _gameplayGraceTimer = 0f;
            GameplayPatches.ResetMissState();

            // Flush stale input events
            PendingHitEvent discard;
            while (_pendingHits.TryDequeue(out discard)) { }
            _pendingAutoRestartJudgement = NO_PENDING_JUDGEMENT;

            if (RiqMenuState.IsMenuScene(scene.name)) {
                HideGameplayUI();
                StopGameMusic();
            }
        }

        private void OnApplicationQuit() {
            RiqMenuState.IsQuitting = true;
            RiqMenuState.IsTransitioning = true;
            _pendingRestart = false;
            _gameplayReady = false;
        }

        private void LateUpdate() {
            if (RiqMenuState.IsQuitting) return;
            try {
                // Drain hit events queued from the input thread
                PendingHitEvent hitEvent;
                while (_pendingHits.TryDequeue(out hitEvent)) {
                    if (RiqMenuState.IsTransitioning || !_gameplayReady) continue;
                    _accuracyBar?.RegisterHit(hitEvent.delta, hitEvent.judgement);
                }

                Judgement restartJudgement = _pendingAutoRestartJudgement;
                if (restartJudgement != NO_PENDING_JUDGEMENT) {
                    _pendingAutoRestartJudgement = NO_PENDING_JUDGEMENT;
                    CheckAutoRestart(restartJudgement);
                }

                if (!RiqMenuState.IsTransitioning && !_gameplayReady && _gameplayGraceTimer > 0) {
                    _gameplayGraceTimer -= Time.deltaTime;
                    if (_gameplayGraceTimer <= 0) {
                        _gameplayReady = true;
                        Debug.Log("[RiqMenu] Gameplay ready - auto-restart now active");
                    }
                }

                if (_pendingRestart && _restartDelay > 0) {
                    _restartDelay -= Time.deltaTime;
                    if (_restartDelay <= 0) {
                        _pendingRestart = false;
                        ExecuteRestart();
                    }
                }
            } catch {
            }
        }

        private void CheckAutoRestart(Judgement judgement) {
            if (RiqMenuState.IsQuitting || RiqMenuState.IsTransitioning || RiqMenuState.IsInGameEditor()) return;

            try {
                var mode = RiqMenuSettings.AutoRestartMode;
                if (mode == AutoRestartMode.Off) return;
                if (_pendingRestart) return;

                var sceneKey = TempoSceneManager.GetActiveSceneKey();
                if (TempoSceneManager.IsTutorialScene(sceneKey) || RiqMenuState.CurrentScene.name.Contains("Tutorial")) {
                    return;
                }

                bool shouldRestart = false;
                if (mode == AutoRestartMode.OnMiss && (judgement == Judgement.Miss || judgement == Judgement.Bad)) {
                    shouldRestart = true;
                }
                else if (mode == AutoRestartMode.OnNonPerfect && judgement != Judgement.Perfect) {
                    shouldRestart = true;
                }

                if (shouldRestart) {
                    RiqMenuState.IsTransitioning = true;
                    _gameplayReady = false;
                    _pendingRestart = true;
                    _restartDelay = 0.3f;

                    HideGameplayUI();

                    Debug.Log($"[RiqMenu] Auto-restart triggered: {mode}, judgement={judgement}");
                }
            } catch (Exception) {
            }
        }

        private void ExecuteRestart() {
            RiqMenuState.IsTransitioning = true;
            _gameplayReady = false;

            try {
                var sceneKey = TempoSceneManager.GetActiveSceneKey();

                if (!TempoSceneManager.IsGameScene(sceneKey) &&
                    sceneKey != SceneKey.RiqLoader &&
                    sceneKey != SceneKey.MixtapeCustom &&
                    !RiqMenuState.CurrentScene.name.Contains("Mixtape")) {
                    return;
                }

                _accuracyBar?.Hide();

                SceneKey restartScene = sceneKey;
                if (sceneKey == SceneKey.MixtapeCustom) {
                    restartScene = SceneKey.RiqLoader;
                }

                JudgementScript.RestartStage();

                var quitter = FindObjectOfType<Quitter>();
                if (quitter != null) {
                    quitter.PreventQuit = false;
                    Debug.Log($"[RiqMenu] Restarting via Quitter: {restartScene}");
                    quitter.Quit(0.1f, 0.25f, 0.75f, restartScene);
                } else {
                    Debug.Log($"[RiqMenu] Restarting via LoadSceneAsync (no Quitter): {restartScene}");
                    TempoSceneManager.LoadSceneAsync(restartScene, 0.1f);
                }
            } catch (Exception ex) {
                Debug.LogWarning($"[RiqMenu] ExecuteRestart failed: {ex.Message}");
            }
        }

        private void InitializeAccuracyBar() {
            if (_accuracyBar == null) {
                var go = new GameObject("RiqMenu_AccuracyBar");
                _accuracyBar = go.AddComponent<AccuracyBar>();
                DontDestroyOnLoad(go);
            }
        }

        private void InitializeProgressBar() {
            if (_progressBar == null) {
                var go = new GameObject("RiqMenu_ProgressBar");
                _progressBar = go.AddComponent<ProgressBar>();
                DontDestroyOnLoad(go);
            }
        }

        private static void StopGameMusic() {
            try {
                var jukebox = FindObjectOfType<JukeboxScript>();
                if (jukebox != null && jukebox.IsPlaying) {
                    jukebox.Stop();
                    Debug.Log("[RiqMenu] Stopped game music on scene transition");
                }
            } catch (Exception ex) {
                Debug.LogWarning($"[RiqMenu] Failed to stop game music: {ex.Message}");
            }
        }
    }
}
