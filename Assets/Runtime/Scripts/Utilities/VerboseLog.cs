using System.Diagnostics;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Per-event trace logging that is OFF unless someone asks for it.
    ///
    /// The console had reached the point where the useful lines were unfindable: entering the market
    /// printed eight lip-sync resolutions, a dialogue trace per line, and one
    /// <c>OnTriggerEnter entered by …</c> for every football, chair and crate that brushed a narrative
    /// zone. A warning that actually mattered scrolled past unread, which is the real cost — a console
    /// nobody reads is the same as no console.
    ///
    /// These calls are not deleted, because each one earns its keep while you are working on that
    /// system: the lip-sync lines say which blendshape was chosen and why, and the dialogue trace is
    /// how you see a sequence advance. They are simply silent until asked for.
    ///
    /// TURNING THEM BACK ON: add <c>DUMYAT_VERBOSE</c> to Project Settings ▸ Player ▸ Scripting Define
    /// Symbols. Per-platform, so a build can stay quiet while the Editor talks.
    ///
    /// <see cref="ConditionalAttribute"/> removes the CALL, not just the output — including evaluating
    /// the arguments. The interpolated strings these pass are therefore never built either, which in a
    /// 90 Hz headset is worth more than the console tidiness.
    ///
    /// Warnings and errors deliberately have no equivalent here: something worth a warning is worth
    /// seeing in every build, and routing one through this would be how it gets missed.
    /// </summary>
    public static class VerboseLog
    {
        [Conditional("DUMYAT_VERBOSE")]
        public static void Trace(string message) => UnityEngine.Debug.Log(message);

        /// <summary>Context overload — clicking the entry selects <paramref name="context"/>.</summary>
        [Conditional("DUMYAT_VERBOSE")]
        public static void Trace(string message, Object context) => UnityEngine.Debug.Log(message, context);
    }
}
