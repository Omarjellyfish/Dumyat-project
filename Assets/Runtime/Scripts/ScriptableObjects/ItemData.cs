using UnityEngine;

namespace DumyatRuntime
{
    [CreateAssetMenu(fileName = "New Item", menuName = "Dumyat/Inventory/Item Data")]
    public class ItemData : ScriptableObject
    {
        [Header("Core Info")]
        public string itemID; 
        public string displayName;
        [TextArea] public string description;

        [Header("Graphics")]
        public Sprite icon;
        public GameObject dropPrefab; 

    }
}