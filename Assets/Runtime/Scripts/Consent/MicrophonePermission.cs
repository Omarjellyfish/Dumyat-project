using System;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace DumyatRuntime
{
    /// <summary>
    /// Thin, platform-safe wrapper over the Android <c>RECORD_AUDIO</c> runtime permission that Unity's
    /// <see cref="UnityEngine.Microphone"/> API requires. This is the OS-level grant — deliberately
    /// SEPARATE from the in-app <see cref="ConsentManager"/> consent flags: an in-app mic consent with
    /// no OS grant still leaves <c>Microphone.devices</c> empty, so voice capture fails silently.
    ///
    /// On every non-Android platform (Editor, Standalone) the OS grants microphone access without a
    /// runtime prompt, so <see cref="IsGranted"/> reports <c>true</c> and <see cref="Request"/> completes
    /// immediately — callers need no platform <c>#if</c> of their own.
    /// </summary>
    public static class MicrophonePermission
    {
        /// <summary>True when the app currently holds the OS microphone permission (always true off Android).</summary>
        public static bool IsGranted
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            get => Permission.HasUserAuthorizedPermission(Permission.Microphone);
#else
            get => true;
#endif
        }

        /// <summary>
        /// Ensures the runtime microphone permission is held, showing the OS prompt if it is not.
        /// Invokes <paramref name="onResult"/> with the final grant state on the main thread — synchronously
        /// when already granted (or off Android), otherwise when the player answers the prompt.
        /// </summary>
        public static void Request(Action<bool> onResult)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                onResult?.Invoke(true);
                return;
            }

            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => onResult?.Invoke(true);
            // A plain deny and a "deny & don't ask again" both mean "not granted" to us. In current Unity
            // the don't-ask-again case routes through PermissionDenied when no dedicated callback is set,
            // so subscribing to PermissionDenied alone covers every rejection.
            callbacks.PermissionDenied += _ => onResult?.Invoke(false);
            Permission.RequestUserPermission(Permission.Microphone, callbacks);
#else
            onResult?.Invoke(true);
#endif
        }
    }
}
