using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Project.Presentation.UI
{
    [ExecuteAlways]
    [RequireComponent(typeof(UIDocument))]
    public class PresentationUIPopulator : MonoBehaviour
    {
        [System.Serializable]
        public class PopulationRule
        {
            public string ContainerName;
            public VisualTreeAsset ItemTemplate;
            public int Count = 5;
        }

        public List<PopulationRule> Rules = new List<PopulationRule>();
        
        [Tooltip("CSS classes to remove from the root element (e.g., hidden classes)")]
        public List<string> ClassesToRemove = new List<string>();

        private UIDocument _uiDocument;

        private void OnEnable()
        {
            Populate();
        }

        public void Populate()
        {
            if (_uiDocument == null) _uiDocument = GetComponent<UIDocument>();
            if (_uiDocument == null || _uiDocument.rootVisualElement == null) return;

            // Remove any hidden classes so the UI is visible in the showcase
            foreach (var className in ClassesToRemove)
            {
                var elements = _uiDocument.rootVisualElement.Query(className: className).Build();
                foreach (var element in elements)
                {
                    element.RemoveFromClassList(className);
                }
            }

            foreach (var rule in Rules)
            {
                if (rule.ItemTemplate == null || string.IsNullOrEmpty(rule.ContainerName)) continue;

                var container = _uiDocument.rootVisualElement.Q<VisualElement>(rule.ContainerName);
                if (container != null)
                {
                    container.Clear();
                    for (int i = 0; i < rule.Count; i++)
                    {
                        var instance = rule.ItemTemplate.Instantiate();
                        // Optional dummy data filling based on naming convention
                        var titleLabel = instance.Q<Label>(className: "item-title") ?? instance.Q<Label>(className: "title");
                        if (titleLabel != null) titleLabel.text = $"Sample Item {i + 1}";
                        
                        container.Add(instance);
                    }
                }
            }
        }
    }
}
