using UnityEngine;

namespace DumyatRuntime
{
    public class SawTip : MonoBehaviour
    {
        [SerializeField] SawBladePoint _bladeTip;
        [SerializeField] SawBladePoint _bladeMid;
        [SerializeField] SawBladePoint _bladeBase;
        bool isAnyTouching = false;
        void FixedUpdate()
        {
            if (_bladeTip._isTouchingWood || _bladeMid._isTouchingWood || _bladeBase._isTouchingWood)
            isAnyTouching = true;
            else
            isAnyTouching = false;
        }
    }
}
