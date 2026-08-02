using UnityEngine;
using UnityEngine.SceneManagement;

namespace DumyatRuntime
{
    public class MenuManager : MonoBehaviour
    {
[Header("Settings")]
    [SerializeField] private GameObject _settingsPanel;

    public void LoadLocation(string _sceneName)
    {
        SceneManager.LoadScene(_sceneName);
    }

    public void ShowLockedFeedback(MenuLocation _location)
    {
        // we will add shake + sound here later
        Debug.Log($"{_location.name} is locked");
    }

    public void ToggleSettings()
    {
        _settingsPanel.SetActive(!_settingsPanel.activeSelf);
    }
    }
}
