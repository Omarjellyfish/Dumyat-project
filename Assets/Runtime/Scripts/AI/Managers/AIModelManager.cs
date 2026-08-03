using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Shared base for the AI model managers (LLM / TTS / STT). Owns the pool of models of one type
    /// and the common machinery: a typed singleton, registration, and priority-ordered access. Each
    /// concrete manager (<see cref="LLMManager"/>, <see cref="TTSManager"/>, <see cref="STTManager"/>)
    /// adds only the provider-agnostic API for its model type on top.
    ///
    /// Models are discovered automatically at <see cref="Start"/> (any scene <see cref="MonoBehaviour"/>
    /// implementing <typeparamref name="TModel"/> joins the pool), and can also be added/removed at
    /// runtime via <see cref="RegisterModel"/>/<see cref="UnregisterModel"/>. Models stay fully
    /// decoupled — they implement the interface and never reference the manager.
    ///
    /// The concrete managers are registered against their capability interface (ILLMService /
    /// ITTSService / ISTTService) by <see cref="AISceneInstaller"/> and injected by consumers — there
    /// is no static accessor.
    /// </summary>
    public abstract class AIModelManager<TManager, TModel> : MonoBehaviour
        where TManager : AIModelManager<TManager, TModel>
        where TModel : class, IAIModel
    {
        private readonly List<TModel> _models = new List<TModel>();

        /// <summary>All registered models, highest priority first.</summary>
        public IReadOnlyList<TModel> Models => _models;

        /// <summary>True when at least one registered model reports itself available.</summary>
        public bool HasAvailableModel
        {
            get
            {
                foreach (TModel model in _models)
                    if (model != null && model.IsAvailable) return true;
                return false;
            }
        }

        protected virtual void Start()
        {
            DiscoverModels();
        }

        /// <summary>Adds a model to the pool (idempotent) and keeps the pool sorted by priority.</summary>
        public void RegisterModel(TModel model)
        {
            if (model == null || _models.Contains(model)) return;
            _models.Add(model);
            _models.Sort((a, b) => b.Priority.CompareTo(a.Priority)); // higher priority first
            Debug.Log($"{GetType().Name}: registered model '{model.ModelName}' (priority {model.Priority}).");
        }

        /// <summary>Removes a model from the pool.</summary>
        public void UnregisterModel(TModel model)
        {
            if (model != null) _models.Remove(model);
        }

        /// <summary>Iterates currently-available models, highest priority first.</summary>
        protected IEnumerable<TModel> AvailableModels()
        {
            foreach (TModel model in _models)
                if (model != null && model.IsAvailable) yield return model;
        }

        private void DiscoverModels()
        {
            MonoBehaviour[] behaviours =
                FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (MonoBehaviour mb in behaviours)
                if (mb is TModel model) RegisterModel(model);

            if (_models.Count == 0)
                Debug.LogWarning($"{GetType().Name}: no models discovered. " +
                                 "Add a model component to the scene or RegisterModel() at runtime.");
        }
    }
}
