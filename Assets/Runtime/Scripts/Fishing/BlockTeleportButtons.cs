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

public void UnblockButtons()
{
    foreach (var button in _buttons)
    {
        if (button == null) continue;
        button.interactable = true;
    }
}

public void BlockButtons()
{
    foreach (var button in _buttons)
    {
        if (button == null) continue;
        button.interactable = false;
    }
}

        // Update is called once per frame
        void Update()
        {

        }
    }
}
