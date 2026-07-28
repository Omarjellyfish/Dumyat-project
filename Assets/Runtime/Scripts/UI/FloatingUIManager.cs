using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    public class FloatingUIManager : MonoBehaviour, IFloatingTextService
    {
        [SerializeField] private FloatingText _floatingTextPrefab;
        [SerializeField] private int _initialPoolSize = 5;

        private Queue<FloatingText> _availablePool = new Queue<FloatingText>();
        private List<FloatingText> _activeInstances = new List<FloatingText>();
        [SerializeField ] Vector3 offset;

        private void Awake()
        {
            if(!_floatingTextPrefab)
            {
                Debug.LogError("FloatingTextPrefab is not assigned");
                return ;
            }
            for (int i = 0; i < _initialPoolSize; i++)
                CreatePooledInstance();
        }

        private FloatingText CreatePooledInstance()
        {
            FloatingText instance = Instantiate(_floatingTextPrefab, transform);
            instance.Hide();
            _availablePool.Enqueue(instance);
            return instance;
        }

        public FloatingText Show(Vector3 worldPosition, string text)
        {
            FloatingText instance = _availablePool.Count > 0
                ? _availablePool.Dequeue()
                : CreatePooledInstance();

            instance.SetPosition(worldPosition ,offset);
            instance.SetText(text);
            instance.Show();

            _activeInstances.Add(instance);
            return instance;
        }

        public void Hide(FloatingText instance)
        {
            if (instance == null) return;

            instance.Hide();
            _activeInstances.Remove(instance);
            _availablePool.Enqueue(instance);
        }
    }
}