using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace DumyatRuntime
{
    public class FishingLineController : MonoBehaviour
    {
        //Objects that will interact with the rope
        public Transform whatTheRopeIsConnectedTo;
        public Transform whatIsHangingFromTheRope;

        //Line renderer used to display the rope
        private LineRenderer lineRenderer;

        //A list with all rope sections
        public List<Vector3> allRopeSections = new List<Vector3>();

        //Rope data
        [SerializeField] private float ropeLength = 1f;
        [SerializeField] private float minRopeLength = 15f;
        [SerializeField] private float maxRopeLength = 20f;
        //Mass of what the rope is carrying
        private float loadMass = 100f;
        //How fast we can add more/less rope
        float winchSpeed = 1f;

        private List<RopeSegment> ropeSegments = new List<RopeSegment>();
        private float ropeSegmentLenght = 0.1f;
        private int segmentCount = 20;
        private float lineWidth = 0.005f;
        [SerializeField] private int startSegmentCount = 10;

        [SerializeField] private GameObject reelPivot;
        private float previousZRotation;

        //The joint we use to approximate the rope
        SpringJoint springJoint;
        private bool isFreeCasting;


        void Start()
        {
            springJoint = whatIsHangingFromTheRope.GetComponent<SpringJoint>();

            whatIsHangingFromTheRope.position = whatTheRopeIsConnectedTo.position;

            ropeLength = 0f;


            //Init the line renderer we use to display the rope
            lineRenderer = GetComponent<LineRenderer>();

            Vector3 ropeStartPoint = Vector3.zero;
            segmentCount = startSegmentCount;

            for (int i = 0; i < segmentCount; i++)
            {
                ropeSegments.Add(new RopeSegment(ropeStartPoint));
                ropeStartPoint.y += ropeSegmentLenght;
            }

            //Init the spring we use to approximate the rope from point a to b
            UpdateSpring();

            //Add the weight to what the rope is carrying
            whatIsHangingFromTheRope.GetComponent<Rigidbody>().mass = loadMass;
            previousZRotation = reelPivot.transform.localEulerAngles.z;
        }

        void Update()
        {
            //Display the rope with a line renderer
            DisplayRope();
        }
        private void FixedUpdate()
        {
            UpdateWinch();
            Simulation();
        }

        private void InitRope()
        {
            float dist = ropeLength;
            int tempSegmentCount = (int)(dist * 2f) + startSegmentCount;
            tempSegmentCount = Mathf.Max(tempSegmentCount, 1);

            if (tempSegmentCount > ropeSegments.Count)
            {
                while (ropeSegments.Count < tempSegmentCount)
                {
                    Vector3 ropeStartPoint = ropeSegments[ropeSegments.Count - 1].posNow;
                    ropeStartPoint.y += ropeSegmentLenght;
                    ropeSegments.Add(new RopeSegment(ropeStartPoint));
                }
            }
            else if (tempSegmentCount < ropeSegments.Count)
            {
                while (ropeSegments.Count > tempSegmentCount && ropeSegments.Count > 1)
                {
                    ropeSegments.RemoveAt(ropeSegments.Count - 1);
                }
            }

            segmentCount = ropeSegments.Count; // always mirrors the real list size now
        }

        private void Simulation()
        {
            Vector3 forceGravity = new Vector3(0f, -1f, 0f);

            for (int i = 1; i < ropeSegments.Count; i++)
            {
                RopeSegment currentSegment = ropeSegments[i];
                Vector3 velocity = currentSegment.posNow - currentSegment.posOld;
                currentSegment.posOld = currentSegment.posNow;

                RaycastHit hit;
                if (Physics.Raycast(currentSegment.posNow, -Vector3.up, out hit, 1f))
                {
                    if (hit.collider != null)
                    {
                        velocity = Vector3.zero;
                    }
                }

                currentSegment.posNow += velocity;
                currentSegment.posNow += forceGravity * Time.fixedDeltaTime;
                ropeSegments[i] = currentSegment;
            }

            for (int i = 0; i < 20; i++)
            {
                ApplyConstraints();
            }
        }

        private void ApplyConstraints()
        {
            RopeSegment firstSegment = ropeSegments[0];
            firstSegment.posNow = whatTheRopeIsConnectedTo.position;
            ropeSegments[0] = firstSegment;

            RopeSegment endSegment = ropeSegments[ropeSegments.Count - 1];
            endSegment.posNow = whatIsHangingFromTheRope.position;
            ropeSegments[ropeSegments.Count - 1] = endSegment;

            for (int i = 0; i < ropeSegments.Count - 1; i++)
            {
                RopeSegment firstSeg = ropeSegments[i];
                RopeSegment secondSeg = ropeSegments[i + 1];

                float distance = (firstSeg.posNow - secondSeg.posNow).magnitude;
                float error = Mathf.Abs(distance - ropeSegmentLenght);

                Vector3 changeDir = Vector3.zero;

                if (distance > ropeSegmentLenght)
                {
                    changeDir = (firstSeg.posNow - secondSeg.posNow).normalized;
                }
                else if (distance < ropeSegmentLenght)
                {
                    changeDir = (secondSeg.posNow - firstSeg.posNow).normalized;
                }

                Vector3 changeAmount = changeDir * error;

                if (i != 0)
                {
                    firstSeg.posNow -= changeAmount * 0.5f;
                    ropeSegments[i] = firstSeg;
                    secondSeg.posNow += changeAmount * 0.5f;
                    ropeSegments[i + 1] = secondSeg;
                }
                else
                {
                    secondSeg.posNow += changeAmount;
                    ropeSegments[i + 1] = secondSeg;
                }

            }
        }

        //Display the rope with a line renderer
        private void DisplayRope()
        {
            lineRenderer.startWidth = lineWidth;
            lineRenderer.endWidth = lineWidth;

            int count = ropeSegments.Count;
            Vector3[] ropePositions = new Vector3[count];

            for (int i = 0; i < count; i++)
            {
                ropePositions[i] = ropeSegments[i].posNow;
            }

            lineRenderer.positionCount = count;
            lineRenderer.SetPositions(ropePositions);
        }

        //Update the spring constant and the length of the spring
        private void UpdateSpring()
        {
            //Someone said you could set this to infinity to avoid bounce, but it doesnt work
            //kRope = float.inf

            //
            //The mass of the rope
            //
            //Density of the wire (stainless steel) kg/m3
            float density = 7750f;
            //The radius of the wire
            float radius = 0.02f;

            float volume = Mathf.PI * radius * radius * ropeLength;

            float ropeMass = volume * density;

            //Add what the rope is carrying
            ropeMass += loadMass;


            //
            //The spring constant (has to recalculate if the rope length is changing)
            //
            //The force from the rope F = rope_mass * g, which is how much the top rope segment will carry
            float ropeForce = ropeMass * 9.81f;

            //Use the spring equation to calculate F = k * x should balance this force, 
            //where x is how much the top rope segment should stretch, such as 0.01m

            //Is about 146000
            float kRope = ropeForce / 0.01f;

            //print(ropeMass);

            //Add the value to the spring
            springJoint.spring = kRope * 1.0f;
            springJoint.damper = kRope * 0.05f;

            //Update length of the rope
            springJoint.maxDistance = ropeLength;
        }

        //Add more/less rope
        private void UpdateWinch()
        {
            if (isFreeCasting)
                return;

            bool hasChangedRope = false;

            float currentZRotation = reelPivot.transform.localEulerAngles.z;

            float deltaRotation = Mathf.DeltaAngle(previousZRotation, currentZRotation);

            // Reel turned forward
            if (deltaRotation > 0f && ropeLength < maxRopeLength)
            {
                ropeLength += winchSpeed * Time.deltaTime;

                InitRope();

                whatIsHangingFromTheRope.GetComponent<Rigidbody>().WakeUp();

                hasChangedRope = true;
            }
            // Reel turned backward
            else if (deltaRotation < 0f && ropeLength > minRopeLength)
            {
                ropeLength -= winchSpeed * Time.deltaTime;

                InitRope();

                whatIsHangingFromTheRope.GetComponent<Rigidbody>().WakeUp();


                hasChangedRope = true;
            }

            previousZRotation = currentZRotation;

            if (hasChangedRope)
            {
                ropeLength = Mathf.Clamp(ropeLength, minRopeLength, maxRopeLength);

                UpdateSpring();
            }
        }
        public struct RopeSegment
        {
            public Vector3 posNow;
            public Vector3 posOld;
            public RopeSegment(Vector3 pos)
            {
                this.posNow = pos;
                this.posOld = pos;
            }
        }
        // public void SetCastingLength(float length)
        // {
        //     ropeLength = Mathf.Clamp(
        //         length,
        //         0f,
        //         maxRopeLength);

        //     InitRope();

        //     UpdateSpring();
        // }

        public float GetMaxLength()
        {
            return maxRopeLength;
        }
        public void EnterFreeCast()
        {
            isFreeCasting = true;

            if (springJoint != null)
            {
                // Neutralize the joint instead of disabling it —
                // SpringJoint has no .enabled, so we just remove its pull.
                springJoint.spring = 0f;
                springJoint.damper = 0f;
                springJoint.maxDistance = maxRopeLength * 10f; // plenty of slack
            }
        }

        public void SettleCastAtCurrentDistance()
        {
            isFreeCasting = false;

            float landedDistance = Vector3.Distance(
                whatTheRopeIsConnectedTo.position,
                whatIsHangingFromTheRope.position);

            ropeLength = Mathf.Clamp(landedDistance, 0f, maxRopeLength);

            InitRope();
            UpdateSpring(); // restores real spring/damper/maxDistance values
        }
    }
}