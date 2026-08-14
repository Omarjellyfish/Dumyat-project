using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatRuntime
{
    /// <summary>
    /// Mock panel content expressed entirely as data: a set of named labels to fill, and a set of row
    /// templates to clone into named containers.
    ///
    /// That pair covers every panel in this project. Several — the quest tracker, the achievements
    /// grid, the completed-quests list — are nothing but an empty container in their UXML, because the
    /// rows are a separate template the presenter clones at runtime. Doing the same here is what makes
    /// those panels show anything at all.
    /// </summary>
    [CreateAssetMenu(fileName = "MockData", menuName = "Dumyat/Presentation/Mock Panel Data")]
    public sealed class PanelMockDataSO : MockDataPopulatorSO
    {
        [System.Serializable]
        public struct LabelValue
        {
            [Tooltip("Element name in the UXML, e.g. \"WalletBalance\".")]
            public string ElementName;

            [Tooltip("Text to write. Authored in the case it should display in — USS has no " +
                     "text-transform.")]
            public string Text;
        }

        [System.Serializable]
        public class MockRow
        {
            [Tooltip("Fields to fill inside this one cloned row.")]
            public LabelValue[] Fields = new LabelValue[0];
        }

        [System.Serializable]
        public class RowGroup
        {
            [Tooltip("Container element the rows are cloned into, e.g. \"SlotContainer\".")]
            public string ContainerName;

            [Tooltip("The row template the real presenter would clone, e.g. InventorySlot.uxml.")]
            public VisualTreeAsset RowTemplate;

            [Tooltip("Clear whatever the UXML already has in the container first. Usually yes — the " +
                     "authored placeholder rows are not the mock data.")]
            public bool ClearFirst = true;

            public MockRow[] Rows = new MockRow[0];
        }

        [SerializeField, Tooltip("Named labels filled directly on the panel root.")]
        private LabelValue[] _labels = new LabelValue[0];

        [SerializeField, Tooltip("Row templates cloned into containers.")]
        private RowGroup[] _rowGroups = new RowGroup[0];

        [SerializeField, Tooltip("Element names to hide — 'no items yet' hints and empty-state text " +
                                 "that would otherwise sit on top of the mock rows.")]
        private string[] _hideElements = new string[0];

        [SerializeField, Tooltip("Element names to SHOW.\n\nSeveral panels author a readout with an " +
                                 "inline style=\"display: none\" and let their presenter reveal it — " +
                                 "the validation panel's fault counter is one. An inline style beats " +
                                 "any class, so the class-change list below cannot reach these; only " +
                                 "writing display back on the element can.")]
        private string[] _showElements = new string[0];

        [System.Serializable]
        public struct ClassChange
        {
            public string ElementName;

            [Tooltip("Class to add, e.g. \"tutorial-panel--visible\".")]
            public string AddClass;

            [Tooltip("Class to remove, e.g. \"tutorial-panel--hidden\".")]
            public string RemoveClass;
        }

        [SerializeField, Tooltip("Classes to add or remove.\n\nSeveral panels are authored HIDDEN and " +
                                 "made visible by their presenter at runtime — the tutorial panel's USS " +
                                 "sets scale 0 and opacity 0 on '--hidden'. Without flipping that class " +
                                 "the panel renders as an empty frame, which looks like a broken texture " +
                                 "rather than a panel doing what it was told.")]
        private ClassChange[] _classChanges = new ClassChange[0];

        public override void Apply(VisualElement root)
        {
            if (root == null) return;

            foreach (LabelValue lv in _labels)
                SetLabel(root, lv.ElementName, lv.Text);

            foreach (RowGroup group in _rowGroups)
            {
                if (string.IsNullOrEmpty(group.ContainerName)) continue;

                VisualElement container = root.Q(group.ContainerName);
                if (container == null)
                {
                    Debug.LogWarning($"[MockData] no container '{group.ContainerName}' in this panel.");
                    continue;
                }

                if (group.ClearFirst) container.Clear();

                foreach (MockRow row in group.Rows)
                {
                    VisualElement instance;
                    if (group.RowTemplate != null)
                    {
                        instance = group.RowTemplate.Instantiate();
                        // Instantiate() wraps the tree in a TemplateContainer that does not stretch;
                        // without this every cloned row collapses to zero height.
                        instance.style.flexGrow = 0f;
                        instance.style.flexShrink = 0f;
                    }
                    else
                    {
                        // No template: a plain label row, enough for a list that has no row asset.
                        instance = new VisualElement();
                    }

                    container.Add(instance);

                    foreach (LabelValue f in row.Fields)
                        SetLabel(instance, f.ElementName, f.Text);
                }
            }

            foreach (string name in _hideElements)
            {
                if (string.IsNullOrEmpty(name)) continue;
                VisualElement el = root.Q(name);
                if (el != null) el.style.display = DisplayStyle.None;
            }

            foreach (string name in _showElements)
            {
                if (string.IsNullOrEmpty(name)) continue;
                VisualElement el = root.Q(name);
                if (el == null)
                {
                    Debug.LogWarning($"[MockData] no element '{name}' to show.");
                    continue;
                }
                el.style.display = DisplayStyle.Flex;
            }

            // Applied last, so a class can override anything set above.
            foreach (ClassChange c in _classChanges)
            {
                if (string.IsNullOrEmpty(c.ElementName)) continue;
                VisualElement el = root.Q(c.ElementName);
                if (el == null)
                {
                    Debug.LogWarning($"[MockData] no element '{c.ElementName}' to change classes on.");
                    continue;
                }
                if (!string.IsNullOrEmpty(c.RemoveClass)) el.RemoveFromClassList(c.RemoveClass);
                if (!string.IsNullOrEmpty(c.AddClass)) el.AddToClassList(c.AddClass);
            }
        }
    }
}
