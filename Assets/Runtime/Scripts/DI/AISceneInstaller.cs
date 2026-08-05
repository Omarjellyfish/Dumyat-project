using Reflex.Core;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Scene-container installer for the AI / merchant-negotiation MonoBehaviour services. Drop this
    /// alongside a <c>ContainerScope</c> in a scene that runs the merchant pipeline (e.g. the AI Test
    /// Scene). Each service is taken from its serialized field, or — if left empty — auto-discovered
    /// in the scene, then registered against its capability interface (mirrors
    /// <see cref="CraftingSceneInstaller"/>).
    ///
    /// The provider clients (Gemini/Groq/ElevenLabs models) are NOT registered here: they stay in the
    /// scene and are auto-discovered into their manager's pool via <c>AIModelManager.DiscoverModels</c>.
    /// <c>IEventService</c> is a root binding from <see cref="GameInstaller"/>.
    /// </summary>
    public sealed class AISceneInstaller : GenericSceneInstaller
    {
        [Header("AI Managers (optional — auto-found if left empty)")]
        [SerializeField] private LLMManager _llm;
        [SerializeField] private TTSManager _tts;
        [SerializeField] private STTManager _stt;

        [Header("Merchant / Player Services (optional — auto-found if left empty)")]
        [SerializeField] private ConsentManager _consent;
        [SerializeField] private PlayerStats _playerStats;
        [SerializeField] private MerchantNegotiationLLM _negotiationLLM;
        [SerializeField] private NegotiationFlowController _flow;
        [SerializeField] private ScriptedSaleController _scriptedSale;

        public override void InstallBindings(ContainerBuilder containerBuilder)
        {
            base.InstallBindings(containerBuilder);

            Register<LLMManager, ILLMService>(containerBuilder, _llm);
            Register<TTSManager, ITTSService>(containerBuilder, _tts);
            Register<STTManager, ISTTService>(containerBuilder, _stt);
            Register<ConsentManager, IConsentService>(containerBuilder, _consent);
            Register<PlayerStats, IPlayerStatsService>(containerBuilder, _playerStats);
            Register<MerchantNegotiationLLM, IMerchantNegotiationService>(containerBuilder, _negotiationLLM);
            Register<NegotiationFlowController, INegotiationService>(containerBuilder, _flow);
            Register<ScriptedSaleController, IScriptedSaleService>(containerBuilder, _scriptedSale);

            // INotificationService is deliberately NOT registered here any more. The HUD is now a root
            // binding (see NotificationHudPresenter): quests progress in scenes this installer is not
            // in, so a per-scene HUD meant they could not be announced there. Sale notifications keep
            // working because the scene container falls through to the root one. Re-registering it
            // here would put a second HUD in the same corner of the player's view.
        }
    }
}
