using UnityEngine;
using UnityEngine.Events;

public class PresentationLocation : MonoBehaviour
{
    [Header("Events")]
    public UnityEvent OnLocationReset;

    public void Activate()
    {
        gameObject.SetActive(true);
    }

    public void Deactivate()
    {
        gameObject.SetActive(false);
    }

    public void ResetState()
    {
        // Re-trigger animations, reset physics, or invoke custom logic when you arrive
        OnLocationReset?.Invoke();
    }
}
