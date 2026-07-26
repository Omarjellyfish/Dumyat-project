using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Reflex.Attributes;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Runs the SCRIPTED (offline) merchant sale — the no-AI, no-network, no-microphone, no-TTS fallback
    /// <see cref="NegotiationFlowController"/> hands off to whenever the voice flow is unavailable. It drives
    /// a button-based loop over the merchant's sell scale:
    ///
    ///   welcome  →  [I'm done]  →  flat quote (+ per-fish breakdown)  →  [Accept] / [Decline]
    ///
    /// The player's intent comes ONLY from explicit button clicks (<see cref="ScriptedChoiceMadeEvent"/>),
    /// never from parsed text — this is the one path where intent is unambiguous, which is exactly why
    /// buttons replace speech (see CLAUDE.md's no-keyword-intent rule). Dialogue is drawn from a
    /// <see cref="ScriptedMerchantLinesSO"/> and shown on the merchant's existing subtitle panel via
    /// <see cref="MerchantSpokeEvent"/>; the price lands on the <see cref="SummaryCard"/> via
    /// <see cref="MerchantOfferedPriceEvent"/>; payout goes through the shared <see cref="BundleSaleSettler"/>.
    /// The quote is a fixed per-merchant rate (see <see cref="ScriptedPriceBook"/>), so re-approaching quotes
    /// the same number and adding/removing fish re-prices symmetrically.
    ///
    /// Drop one instance in a merchant scene (alongside the ContainerScope) and register it as
    /// <see cref="IScriptedSaleService"/>; <see cref="NegotiationFlowController"/> resolves it by scene search.
    /// </summary>
    public class ScriptedSaleController : MonoBehaviour, IScriptedSaleService
    {
        private const string LogPrefix = "[ScriptedSale]";

        [Header("Config")]
        [Tooltip("Global merchant config — supplies the market-wide scripted line bank and feeds the price " +
                 "calculator. Auto-found in the scene if left empty.")]
        [SerializeField] private MerchantSystemConfig _systemConfig;

        [Header("Placement")]
        [Tooltip("Seconds of no activity before the merchant nudges the player to place fish on the scale.")]
        [SerializeField, Min(1f)] private float _placementIdleSeconds = 15f;

        [Tooltip("Give-up guard: after this many unproductive placement nudges (player never loads the " +
                 "scale) the merchant abandons the sale.")]
        [SerializeField, Min(1)] private int _maxPlacementPrompts = 8;

        [Header("Closing")]
        [Tooltip("Seconds to hold the closing/farewell subtitle on screen before the sale tears down, so " +
                 "the player can read it (there is no voice to pace it).")]
        [SerializeField, Min(0.5f)] private float _closingHoldSeconds = 3.5f;

        [Header("Fish Inventory")]
        [Tooltip("Optional component implementing IFishInventory (the sell-side seam). Auto-found in the " +
                 "scene if empty. Used to consume sold fish; a scene without one still sells (no-op consume).")]
        [SerializeField] private MonoBehaviour _fishInventorySource;

        private IEventService _events;
        private IPlayerStatsService _playerStats;

        [Inject]
        private void Inject(IEventService events, IPlayerStatsService playerStats)
        {
            _events = events;
            _playerStats = playerStats;
        }

        // Rate book persists across sales so each merchant's rolled rate is stable for the session.
        private ScriptedPriceBook _priceBook;
        private PriceNegotiationCalculator _calculator;
        private ScriptedMerchantLinesSO _fallbackLines;   // lazily created when no bank is assigned anywhere
        private IFishInventory _fishInventory;

        private enum Stage { Idle, AwaitingPlacement, AwaitingDecision, Closing }
        private Stage _stage = Stage.Idle;

        private MerchantNPC _merchant;
        private ScriptedMerchantLinesSO _lines;
        private Action<NegotiationPhase> _onEnded;
        private readonly List<FishData> _bundle = new List<FishData>();
        private int _placementPrompts;
        private bool _subscribed;

        /// <inheritdoc/>
        public bool IsRunning => _stage != Stage.Idle;

        private void Awake()
        {
            if (_systemConfig == null) _systemConfig = FindFirstObjectByType<MerchantSystemConfig>();
            _calculator = new PriceNegotiationCalculator(_systemConfig);
            _priceBook = new ScriptedPriceBook(_calculator);
        }

        private void OnDestroy() => Unsubscribe();

        // --- Entry point -----------------------------------------------------------

        /// <inheritdoc/>
        public bool BeginScriptedSale(MerchantNPC merchant, Action<NegotiationPhase> onEnded)
        {
            if (merchant == null)
            {
                Debug.LogWarning($"{LogPrefix} BeginScriptedSale called with a null merchant.");
                return false;
            }
            if (IsRunning)
            {
                Debug.LogWarning($"{LogPrefix} Already running (with '{NameOf(_merchant)}'); " +
                                 $"ignoring request for '{NameOf(merchant)}'.");
                return false;
            }
            if (merchant.SellScale == null)
            {
                // The scripted flow is scale-based (the catch comes from the physical scale). A merchant
                // with no scale can't run it — the caller falls back to ending cleanly.
                Debug.LogWarning($"{LogPrefix} '{NameOf(merchant)}' has no sell scale — cannot run the " +
                                 "scripted sale.");
                return false;
            }

            _merchant = merchant;
            _onEnded = onEnded;
            _lines = ResolveLines(merchant);
            _bundle.Clear();
            _placementPrompts = 0;

            Subscribe();
            _stage = Stage.AwaitingPlacement;

            Debug.Log($"{LogPrefix} Begin scripted sale with '{NameOf(merchant)}'.");
            ScriptedMerchantLinesSO.Line welcome = _lines.RandomWelcome();
            Speak(welcome);
            RequestChoices(ScriptedChoice.ConfirmPlacement);
            ArmPlacementIdle();
            return true;
        }

        /// <inheritdoc/>
        public void Cancel(MerchantNPC merchant)
        {
            if (merchant == null || merchant != _merchant || !IsRunning) return;
            Debug.Log($"{LogPrefix} Scripted sale with '{NameOf(merchant)}' cancelled (player left).");
            Finish(NegotiationPhase.WalkedAway);
        }

        // --- Player choices --------------------------------------------------------

        private void HandleChoiceMade(ScriptedChoiceMadeEvent evt)
        {
            if (evt.Merchant != _merchant || !IsRunning) return;

            switch (_stage)
            {
                case Stage.AwaitingPlacement:
                    if (evt.Choice == ScriptedChoice.ConfirmPlacement) HandleDonePlacing();
                    break;
                case Stage.AwaitingDecision:
                    if (evt.Choice == ScriptedChoice.Accept) HandleAccept();
                    else if (evt.Choice == ScriptedChoice.Decline) HandleDecline();
                    break;
            }
        }

        // Player pressed "I'm done" during placement.
        private void HandleDonePlacing()
        {
            CancelInvoke(nameof(PlacementIdleNudge));

            ISellScale scale = _merchant.SellScale;
            if (scale == null || !scale.HasFish)
            {
                if (++_placementPrompts > _maxPlacementPrompts)
                {
                    Debug.Log($"{LogPrefix} Player never loaded the scale after {_placementPrompts} prompts — giving up.");
                    SpeakThenFinish(_lines.RandomGaveUp(), NegotiationPhase.Declined);
                    return;
                }
                Speak(_lines.RandomEmptyScaleNudge());
                RequestChoices(ScriptedChoice.ConfirmPlacement);   // keep the button up
                ArmPlacementIdle();
                return;
            }

            SnapshotBundle(scale);
            QuoteCurrentBundle(introOnly: true, added: null, removed: null);
        }

        private void HandleAccept()
        {
            ISellScale scale = _merchant.SellScale;

            // Re-verify against the live scale at the moment of acceptance, so we only ever pay for fish
            // actually resting on it (the player may have lifted one just before clicking).
            SnapshotBundle(scale);
            if (_bundle.Count == 0)
            {
                // Nothing to buy — treat like an emptied scale rather than closing on air.
                Speak(_lines.RandomScaleEmptied());
                RequestChoices(ScriptedChoice.Decline);
                return;
            }

            float total = _priceBook.QuoteFor(_merchant, _bundle);
            Dictionary<FishData, float> priceByFish = BuildPriceMap(total);

            RequestChoices();   // hide buttons — the deal is closing

            // Pay out BEFORE the closing line, so a confirmed deal is honoured even if the player walks off
            // while the farewell shows (mirrors the AI SealBundleSaleAsync ordering).
            BundleSaleSettler.Payout(scale, priceByFish, ResolveFishInventory(), _playerStats,
                _calculator, out int soldCount);
            Debug.Log($"{LogPrefix} '{NameOf(_merchant)}' accepted: sold {soldCount} fish for {total:0} EGP.");

            SpeakThenFinish(_lines.RandomAccepted(), NegotiationPhase.Accepted);
        }

        private void HandleDecline()
        {
            RequestChoices();   // hide buttons
            Debug.Log($"{LogPrefix} Player declined '{NameOf(_merchant)}'.");
            SpeakThenFinish(_lines.RandomDeclined(), NegotiationPhase.Declined);
        }

        // --- Scale changes ---------------------------------------------------------

        private void HandleScaleChanged(ScaleContentsChangedEvent evt)
        {
            if (_merchant == null || !ReferenceEquals(evt.Scale, _merchant.SellScale)) return;

            switch (_stage)
            {
                case Stage.AwaitingPlacement:
                    // The player is actively loading fish — reset the idle nudge so they aren't hurried.
                    ArmPlacementIdle();
                    break;
                case Stage.AwaitingDecision:
                    ReconcileDuringDecision(evt.Scale);
                    break;
            }
        }

        // Fold fish added/removed since the last quote into the deal and re-quote off the SAME rate, so the
        // total is always rate × the current fair sum — add/remove symmetric.
        private void ReconcileDuringDecision(ISellScale scale)
        {
            IReadOnlyList<FishData> onScale = scale.FishDataOnScale;

            var added = new List<FishData>();
            var removed = new List<FishData>();
            foreach (FishData f in _bundle)
                if (f != null && !ContainsByRef(onScale, f)) removed.Add(f);
            foreach (FishData f in onScale)
                if (f != null && !ContainsByRef(_bundle, f)) added.Add(f);

            if (added.Count == 0 && removed.Count == 0) return;

            foreach (FishData f in removed) _bundle.Remove(f);
            foreach (FishData f in added) _bundle.Add(f);

            if (_bundle.Count == 0)
            {
                // Emptied mid-decision — do NOT cancel (they may be rearranging). Nudge to reload and keep
                // only Decline available until fish return; the next scale change re-quotes.
                Speak(_lines.RandomScaleEmptied());
                RequestChoices(ScriptedChoice.Decline);
                return;
            }

            QuoteCurrentBundle(introOnly: false, added: added, removed: removed);
        }

        // --- Quoting ---------------------------------------------------------------

        // Snapshots the fish currently resting on the scale into the working bundle (stable FishData refs).
        private void SnapshotBundle(ISellScale scale)
        {
            _bundle.Clear();
            if (scale != null) _bundle.AddRange(scale.FishDataOnScale);
        }

        // Prices the current bundle at the merchant's fixed rate, reads the breakdown, and shows Accept/Decline.
        // introOnly: first quote (optional lead-in line). Otherwise it acknowledges what the player changed.
        private void QuoteCurrentBundle(bool introOnly, List<FishData> added, List<FishData> removed)
        {
            _stage = Stage.AwaitingDecision;

            float total = _priceBook.QuoteFor(_merchant, _bundle);
            float[] prices = _calculator.SplitBundlePrice(_bundle, total);

            string ackEnglish = null;
            if (!introOnly)
            {
                var sb = new StringBuilder();
                if (added != null && added.Count > 0)
                    sb.Append(_lines.RandomCatchAdded(JoinNames(added)).english);
                if (removed != null && removed.Count > 0)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(_lines.RandomCatchRemoved(JoinNames(removed)).english);
                }
                ackEnglish = sb.ToString();
            }
            else
            {
                ackEnglish = _lines.RandomSummaryIntro().english;
            }

            string english = BuildQuoteText(ackEnglish, prices, total);

            // Type the read-back out like every other line, even though there is no voice to pace it.
            Speak(english, arabic: string.Empty, instant: false);
            PublishOffer(total);
            RequestChoices(ScriptedChoice.Accept, ScriptedChoice.Decline);
        }

        // Builds the per-fish breakdown + total subtitle, e.g.
        //   Bouri 1.8 kg — 120 EGP
        //   Denis 0.9 kg — 70 EGP
        //   Total: 190 EGP. Take it or leave it.
        private string BuildQuoteText(string leadIn, float[] prices, float total)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(leadIn)) sb.Append(leadIn).Append('\n');

            for (int i = 0; i < _bundle.Count; i++)
            {
                FishData fish = _bundle[i];
                if (fish == null) continue;
                string name = !string.IsNullOrWhiteSpace(fish.fishType) ? fish.fishType : "Fish";
                float price = i < prices.Length ? prices[i] : 0f;
                sb.Append(name).Append(' ')
                  .Append(fish.weight.ToString("0.##", CultureInfo.InvariantCulture)).Append(" kg — ")
                  .Append(price.ToString("0", CultureInfo.InvariantCulture)).Append(" EGP\n");
            }

            sb.Append("Total: ").Append(total.ToString("0", CultureInfo.InvariantCulture))
              .Append(" EGP. Take it or leave it.");
            return sb.ToString();
        }

        // --- Resolution ------------------------------------------------------------

        // Shows a final line, holds it briefly so it is readable, then tears down with the given outcome.
        private void SpeakThenFinish(ScriptedMerchantLinesSO.Line line, NegotiationPhase phase)
        {
            _stage = Stage.Closing;
            RequestChoices();                 // ensure no buttons linger during the hold
            Speak(line);
            _pendingPhase = phase;
            CancelInvoke(nameof(FinishAfterHold));
            Invoke(nameof(FinishAfterHold), _closingHoldSeconds);
        }

        private NegotiationPhase _pendingPhase;
        private void FinishAfterHold() => Finish(_pendingPhase);

        // Single teardown: clears state, unsubscribes, and hands the outcome back to the caller exactly once.
        private void Finish(NegotiationPhase phase)
        {
            if (!IsRunning && _onEnded == null) return;

            CancelInvoke(nameof(PlacementIdleNudge));
            CancelInvoke(nameof(FinishAfterHold));

            MerchantNPC merchant = _merchant;
            Action<NegotiationPhase> onEnded = _onEnded;

            RequestChoices();   // hide any buttons (published before the merchant ref is cleared)

            _stage = Stage.Idle;
            _merchant = null;
            _onEnded = null;
            _lines = null;
            _bundle.Clear();
            Unsubscribe();

            Debug.Log($"{LogPrefix} Scripted sale with '{NameOf(merchant)}' ended ({phase}).");
            onEnded?.Invoke(phase);   // NegotiationFlowController.Cleanup — publishes MerchantStoppedSpeaking, etc.
        }

        // --- Idle nudge ------------------------------------------------------------

        private void ArmPlacementIdle()
        {
            CancelInvoke(nameof(PlacementIdleNudge));
            Invoke(nameof(PlacementIdleNudge), _placementIdleSeconds);
        }

        private void PlacementIdleNudge()
        {
            if (_stage != Stage.AwaitingPlacement) return;

            if (++_placementPrompts > _maxPlacementPrompts)
            {
                Debug.Log($"{LogPrefix} Player idle through {_placementPrompts} placement nudges — giving up.");
                SpeakThenFinish(_lines.RandomGaveUp(), NegotiationPhase.Declined);
                return;
            }

            // If they've already loaded fish, nudge them to press the button instead of nagging to place fish.
            ISellScale scale = _merchant != null ? _merchant.SellScale : null;
            Speak(scale != null && scale.HasFish
                ? _lines.RandomWelcome()
                : _lines.RandomEmptyScaleNudge());
            RequestChoices(ScriptedChoice.ConfirmPlacement);
            ArmPlacementIdle();
        }

        // --- Event plumbing --------------------------------------------------------

        private void Speak(ScriptedMerchantLinesSO.Line line) => Speak(line.english, line.arabic, false);

        private void Speak(string english, string arabic, bool instant)
        {
            if (_events == null || _merchant == null) return;
            _events.Publish(new MerchantSpokeEvent(_merchant, english, arabic, instant));
        }

        private void RequestChoices(params ScriptedChoice[] options)
        {
            if (_events == null || _merchant == null) return;
            _events.Publish(new ScriptedChoiceRequestedEvent(_merchant, options ?? Array.Empty<ScriptedChoice>()));
        }

        // Surfaces the current quote to the SummaryCard / market ticket (only the number the merchant voiced).
        private void PublishOffer(float amount)
        {
            if (_events == null || _merchant == null || amount <= 0f) return;
            _events.Publish(new MerchantOfferedPriceEvent(_merchant, _merchant.SellScale, amount));
        }

        private void Subscribe()
        {
            if (_subscribed || _events == null) return;
            _events.Subscribe<ScriptedChoiceMadeEvent>(HandleChoiceMade);
            _events.Subscribe<ScaleContentsChangedEvent>(HandleScaleChanged);
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _events == null) return;
            _events.Unsubscribe<ScriptedChoiceMadeEvent>(HandleChoiceMade);
            _events.Unsubscribe<ScaleContentsChangedEvent>(HandleScaleChanged);
            _subscribed = false;
        }

        // --- Helpers ---------------------------------------------------------------

        // The market-wide bank, or this merchant's override, or a runtime-created empty bank (whose built-in
        // fallback strings keep the sale playable even with nothing authored).
        private ScriptedMerchantLinesSO ResolveLines(MerchantNPC merchant)
        {
            ScriptedMerchantLinesSO lines = merchant.Config != null ? merchant.Config.ScriptedLines : null;
            if (lines == null && _systemConfig != null) lines = _systemConfig.ScriptedLines;
            if (lines == null)
            {
                Debug.LogWarning($"{LogPrefix} No ScriptedMerchantLinesSO assigned (merchant or " +
                                 "MerchantSystemConfig) — using built-in fallback lines.");
                lines = _fallbackLines != null
                    ? _fallbackLines
                    : (_fallbackLines = ScriptableObject.CreateInstance<ScriptedMerchantLinesSO>());
            }
            return lines;
        }

        private Dictionary<FishData, float> BuildPriceMap(float total)
        {
            var map = new Dictionary<FishData, float>();
            float[] split = _calculator.SplitBundlePrice(_bundle, total);
            for (int i = 0; i < _bundle.Count; i++)
                if (_bundle[i] != null) map[_bundle[i]] = i < split.Length ? split[i] : 0f;
            return map;
        }

        private IFishInventory ResolveFishInventory()
        {
            if (_fishInventory != null) return _fishInventory;

            if (_fishInventorySource is IFishInventory fromField)
            {
                _fishInventory = fromField;
                return _fishInventory;
            }

            foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb is IFishInventory inv)
                {
                    _fishInventory = inv;
                    return _fishInventory;
                }
            }
            return null;
        }

        private static string JoinNames(List<FishData> fish)
        {
            if (fish == null || fish.Count == 0) return string.Empty;
            var names = new List<string>(fish.Count);
            foreach (FishData f in fish)
            {
                if (f == null) continue;
                names.Add(!string.IsNullOrWhiteSpace(f.fishType) ? f.fishType : "a fish");
            }
            return string.Join(", ", names);
        }

        private static bool ContainsByRef(IReadOnlyList<FishData> list, FishData fish)
        {
            for (int i = 0; i < list.Count; i++)
                if (ReferenceEquals(list[i], fish)) return true;
            return false;
        }

        private static string NameOf(MerchantNPC merchant)
        {
            if (merchant == null) return "<none>";
            return merchant.Config != null ? merchant.Config.MerchantName : merchant.name;
        }
    }
}
