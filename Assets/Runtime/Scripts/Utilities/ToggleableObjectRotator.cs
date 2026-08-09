using UnityEngine;

public class ToggleableObjectRotator : MonoBehaviour
{
    [Tooltip("The speed and axis of rotation in degrees per second.")]
    [SerializeField] private Vector3 _rotationSpeed = new Vector3(0f, 90f, 0f);

    [Tooltip("Whether to rotate around the local axes or world axes.")]
    [SerializeField] private Space _rotationSpace = Space.Self;

    [Tooltip("Is the rotation currently active?")]
    [SerializeField] private bool _isRotating = false;

    private void Update()
    {
        if (!_isRotating) return;

        // Multiply by Time.deltaTime to ensure frame-rate independent rotation
        transform.Rotate(_rotationSpeed * Time.deltaTime, _rotationSpace);
    }

    /// <summary>
    /// Toggles the rotation state on or off. Perfect for linking to a UI Button's OnClick event.
    /// </summary>
    public void ToggleRotation()
    {
        _isRotating = !_isRotating;
    }

    /// <summary>
    /// Explicitly turns the rotation on.
    /// </summary>
    public void StartRotation()
    {
        _isRotating = true;
    }

    /// <summary>
    /// Explicitly turns the rotation off.
    /// </summary>
    public void StopRotation()
    {
        _isRotating = false;
    }

    /// <summary>
    /// Sets the rotation state directly. Good for linking to a UI Toggle's OnValueChanged event.
    /// </summary>
    public void SetRotationEnabled(bool state)
    {
        _isRotating = state;
    }
}
