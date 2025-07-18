using System.Collections.Generic;
using UnityEngine;
using RiqMenu.Audio;
using RiqMenu.Songs;
using RiqMenu.UI;
using RiqMenu.Input;
using RiqMenu.Cache;

namespace RiqMenu.Core
{
    public class RiqMenuSystemManager : MonoBehaviour
    {
        private static RiqMenuSystemManager _instance;
        public static RiqMenuSystemManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("RiqMenuSystemManager");
                    _instance = go.AddComponent<RiqMenuSystemManager>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        private List<IRiqMenuSystem> _systems = new List<IRiqMenuSystem>();

        public SongManager SongManager { get; private set; }
        public AudioManager AudioManager { get; private set; }
        public UIManager UIManager { get; private set; }
        public RiqMenu.Input.RiqInputManager InputManager { get; private set; }
        public AudioPreloader AudioPreloader { get; private set; }

        private bool _isInitialized = false;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void InitializeSystems()
        {
            if (_isInitialized) return;

            AudioPreloader = gameObject.AddComponent<AudioPreloader>();
            SongManager = gameObject.AddComponent<SongManager>();
            AudioManager = gameObject.AddComponent<AudioManager>();
            InputManager = gameObject.AddComponent<RiqMenu.Input.RiqInputManager>();
            UIManager = gameObject.AddComponent<UIManager>();

            _systems.Add(AudioPreloader);
            _systems.Add(SongManager);
            _systems.Add(AudioManager);
            _systems.Add(InputManager);
            _systems.Add(UIManager);

            foreach (var system in _systems)
            {
                system.Initialize();
            }

            _isInitialized = true;
        }

        private void Update()
        {
            if (!_isInitialized) return;

            foreach (var system in _systems)
            {
                if (system.IsActive)
                {
                    system.Update();
                }
            }
        }

        public void OnDestroy()
        {
            foreach (var system in _systems)
            {
                system?.Cleanup();
            }
            _systems.Clear();
        }

        public T GetSystem<T>() where T : class, IRiqMenuSystem
        {
            foreach (var system in _systems)
            {
                if (system is T targetSystem)
                {
                    return targetSystem;
                }
            }
            return null;
        }
    }
}