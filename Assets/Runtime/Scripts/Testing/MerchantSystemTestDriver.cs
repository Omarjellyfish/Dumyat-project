using System.Collections;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// TEST-ONLY helper for the "AI Test Scene". Grants AI consent and kicks off a merchant
    /// negotiation so the consent → pricing → LLM → TTS/STT pipeline can be exercised in the
    /// Editor without VR locomotion or a wired inventory. Not used by the shipping game.
    ///
    /// In Play mode you can also re-run any step from the component's context menu
    /// (right-click the component header): Grant Consent, Revoke Consent, Start Negotiation.
    /// </summary>
    public class MerchantSystemTestDriver : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Spawner that owns the one-at-a-time lock; the negotiation is routed through it.")]
        [SerializeField] private MerchantSpawner _spawner;

        [Tooltip("Merchant the test negotiation is started with.")]
        [SerializeField] private MerchantNPC _merchant;

        [Header("On Play")]
        [Tooltip("Auto-grant mic + internet consent on Play so the AI path is reachable.")]
        [SerializeField] private bool _grantConsentOnPlay = true;

        [Tooltip("Automatically start a negotiation shortly after entering Play mode.")]
        [SerializeField] private bool _startNegotiationOnPlay = true;

        [Tooltip("Delay before the auto-start, to let the manager singletons finish model discovery.")]
        [SerializeField, Min(0f)] private float _startDelaySeconds = 0.75f;

        private void Start()
        {
            if (_grantConsentOnPlay) GrantConsent();
            if (_startNegotiationOnPlay) StartCoroutine(StartAfterDelay());
        }

        private IEnumerator StartAfterDelay()
        {
            yield return new WaitForSeconds(_startDelaySeconds);
            StartNegotiation();
        }

        [ContextMenu("Grant Consent")]
        public void GrantConsent()
        {
            if (ConsentManager.Singleton == null)
            {
                Debug.LogWarning("[TestDriver] No ConsentManager in scene.");
                return;
            }
            ConsentManager.Singleton.SubmitConsentResult(ConsentStatus.Granted, ConsentStatus.Granted);
            Debug.Log("[TestDriver] Consent granted (mic + internet).");
        }

        [ContextMenu("Revoke Consent")]
        public void RevokeConsent()
        {
            if (ConsentManager.Singleton == null) return;
            ConsentManager.Singleton.SetConsent(ConsentStatus.Declined, ConsentStatus.Declined);
            Debug.Log("[TestDriver] Consent revoked — negotiations will use the scripted fallback.");
        }

        [ContextMenu("Start Negotiation")]
        public void StartNegotiation()
        {
            if (_merchant == null)
            {
                Debug.LogWarning("[TestDriver] No merchant assigned to negotiate with.");
                return;
            }

            if (_spawner != null)
            {
                Debug.Log($"[TestDriver] Starting negotiation with '{_merchant.name}' via MerchantSpawner.");
                _spawner.PlayerInitiatesWithMerchant(_merchant);
            }
            else if (NegotiationFlowController.Singleton != null)
            {
                Debug.Log($"[TestDriver] No spawner set; starting directly with '{_merchant.name}'.");
                NegotiationFlowController.Singleton.BeginNegotiation(_merchant);
            }
            else
            {
                Debug.LogWarning("[TestDriver] No MerchantSpawner or NegotiationFlowController available.");
            }
        }
    }
}
