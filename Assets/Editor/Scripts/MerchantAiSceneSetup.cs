#if UNITY_EDITOR
using DumyatRuntime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace DumyatEditor
{
    /// <summary>
    /// One-shot, idempotent scene-wiring tool for the AI Test Scene:
    /// turns the AiNPC character into the merchant NPC (config, spatial audio, proximity
    /// trigger, greeting animation, lip sync), moves it away from the player, retargets the
    /// MerchantApproachCoordinator roster at it (disabling the placeholder booth merchants and the
    /// market-wide auto-approach trigger), stops the test driver from auto-granting consent /
    /// auto-starting a negotiation, and adds the UI Toolkit consent prompt.
    ///
    /// Exists as a menu item (instead of hand-wiring in the Inspector) so the setup is
    /// deterministic, reviewable and re-runnable after scene changes.
    /// </summary>
    public static class MerchantAiSceneSetup
    {
        private const string MerchantConfigPath = "Assets/Runtime/Data Layer/Merchants/Merchant_Warm_AmSayed.asset";
        private const string ConsentUxmlPath = "Assets/Runtime/UI Toolkit/ConsentPrompt.uxml";
        private const string ThemePath = "Assets/Runtime/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss";
        private const string ConsentPanelSettingsPath = "Assets/Runtime/UI Toolkit/Consent Panel Settings.asset";
        private const string NpcName = "AiNPC";
        private const string ConsentUiName = "Consent UI";

        // Far from the player stand-in at (0, 1, -8) so the negotiation only starts on approach.
        private static readonly Vector3 NpcPosition = new Vector3(8f, 0f, 5f);

        // DETECTION range: how far away the merchant notices the player and starts walking over
        // (the walk stops at MerchantApproachMover's conversation distance).
        private const float DetectionRadius = 6f;

        // Consent is deliberately persisted across sessions (asked once per install), so testers
        // need an explicit reset to see the prompt again.
        [MenuItem("Dumyat/Setup/Reset Consent (Show Prompt Again)")]
        public static void ResetConsent()
        {
            ConsentManager.ClearPersistedConsent();
            Debug.Log("[SceneSetup] Consent reset to NotAsked — the consent prompt will show on next Play.");
        }

        [MenuItem("Dumyat/Setup/Wire AI Test Scene (Consent + AiNPC Merchant)")]
        public static void Wire()
        {
            GameObject npc = GameObject.Find(NpcName);
            if (npc == null)
            {
                Debug.LogError($"[SceneSetup] No '{NpcName}' object in the open scene — aborting.");
                return;
            }

            MerchantNPC merchant = WireNpc(npc);
            WireCoordinator(merchant);
            DisablePlaceholderBooths(merchant);
            WireTestDriver(merchant);
            WireConsentUi();
            WireTestPlayerMovement();

            EditorSceneManager.MarkSceneDirty(npc.scene);
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("[SceneSetup] AI Test Scene wired: AiNPC is the merchant (approach to start), " +
                      "consent UI added, auto-start/auto-consent disabled. Scene saved.");
        }

        // --- AiNPC: merchant identity + interaction + cosmetics ---------------------

        private static MerchantNPC WireNpc(GameObject npc)
        {
            npc.transform.position = NpcPosition;
            FaceTowardPlayer(npc);

            MerchantNPC merchant = EnsureComponent<MerchantNPC>(npc);
            var config = AssetDatabase.LoadAssetAtPath<MerchantConfig>(MerchantConfigPath);
            if (config == null)
            {
                Debug.LogWarning($"[SceneSetup] Merchant config not found at '{MerchantConfigPath}'.");
            }
            else
            {
                var so = new SerializedObject(merchant);
                so.FindProperty("_config").objectReferenceValue = config;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // Spatial voice: the flow controller prefers the merchant's own AudioSource.
            AudioSource audio = EnsureComponent<AudioSource>(npc);
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.minDistance = 1.5f;
            audio.maxDistance = 20f;

            // Proximity approach: kinematic body + trigger sphere sized to conversation range.
            Rigidbody body = EnsureComponent<Rigidbody>(npc);
            body.isKinematic = true;
            body.useGravity = false;

            SphereCollider trigger = EnsureComponent<SphereCollider>(npc);
            trigger.isTrigger = true;
            trigger.radius = DetectionRadius;
            trigger.center = new Vector3(0f, 1f, 0f);

            EnsureComponent<MerchantProximityTrigger>(npc);
            EnsureComponent<MerchantApproachMover>(npc);   // merchant walks to the player and back
            EnsureComponent<MerchantAnimationDriver>(npc);
            EnsureComponent<SimpleLipSync>(npc);

            EditorUtility.SetDirty(npc);
            return merchant;
        }

        private static void FaceTowardPlayer(GameObject npc)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player == null) return;

            Vector3 toPlayer = player.transform.position - npc.transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.001f)
                npc.transform.rotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
        }

        // --- Coordinator: roster = AiNPC only; no market-volume auto-approach -----------

        private static void WireCoordinator(MerchantNPC merchant)
        {
            MerchantApproachCoordinator coordinator = Object.FindFirstObjectByType<MerchantApproachCoordinator>();
            if (coordinator == null)
            {
                Debug.LogWarning("[SceneSetup] No MerchantApproachCoordinator in scene; skipped roster wiring.");
                return;
            }

            // The market-wide trigger box auto-started approaches on entry; negotiations must now
            // start from the NPC's own proximity trigger instead.
            var marketTrigger = coordinator.GetComponent<BoxCollider>();
            if (marketTrigger != null) marketTrigger.enabled = false;

            var so = new SerializedObject(coordinator);
            SerializedProperty roster = so.FindProperty("_merchants");
            roster.arraySize = 1;
            roster.GetArrayElementAtIndex(0).objectReferenceValue = merchant;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(coordinator);
        }

        private static void DisablePlaceholderBooths(MerchantNPC keep)
        {
            foreach (MerchantNPC other in Object.FindObjectsByType<MerchantNPC>(FindObjectsSortMode.None))
            {
                if (other == keep) continue;
                other.gameObject.SetActive(false);
                Debug.Log($"[SceneSetup] Disabled placeholder merchant '{other.name}'.");
            }
        }

        // --- Test driver: consent comes from the UI, negotiation from proximity -----

        private static void WireTestDriver(MerchantNPC merchant)
        {
            MerchantSystemTestDriver driver = Object.FindFirstObjectByType<MerchantSystemTestDriver>();
            if (driver == null) return;

            var so = new SerializedObject(driver);
            so.FindProperty("_grantConsentOnPlay").boolValue = false;
            so.FindProperty("_startNegotiationOnPlay").boolValue = false;
            so.FindProperty("_merchant").objectReferenceValue = merchant; // for manual context-menu runs
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(driver);
        }

        // The stand-in capsule has no locomotion of its own; give it the WASD test mover so
        // the proximity approach can actually be walked in the Editor.
        private static void WireTestPlayerMovement()
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player == null)
            {
                Debug.LogWarning("[SceneSetup] No object tagged 'Player'; skipped test mover.");
                return;
            }
            EnsureComponent<TestPlayerMover>(player);

            // The listener must ride the PLAYER (as the VR head will), not the far spectator
            // camera — otherwise spatial merchant audio is nearly inaudible at the camera's
            // distance and anything sampling audio output reads near-zero.
            AudioListener playerListener = EnsureComponent<AudioListener>(player);
            playerListener.enabled = true;
            foreach (AudioListener listener in Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            {
                if (listener != playerListener && listener.enabled)
                {
                    listener.enabled = false;
                    Debug.Log($"[SceneSetup] Disabled extra AudioListener on '{listener.gameObject.name}'.");
                }
            }

            EditorUtility.SetDirty(player);
        }

        // --- Consent UI --------------------------------------------------------------

        private static void WireConsentUi()
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ConsentUxmlPath);
            if (uxml == null)
            {
                Debug.LogWarning($"[SceneSetup] Consent UXML not found at '{ConsentUxmlPath}'.");
                return;
            }

            PanelSettings panelSettings = EnsureConsentPanelSettings();

            GameObject go = GameObject.Find(ConsentUiName);
            if (go == null) go = new GameObject(ConsentUiName);

            UIDocument document = go.GetComponent<UIDocument>();
            if (document == null) document = go.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = uxml;

            if (go.GetComponent<ConsentPromptUI>() == null) go.AddComponent<ConsentPromptUI>();
            EditorUtility.SetDirty(go);
        }

        // Loads (or creates once) a dedicated PanelSettings so the consent overlay sorts above
        // other runtime panels instead of borrowing the inventory's settings.
        private static PanelSettings EnsureConsentPanelSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(ConsentPanelSettingsPath);
            if (settings != null) return settings;

            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "Consent Panel Settings";
            settings.sortingOrder = 100;

            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (theme != null) settings.themeStyleSheet = theme;
            else Debug.LogWarning($"[SceneSetup] Theme not found at '{ThemePath}'; panel created without one.");

            AssetDatabase.CreateAsset(settings, ConsentPanelSettingsPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        private static T EnsureComponent<T>(GameObject go) where T : Component
        {
            T existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
        }
    }
}
#endif
