using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PresentationManager))]
public class PresentationManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        PresentationManager manager = (PresentationManager)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Debug Controls", EditorStyles.boldLabel);
        
        EditorGUILayout.BeginHorizontal();
        
        if (GUILayout.Button("Previous Location", GUILayout.Height(30)))
        {
            if (Application.isPlaying)
            {
                manager.PreviousLocation();
            }
            else
            {
                Debug.LogWarning("Debug buttons only work in Play Mode.");
            }
        }

        if (GUILayout.Button("Next Location", GUILayout.Height(30)))
        {
            if (Application.isPlaying)
            {
                manager.NextLocation();
            }
            else
            {
                Debug.LogWarning("Debug buttons only work in Play Mode.");
            }
        }
        
        EditorGUILayout.EndHorizontal();
    }
}
