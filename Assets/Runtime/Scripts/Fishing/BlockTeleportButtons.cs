using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DumyatRuntime
{
    public class BlockTeleportButtons : MonoBehaviour
    {
        [SerializeField] private Button[] _buttons;
        void Start()
        {
            BlockButtons();
        }

        public void BlockButtons()
        {
            foreach (var button in _buttons)
            {
                button.interactable = false;
            }
        }

        public void UnblockButtons()
        {
            foreach (var button in _buttons)
            {
                button.interactable = true;
            }
        }

        // Update is called once per frame
        void Update()
        {

        }
    }
}
