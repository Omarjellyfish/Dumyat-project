using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatEditor
{
    [CustomEditor(typeof(PlayerAttackStats))]
    public class PlayerAttackStatsEditor: Editor
    {
        #region Variables
        [SerializeField] private VisualTreeAsset _visualTreeAsset;

        private PlayerAttackStats _playerAttackStats;
        private Button _assignRandomSpeedButton;
        private Button _setToDefaultValuesButton;

        private PropertyField _toggleProperty;
        private SerializedProperty _togglePropertySerialized;
        private VisualElement _allElementsToHide;


        #endregion

        #region Unity Methods

        void OnEnable()
        {
            _playerAttackStats = (PlayerAttackStats)target;
            _togglePropertySerialized = serializedObject.FindProperty("_showValues");
        }

        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            _visualTreeAsset.CloneTree(root);

            _assignRandomSpeedButton = root.Q<Button>("RandomizeMoveSpeed");
            _assignRandomSpeedButton.RegisterCallback<ClickEvent>(OnAssignRandomSpeedButtonClicked);

            _setToDefaultValuesButton = root.Q<Button>("DefaultValues");
            _setToDefaultValuesButton.RegisterCallback<ClickEvent>(OnSetToDefaultValuesButtonClicked);

            _toggleProperty = root.Q<PropertyField>("ShowValues");
            _toggleProperty.RegisterCallback<ChangeEvent<bool>>(OnToggleElementsChanged);

            _allElementsToHide = root.Q<VisualElement>("AllElementsToHide");
            Debug.Log($"hide element: {_allElementsToHide}");

            CheckForDisplayType();

            return root;
        }

        #endregion

        #region Events
        private void OnAssignRandomSpeedButtonClicked(ClickEvent evt)
        {
            _playerAttackStats.AssignRandomSpeed();
        }

        private void OnSetToDefaultValuesButtonClicked(ClickEvent evt)
        {
            _playerAttackStats.SetToDefaultValues();
        }

        private void OnToggleElementsChanged(ChangeEvent<bool> evt)
        {
            CheckForDisplayType();
        }

        #endregion

        #region Private Methods
        private void CheckForDisplayType()
        {
            serializedObject.Update();
            if (_togglePropertySerialized.boolValue)
            {
                _allElementsToHide.style.display = DisplayStyle.Flex;
            }
            else
            {
                _allElementsToHide.style.display = DisplayStyle.None;
            }
        }

        #endregion

    }
}
