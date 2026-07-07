#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using DumyatRuntime;
using UnityEditor;
using UnityEngine;

namespace DumyatEditor
{
    /// <summary>
    /// Custom inspector for <see cref="VoiceCaptureController"/> that renders the
    /// <c>_microphoneDevice</c> field as a live dropdown of the microphones currently connected to
    /// the system. The list refreshes in real time (<see cref="RequiresConstantRepaint"/>) so
    /// connecting or disconnecting a microphone updates the choices immediately. Empty selection
    /// ("Default") means the controller uses the first connected device at capture time.
    /// </summary>
    [CustomEditor(typeof(VoiceCaptureController))]
    public class VoiceCaptureControllerEditor : Editor
    {
        private const string DeviceFieldName = "_microphoneDevice";
        private const string DefaultLabel = "Default (first connected device)";

        // Repaint continuously so the device list reflects hot-plug changes without needing focus.
        public override bool RequiresConstantRepaint() => true;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (iterator.propertyPath == "m_Script")
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.PropertyField(iterator, true);
                }
                else if (iterator.name == DeviceFieldName)
                {
                    DrawMicrophoneDevicePopup(iterator);
                }
                else
                {
                    EditorGUILayout.PropertyField(iterator, true);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawMicrophoneDevicePopup(SerializedProperty prop)
        {
            string[] devices = Microphone.devices;

            // Index 0 is always "Default" (maps to empty string); connected devices follow.
            var labels = new List<string>(devices.Length + 2) { DefaultLabel };
            labels.AddRange(devices);

            string current = prop.stringValue;
            int selected = 0;
            if (!string.IsNullOrEmpty(current))
            {
                int idx = Array.IndexOf(devices, current);
                if (idx >= 0)
                {
                    selected = idx + 1;
                }
                else
                {
                    // Configured device isn't connected right now — keep it visible (flagged) so the
                    // saved choice isn't silently lost when the device is unplugged.
                    labels.Add($"{current}  (disconnected)");
                    selected = labels.Count - 1;
                }
            }

            var content = new GUIContent(prop.displayName, prop.tooltip);
            int chosen = EditorGUILayout.Popup(content, selected, labels.ToArray());
            if (chosen == selected) return;

            if (chosen == 0)
                prop.stringValue = string.Empty;                 // Default
            else if (chosen <= devices.Length)
                prop.stringValue = devices[chosen - 1];          // a connected device
            // else: the trailing "(disconnected)" entry — leave the value unchanged
        }
    }
}
#endif
