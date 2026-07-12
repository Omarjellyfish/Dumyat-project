using System;
using Unity.Behavior;
using Unity.Properties;

namespace DumyatRuntime
{
    /// <summary>
    /// Behavior-graph CONDITION: true while voice mode (mic + internet consent + connectivity) is
    /// available. Gates the whole AI negotiation branch; when false the graph runs the scripted
    /// fallback. Delegates to <see cref="NegotiationFlowController.Bt_ConsentAvailable"/>.
    /// </summary>
    [Serializable, GeneratePropertyBag]
    [Condition(
        name: "Voice Mode Available",
        story: "Voice mode is available",
        category: "Conditions/Merchant",
        id: "b1f2a3c4-0001-4a10-9b20-0000000000a1")]
    public partial class VoiceModeAvailableCondition : Condition
    {
        public override bool IsTrue()
        {
            NegotiationFlowController c = NegotiationFlowController.Singleton;
            return c != null && c.Bt_ConsentAvailable();
        }
    }

    /// <summary>
    /// Behavior-graph CONDITION: true while the negotiation should keep looping (busy and not yet
    /// resolved). Use it as the "Repeat While" condition around the turn loop. Delegates to
    /// <see cref="NegotiationFlowController.NegotiationOngoing"/>.
    /// </summary>
    [Serializable, GeneratePropertyBag]
    [Condition(
        name: "Negotiation Ongoing",
        story: "Negotiation is ongoing",
        category: "Conditions/Merchant",
        id: "b1f2a3c4-0002-4a10-9b20-0000000000a2")]
    public partial class NegotiationOngoingCondition : Condition
    {
        public override bool IsTrue()
        {
            NegotiationFlowController c = NegotiationFlowController.Singleton;
            return c != null && c.NegotiationOngoing;
        }
    }
}
