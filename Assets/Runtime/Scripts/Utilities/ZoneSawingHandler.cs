using DG.Tweening;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DumyatRuntime
{
    [ExecuteAlways]
    public class ZoneSawingHandler : MonoBehaviour
    {
        [Header("Target Object")]
        [Tooltip("Leave empty if this script is attached directly to the object you want to move")]
        [SerializeField] private Transform objectToMove;

        [Header("Movement Settings")]
        [Tooltip("The local direction to push (e.g., 0,0,1 is forward)")]
        [SerializeField] private Vector3 pushAxis = Vector3.forward;

        [Tooltip("How far forward the object travels before pulling back (in meters)")]
        [SerializeField] private float pushDistance = 0.5f;

        [Tooltip("Time in seconds to complete ONE full cycle (forward AND back)")]
        [SerializeField] private float cycleDuration = 1.0f;

        private Tween sawTween;
        private Vector3 lastEditorPosition;

        private void Start()
        {
            if (objectToMove == null) objectToMove = this.transform;
            StartContinuousMovement();
        }

        private void OnEnable()
        {
            if (objectToMove == null) objectToMove = this.transform;
            StartContinuousMovement();

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                EditorApplication.update += EditorUpdate;
                if (objectToMove != null) lastEditorPosition = objectToMove.localPosition;
            }
#endif
        }

        private void StartContinuousMovement()
        {
            KillTween();
            if (objectToMove == null) return;

            // Calculate the delta vector based on the object's current orientation
            Vector3 localDelta = objectToMove.localRotation * pushAxis.normalized * pushDistance;
            float halfDuration = cycleDuration / 2f;

            // .SetRelative(true) tells DOTween to treat localDelta as a relative offset (+ / -)
            // instead of an absolute coordinate in space.
            sawTween = objectToMove.DOLocalMove(localDelta, halfDuration)
                .SetEase(Ease.InOutQuad)
                .SetLoops(-1, LoopType.Yoyo)
                .SetRelative(true); 

#if UNITY_EDITOR
            if (!Application.isPlaying && sawTween != null)
            {
                sawTween.SetUpdate(UpdateType.Manual);
            }
#endif
        }

#if UNITY_EDITOR
        private void EditorUpdate()
        {
            if (Application.isPlaying || objectToMove == null) return;

            // If you drag the object in the editor, its tweened position will fight your mouse.
            // We check if the base position changed dramatically (meaning you are moving it).
            // If so, we restart the movement loop at the new location.
            if (Selection.activeTransform == objectToMove || Selection.activeTransform == this.transform)
            {
                // Simple check to see if the user is dragging the object manually
                if (sawTween != null && !sawTween.IsPlaying())
                {
                    // Allow manual layout adjustments smoothly
                    lastEditorPosition = objectToMove.localPosition;
                    return;
                }
            }

            if (sawTween != null && sawTween.IsActive())
            {
                sawTween.ManualUpdate(Time.deltaTime, Time.unscaledDeltaTime);
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
            }
        }

        // Whenever you tweak values in the Inspector (like pushDistance), update instantly
        private void OnValidate()
        {
            if (gameObject.activeInHierarchy)
            {
                // Delay call ensures Unity has finished processing inspector changes
                EditorApplication.delayCall += () => {
                    if (this != null) StartContinuousMovement();
                };
            }
        }
#endif

        private void OnDisable()
        {
            KillTween();
#if UNITY_EDITOR
            EditorApplication.update -= EditorUpdate;
#endif
        }

        private void OnDestroy()
        {
            KillTween();
        }

        private void KillTween()
        {
            if (sawTween != null && sawTween.IsActive())
            {
                sawTween.Kill();
            }
            sawTween = null;
        }
    }
}