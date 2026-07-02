using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace DumyatRuntime
{
    public class PauseManager : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private InputActionReference _pauseAction;

        [Header("References")]
        [SerializeField] private GameObject _pauseMenuPanel;

        private bool _isPaused = false;

        private void OnEnable()
        {
            _pauseAction.action.performed += HandlePausePressed;
            _pauseAction.action.Enable();
        }

        private void OnDisable()
        {
            _pauseAction.action.performed -= HandlePausePressed;
        }

        private void HandlePausePressed(InputAction.CallbackContext _ctx)
        {
            if (_isPaused) Resume();
            else Pause();
        }

        public void Pause()
        {
            _isPaused = true;
            _pauseMenuPanel.SetActive(true);
            Time.timeScale = 0f;
            EventBus.Publish(new GamePauseToggledEvent(true));
        }

        public void Resume()
        {
            _isPaused = false;
            _pauseMenuPanel.SetActive(false);
            Time.timeScale = 1f;
            EventBus.Publish(new GamePauseToggledEvent(false));
        }

        public void Restart()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        public void Quit()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene("Scene_MainMenu");
        }
    }
}