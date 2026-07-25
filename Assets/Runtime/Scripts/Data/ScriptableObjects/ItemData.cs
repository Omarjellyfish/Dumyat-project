using UnityEngine;
using UnityEngine.Serialization;

namespace DumyatRuntime
{
    [CreateAssetMenu(fileName = "New Item", menuName = "Dumyat/Inventory/Item Data")]
    public class ItemData : ScriptableObject, IQuestTarget
    {
        [Header("Core Info")]
        [FormerlySerializedAs("itemID")]
        [SerializeField] private string _itemID; 

        [FormerlySerializedAs("displayName")]
        [SerializeField] private string _displayName;

        [FormerlySerializedAs("description")]
        [TextArea] [SerializeField] private string _description;

        [Header("Graphics")]
        [FormerlySerializedAs("icon")]
        [SerializeField] private Sprite _icon;

        [FormerlySerializedAs("dropPrefab")]
        [SerializeField] private GameObject _dropPrefab; 

        public string ItemID => _itemID;
        public string DisplayName => _displayName;
        public string Description => _description;
        public Sprite Icon => _icon;
        public GameObject DropPrefab => _dropPrefab;

        /// <inheritdoc />
        public string TargetID => _itemID;
    }
}