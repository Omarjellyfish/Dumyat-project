using System.Collections;
using System.Collections.Generic;
using UnityEngine;



public class PresentationManager : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float _transitionDelay = 2.0f;
    [SerializeField] private List<PresentationLocation> _locations;

    private int _currentIndex = 0;
    private Coroutine _transitionCoroutine;

    private void Start()
    {
        if (_locations == null || _locations.Count == 0)
        {
            Debug.LogWarning("PresentationManager: No locations assigned!");
            return;
        }

        // Initialize: Deactivate all locations except the first one
        for (int i = 0; i < _locations.Count; i++)
        {
            if (i == _currentIndex)
            {
                _locations[i].Activate();
                _locations[i].ResetState();
            }
            else
            {
                _locations[i].Deactivate();
            }
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.A) || Input.GetMouseButtonDown(1))
        {
            PreviousLocation();
        }
        else if (Input.GetKeyDown(KeyCode.D) || Input.GetMouseButtonDown(0))
        {
            NextLocation();
        }
        else if (Input.GetKeyDown(KeyCode.Escape))
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    [ContextMenu("Next Location")]
    public void NextLocation()
    {
        if (_locations.Count == 0) return;

        int previousIndex = _currentIndex;
        _currentIndex = (_currentIndex + 1) % _locations.Count;

        if (_transitionCoroutine != null)
        {
            StopCoroutine(_transitionCoroutine);
            // Clean up any other locations that might be stuck active due to interruption
            for (int i = 0; i < _locations.Count; i++)
            {
                if (i != previousIndex && i != _currentIndex)
                {
                    _locations[i].Deactivate();
                }
            }
        }

        _transitionCoroutine = StartCoroutine(TransitionRoutine(previousIndex, _currentIndex));
    }

    [ContextMenu("Previous Location")]
    public void PreviousLocation()
    {
        if (_locations.Count == 0) return;

        int previousIndex = _currentIndex;
        _currentIndex = _currentIndex - 1;
        if (_currentIndex < 0)
        {
            _currentIndex = _locations.Count - 1;
        }

        if (_transitionCoroutine != null)
        {
            StopCoroutine(_transitionCoroutine);
            // Clean up any other locations that might be stuck active due to interruption
            for (int i = 0; i < _locations.Count; i++)
            {
                if (i != previousIndex && i != _currentIndex)
                {
                    _locations[i].Deactivate();
                }
            }
        }

        _transitionCoroutine = StartCoroutine(TransitionRoutine(previousIndex, _currentIndex));
    }

    public void TriggerCustomAction(string actionId)
    {
        if (_locations.Count == 0) return;

        PresentationLocation currentLoc = _locations[_currentIndex];
        IPresentationCustomAction[] customActions = currentLoc.GetComponents<IPresentationCustomAction>();

        foreach (var action in customActions)
        {
            action.TriggerAction(actionId);
        }
    }

    private IEnumerator TransitionRoutine(int previousIndex, int newIndex)
    {
        PresentationLocation newLocation = _locations[newIndex];
        PresentationLocation oldLocation = _locations[previousIndex];

        // 1. Activate new location (so Cinemachine can start blending to it immediately)
        newLocation.Activate();

        // 2. Reset the state of the new location
        newLocation.ResetState();

        // 3. Wait for the transition delay (gives time for Cinemachine to blend)
        yield return new WaitForSeconds(_transitionDelay);

        // 4. Deactivate old location
        oldLocation.Deactivate();

        _transitionCoroutine = null;
    }
}
