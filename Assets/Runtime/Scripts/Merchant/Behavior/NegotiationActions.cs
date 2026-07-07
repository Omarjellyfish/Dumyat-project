using System;
using System.Threading.Tasks;
using Unity.Behavior;
using Unity.Properties;
using Action = Unity.Behavior.Action;

namespace DumyatRuntime
{
    // Behavior-graph ACTION nodes for the merchant negotiation. Each wraps one Bt_* step on
    // NegotiationFlowController (reached via its Singleton, matching the project's singleton
    // convention). Synchronous steps resolve in OnStart; async steps (LLM) poll a Task in OnUpdate;
    // TTS/STT steps poll a completion flag. See Merchant/Behavior for the intended graph shape.

    /// <summary>Resolve the fish + build negotiation state + warm the STT session.</summary>
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "Setup Negotiation", story: "Setup negotiation",
        category: "Action/Merchant", id: "c2a10001-4b20-4c30-8d40-000000000001")]
    public partial class SetupNegotiationAction : Action
    {
        protected override Status OnStart()
        {
            NegotiationFlowController c = NegotiationFlowController.Singleton;
            if (c == null) return Status.Failure;
            return c.Bt_Setup() ? Status.Success : Status.Failure;
        }
    }

    /// <summary>Deterministic opening offer + deception roll, then LLM phrasing (async).</summary>
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "Generate Opening Line", story: "Generate opening line",
        category: "Action/Merchant", id: "c2a10002-4b20-4c30-8d40-000000000002")]
    public partial class GenerateOpeningLineAction : Action
    {
        private Task<bool> _task;

        protected override Status OnStart()
        {
            NegotiationFlowController c = NegotiationFlowController.Singleton;
            if (c == null) return Status.Failure;
            _task = c.Bt_RunOpeningAsync();
            return Status.Running;
        }

        protected override Status OnUpdate()
        {
            if (_task == null) return Status.Failure;
            if (!_task.IsCompleted) return Status.Running;
            return _task.Result ? Status.Success : Status.Failure;
        }

        protected override void OnEnd() => _task = null;
    }

    /// <summary>Speak the queued merchant line via TTS; Running until it finishes (or fails).</summary>
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "Speak Merchant Line", story: "Speak merchant line",
        category: "Action/Merchant", id: "c2a10003-4b20-4c30-8d40-000000000003")]
    public partial class SpeakMerchantLineAction : Action
    {
        protected override Status OnStart()
        {
            NegotiationFlowController c = NegotiationFlowController.Singleton;
            if (c == null) return Status.Failure;
            c.Bt_PlayPendingLine();
            return c.LineFinished ? Status.Success : Status.Running;
        }

        protected override Status OnUpdate()
        {
            NegotiationFlowController c = NegotiationFlowController.Singleton;
            if (c == null) return Status.Failure;
            return c.LineFinished ? Status.Success : Status.Running;
        }
    }

    /// <summary>Arm the mic and wait for the player's utterance. Success on transcript, Failure on capture error.</summary>
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "Listen For Player", story: "Listen for player",
        category: "Action/Merchant", id: "c2a10004-4b20-4c30-8d40-000000000004")]
    public partial class ListenForPlayerAction : Action
    {
        protected override Status OnStart()
        {
            NegotiationFlowController c = NegotiationFlowController.Singleton;
            if (c == null) return Status.Failure;
            c.Bt_BeginListen();
            return Status.Running;
        }

        protected override Status OnUpdate()
        {
            NegotiationFlowController c = NegotiationFlowController.Singleton;
            if (c == null) return Status.Failure;
            if (c.CaptureFailed) return Status.Failure;
            return c.TranscriptReady ? Status.Success : Status.Running;
        }
    }

    /// <summary>Run the player's utterance through the LLM (async). Success when a valid turn is produced.</summary>
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "Process Player Turn", story: "Process player turn",
        category: "Action/Merchant", id: "c2a10005-4b20-4c30-8d40-000000000005")]
    public partial class ProcessPlayerTurnAction : Action
    {
        private Task<bool> _task;

        protected override Status OnStart()
        {
            NegotiationFlowController c = NegotiationFlowController.Singleton;
            if (c == null) return Status.Failure;
            _task = c.Bt_ProcessTurnAsync();
            return Status.Running;
        }

        protected override Status OnUpdate()
        {
            if (_task == null) return Status.Failure;
            if (!_task.IsCompleted) return Status.Running;
            return _task.Result ? Status.Success : Status.Failure;
        }

        protected override void OnEnd() => _task = null;
    }

    /// <summary>Apply the stored turn result to game state; queues the next line + sets terminal on Accept/Walk.</summary>
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "Apply Turn Result", story: "Apply turn result",
        category: "Action/Merchant", id: "c2a10006-4b20-4c30-8d40-000000000006")]
    public partial class ApplyTurnResultAction : Action
    {
        protected override Status OnStart()
        {
            NegotiationFlowController c = NegotiationFlowController.Singleton;
            if (c == null) return Status.Failure;
            c.Bt_ApplyResult();
            return Status.Success;
        }
    }

    /// <summary>Queue a generic "say that again" line, or give up (sets terminal) past the recovery limit.</summary>
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "Recover Turn", story: "Recover turn",
        category: "Action/Merchant", id: "c2a10007-4b20-4c30-8d40-000000000007")]
    public partial class RecoverTurnAction : Action
    {
        protected override Status OnStart()
        {
            NegotiationFlowController c = NegotiationFlowController.Singleton;
            if (c == null) return Status.Failure;
            c.Bt_RecoverTurn();
            return Status.Success;
        }
    }

    /// <summary>Record the outcome + tear down (notifies the spawner). Runs after the loop resolves.</summary>
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "Resolve Negotiation", story: "Resolve negotiation",
        category: "Action/Merchant", id: "c2a10008-4b20-4c30-8d40-000000000008")]
    public partial class ResolveNegotiationAction : Action
    {
        protected override Status OnStart()
        {
            NegotiationFlowController c = NegotiationFlowController.Singleton;
            if (c == null) return Status.Failure;
            c.Bt_Resolve();
            return Status.Success;
        }
    }

    /// <summary>Hand to the scripted fallback + tear down. Runs on the early-failure branch.</summary>
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "Scripted Fallback", story: "Scripted fallback",
        category: "Action/Merchant", id: "c2a10009-4b20-4c30-8d40-000000000009")]
    public partial class ScriptedFallbackAction : Action
    {
        protected override Status OnStart()
        {
            NegotiationFlowController c = NegotiationFlowController.Singleton;
            if (c == null) return Status.Failure;
            c.Bt_ScriptedFallback();
            return Status.Success;
        }
    }
}
