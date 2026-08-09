using UnityEngine;

public class ObjectRotator : MonoBehaviour
{
    [Tooltip("The speed and axis of rotation in degrees per second.")]
    [SerializeField] private Vector3 _rotationSpeed = new Vector3(0f, 90f, 0f);
    
    [Tooltip("Whether to rotate around the local axes or world axes.")]
    [SerializeField] private Space _rotationSpace = Space.Self;

    private void Update()
    {
        // Multiply by Time.deltaTime to ensure frame-rate independent rotation
        transform.Rotate(_rotationSpeed * Time.deltaTime, _rotationSpace);
    }
}
