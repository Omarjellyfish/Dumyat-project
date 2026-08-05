using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Y-axis billboard that faces the XR camera. Placed on the tutorial hover UI so the panel always
    /// faces the player without tilting vertically. The facing itself — and the camera lookup behind
    /// it — belong to <see cref="CameraBillboard"/>, which every world-space panel in the project
    /// shares; this component only decides WHICH transform gets billboarded and in which mode.
    /// </summary>
    public class TutorialBillboard : MonoBehaviour
    {
        private readonly CameraBillboard _billboard = new CameraBillboard();

        private void LateUpdate()
        {
            // YawOnly: the hover panel is anchored in the world and should read as a standing sign,
            // never tipping toward a head that is above or below it.
            _billboard.Apply(transform, BillboardMode.YawOnly);
        }
    }
}
