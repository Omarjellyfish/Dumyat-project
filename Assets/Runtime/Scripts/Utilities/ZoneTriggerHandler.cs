using System.Collections.Generic;
using UnityEngine;

public class ZoneTriggerHandler : MonoBehaviour
{
    [Header("Target Objects")]
    [SerializeField] private List<GameObject> objectsToToggle = new List<GameObject>();

    [Header("Collision Settings")]
    [Tooltip("Matches the tag on your XR Origin's Main Camera")]
    [SerializeField] private string targetTag = "MainCamera";

    void Start()
    {
        SetObjectsState(false); // Ensure objects are disabled when the script is enabled
    }
    private void OnTriggerEnter(Collider other)
    {
        // 1. This tells us if ANY physics collision is happening
        Debug.Log("Something entered the trigger zone: " + other.name); 
        
        if (other.CompareTag(targetTag))
        {
            // 2. This tells us if the tag check actually passed
            Debug.Log("Success! MainCamera detected. Toggling objects ON."); 
            SetObjectsState(true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(targetTag))
        {
            Debug.Log("MainCamera left. Toggling objects OFF.");
            SetObjectsState(false);
        }
    }   

    private void OnTriggerStay(Collider other)
    {
        if (other.CompareTag(targetTag))
        {
            Debug.Log("MainCamera is still in the trigger zone.");
        }
    }

    [ContextMenu("Enable Objects")]
    private void enableObjects()
    {
        SetObjectsState(true);
    }

    [ContextMenu("Disable Objects")]
    private void disableObjects()
    {
        SetObjectsState(false);
    }

    private void SetObjectsState(bool state)
    {
        foreach (GameObject obj in objectsToToggle)
        {
            if (obj != null)
            {
                obj.SetActive(state);
            }
        }
    }
}
