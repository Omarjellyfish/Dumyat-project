using UnityEngine;

namespace DumyatRuntime
{
    public class RopeVisuals : MonoBehaviour
    {
        [SerializeField] private LineRenderer line;
        [SerializeField] private Transform pointA;
        [SerializeField] private Transform pointB;

        private void Awake()
        {
            line.positionCount = 2;
        }

        private void LateUpdate()
        {
            line.SetPosition(0, pointA.position);
            line.SetPosition(1, pointB.position);
        }
    }
}
