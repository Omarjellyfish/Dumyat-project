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
            NegotiationFlowController c = GameObject == null ? null : GameObject.GetComponent<NegotiationFlowController>();
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
            NegotiationFlowController c = GameObject == null ? null : GameObject.GetComponent<NegotiationFlowController>();
            return c != null && c.NegotiationOngoing;
        }
    }

    /// <summary>
    /// Behavior-graph CONDITION — the ONLINE/OFFLINE selling SWITCH. True when the AI/ONLINE selling
    /// system should run: scripted mode is not forced AND voice mode (mic + internet consent +
    /// connectivity) is available. Guard the AI branch of the root Selector with it; when it is false the
    /// Selector falls through to the "Run Offline Sale" branch. This is a superset of "Voice Mode
    /// Available" (it also honours the Force Scripted Mode toggle). Delegates to
    /// <see cref="NegotiationFlowController.Bt_ShouldUseOnlineSelling"/>.
    /// </summary>
    [Serializable, GeneratePropertyBag]
    [Condition(
        name: "Use Online Selling",
        story: "Use online selling",
        category: "Conditions/Merchant",
        id: "b1f2a3c4-0003-4a10-9b20-0000000000a3")]
    public partial class UseOnlineSellingCondition : Condition
    {
        public override bool IsTrue()
        {
            NegotiationFlowController c = GameObject == null ? null : GameObject.GetComponent<NegotiationFlowController>();
            return c != null && c.Bt_ShouldUseOnlineSelling();
        }
    }
}
