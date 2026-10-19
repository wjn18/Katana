using UnityEngine;

namespace OCDesign
{
    // Author the weapon pose under the right hand. Assistance changes bones, never weapon transforms.
    [DefaultExecutionOrder(100)]
    public sealed class TwoHandPolearmGrip : MonoBehaviour
    {
        public Animator target;
        [Tooltip("手调此挂点或武器子物体；脚本不会覆盖其 Transform。")]
        public Transform weaponRoot;
        public Transform rightHand;
        public Transform leftHand;
        public Vector3 rightPalmOffset = new Vector3(-0.038f, 0.118f, 0.064f);
        public Vector3 leftPalmOffset = new Vector3(0.038f, 0.118f, 0.064f);
        public PolearmGripSettings weaponSettings;
        [Tooltip("启用时使用角色下方的真实地面高度；默认关闭，保留原平地预览行为。仅影响手臂辅助，不移动武器。")]
        public bool useWorldGround = false;
        private const float GroundProbeLift = 0.5f;
        private const float GroundProbeDistance = 100f;
        private readonly RaycastHit[] groundHits = new RaycastHit[64];
        private Transform selfCollisionRoot;
        private bool hasKnownGroundHeight;
        private float knownGroundHeight;
        private Transform leftUpperArm;
        private Transform leftForearm;
        private Transform rightUpperArm;
        private Transform rightForearm;
        public Vector3 RightPalm => rightHand.TransformPoint(rightPalmOffset);
        public Vector3 LeftPalm => leftHand.TransformPoint(leftPalmOffset);
        public float LeftPalmAxisError { get; private set; }
        public bool LeftGripReachable { get; private set; }

        private void Awake() => ResolveReferences();
        private void LateUpdate() => AlignNow();

        private void ResolveReferences()
        {
            var characterController = GetComponentInParent<CharacterController>();
            selfCollisionRoot = characterController ? characterController.transform : transform;
            if (!target) target = GetComponentInChildren<Animator>();
            if (target && target.isHuman)
            {
                if (!rightHand) rightHand = target.GetBoneTransform(HumanBodyBones.RightHand);
                if (!leftHand) leftHand = target.GetBoneTransform(HumanBodyBones.LeftHand);
                leftUpperArm = target.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                leftForearm = target.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                rightUpperArm = target.GetBoneTransform(HumanBodyBones.RightUpperArm);
                rightForearm = target.GetBoneTransform(HumanBodyBones.RightLowerArm);
            }
            if (weaponRoot && (!weaponSettings || !weaponSettings.transform.IsChildOf(weaponRoot)))
                weaponSettings = weaponRoot.GetComponentInChildren<PolearmGripSettings>();
        }

        private float ResolveFloorHeight()
        {
            float fallback = transform.position.y + weaponSettings.floorOffset;
            if (!useWorldGround) return fallback;
            if (!selfCollisionRoot) selfCollisionRoot = transform;
            Vector3 origin = selfCollisionRoot.position + Vector3.up * GroundProbeLift;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, groundHits,
                GroundProbeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = groundHits[i];
                Transform hitTransform = hit.collider ? hit.collider.transform : null;
                if (!hitTransform || hitTransform == selfCollisionRoot
                    || hitTransform.IsChildOf(selfCollisionRoot) || hit.normal.y < 0.5f) continue;
                if (hit.distance >= nearest) continue;
                nearest = hit.distance;
                knownGroundHeight = hit.point.y;
                hasKnownGroundHeight = true;
            }
            // A ray miss while airborne must not lift the virtual floor with the character.
            return hasKnownGroundHeight ? knownGroundHeight + weaponSettings.floorOffset : fallback;
        }

        private void OnDisable() => hasKnownGroundHeight = false;

        public void AlignNow()
        {
            if (!leftUpperArm || !weaponSettings) ResolveReferences();
            if (!weaponRoot || !weaponSettings || !weaponSettings.assistLeftHand
                || !leftHand || !leftUpperArm || !leftForearm) return;
            Transform space = weaponSettings.shaftSpace ? weaponSettings.shaftSpace : weaponSettings.transform;
            Vector3 localAxis = weaponSettings.shaftAxis.normalized;
            if (localAxis.sqrMagnitude < 0.5f) return;
            // Resolve once per pose, never once per IK fitting pass.
            float floor = weaponSettings.keepAbovePreviewFloor ? ResolveFloorHeight() : 0f;
            Vector3 originalLeftPalm = LeftPalm;
            Quaternion originalLeftRotation = leftHand.rotation;
            if (weaponSettings.assistRightWrist && rightHand)
            {
                Vector3 handLine = LeftPalm - RightPalm;
                if (handLine.sqrMagnitude > 0.0001f)
                    rightHand.rotation = Quaternion.FromToRotation(rightHand.forward, handLine.normalized)
                        * rightHand.rotation;
            }
            for (int fit = 0; fit < 12; fit++)
            {
            if (weaponSettings.keepAbovePreviewFloor && rightHand && rightUpperArm && rightForearm)
            {
                float margin = Mathf.Max(0.02f, weaponSettings.lateralClearance);
                for (int pass = 0; pass < 3; pass++)
                {
                    Vector3 palm = RightPalm;
                    if (palm.y < floor + margin + 0.02f)
                    {
                        SolveArm(rightUpperArm, rightForearm, rightHand,
                            rightHand.position + Vector3.up * (floor + margin + 0.02f - palm.y),
                            rightHand.rotation);
                        palm = RightPalm;
                    }
                    Vector3 shaft = space.TransformVector(localAxis);
                    float units = shaft.magnitude;
                    if (units < 0.0001f) break;
                    shaft /= units;
                    Vector3 bottom = space.TransformPoint(weaponSettings.shaftOrigin);
                    float length = Mathf.Max(0.1f, weaponSettings.shaftLength * units);
                    float grip = Mathf.Clamp(Vector3.Dot(palm - bottom, shaft), 0.05f, length - 0.05f);
                    float lowerY = Mathf.Clamp((floor + margin - palm.y) / (length - grip), -0.98f, 0.98f);
                    float upperY = Mathf.Clamp((palm.y - floor - margin) / grip, -0.98f, 0.98f);
                    if (lowerY > upperY) break;
                    float y = Mathf.Clamp(shaft.y, lowerY, upperY);
                    Vector3 horizontal = new Vector3(shaft.x, 0f, shaft.z);
                    if (horizontal.sqrMagnitude < 0.0001f) horizontal = rightHand.right;
                    horizontal.y = 0f;
                    Vector3 desired = horizontal.normalized * Mathf.Sqrt(1f - y * y) + Vector3.up * y;
                    rightHand.rotation = Quaternion.FromToRotation(shaft, desired) * rightHand.rotation;
                }
            }
            Vector3 origin = space.TransformPoint(weaponSettings.shaftOrigin);
            Vector3 scaledAxis = space.TransformVector(localAxis);
            float scale = scaledAxis.magnitude;
            if (scale < 0.0001f) return;
            Vector3 axis = scaledAxis / scale;
            float low = Mathf.Min(weaponSettings.leftSlideRange.x, weaponSettings.leftSlideRange.y) * scale;
            float high = Mathf.Max(weaponSettings.leftSlideRange.x, weaponSettings.leftSlideRange.y) * scale;
            Quaternion handRotation = originalLeftRotation;
            if (weaponSettings.alignLeftWrist)
            {
                Vector3 sourceForward = originalLeftRotation * Vector3.forward;
                Vector3 handAxis = Vector3.Dot(sourceForward, axis) >= 0f ? axis : -axis;
                handRotation = Quaternion.FromToRotation(sourceForward, handAxis) * handRotation;
            }
            Vector3 palmOffset = handRotation * Vector3.Scale(leftPalmOffset, leftHand.lossyScale);
            float along = Mathf.Clamp(Vector3.Dot(originalLeftPalm - origin, axis), low, high);
            float reach = Vector3.Distance(leftUpperArm.position, leftForearm.position)
                + Vector3.Distance(leftForearm.position, leftHand.position) - 0.0001f;
            Vector3 start = origin - palmOffset - leftUpperArm.position;
            float projection = Vector3.Dot(start, axis);
            float discriminant = projection * projection - start.sqrMagnitude + reach * reach;
            LeftGripReachable = false;
            if (discriminant >= 0f)
            {
                float radius = Mathf.Sqrt(discriminant);
                float reachableLow = Mathf.Max(low, -projection - radius);
                float reachableHigh = Mathf.Min(high, -projection + radius);
                LeftGripReachable = reachableLow <= reachableHigh;
                if (LeftGripReachable) along = Mathf.Clamp(along, reachableLow, reachableHigh);
            }
            Vector3 palmTarget = origin + axis * along;
            if (!LeftGripReachable && weaponSettings.assistRightWrist && rightHand && fit < 11)
            {
                along = Mathf.Clamp(-projection, low, high);
                Vector3 wristTarget = origin + axis * along - palmOffset;
                Vector3 toward = wristTarget - leftUpperArm.position;
                if (toward.sqrMagnitude > 0.0001f)
                {
                    Vector3 reachable = leftUpperArm.position + toward.normalized * (reach - 0.001f);
                    if (rightUpperArm && rightForearm)
                    {
                        Vector3 correction = reachable - wristTarget;
                        correction.y = Mathf.Max(0f, correction.y);
                        Vector3 before = rightHand.position;
                        SolveArm(rightUpperArm, rightForearm, rightHand,
                            before + correction, rightHand.rotation);
                        if (Vector3.Distance(before, rightHand.position) > 0.00001f) continue;
                    }
                    rightHand.rotation = Quaternion.FromToRotation(wristTarget - rightHand.position,
                        reachable - rightHand.position) * rightHand.rotation;
                    continue;
                }
            }
            SolveArm(leftUpperArm, leftForearm, leftHand, palmTarget - palmOffset, handRotation);
            LeftPalmAxisError = Vector3.Distance(LeftPalm, palmTarget);
            break;
            }
        }

        private static void SolveArm(Transform upperArm, Transform forearm, Transform hand,
            Vector3 wristTarget, Quaternion handRotation)
        {
            Vector3 shoulder = upperArm.position;
            Vector3 elbow = forearm.position;
            Vector3 wrist = hand.position;
            float upperLength = Vector3.Distance(shoulder, elbow);
            float lowerLength = Vector3.Distance(elbow, wrist);
            Vector3 direction = wristTarget - shoulder;
            if (direction.sqrMagnitude < 0.000001f) return;
            float distance = Mathf.Clamp(direction.magnitude,
                Mathf.Abs(upperLength - lowerLength) + 0.0001f,
                upperLength + lowerLength - 0.0001f);
            direction.Normalize();
            Vector3 bend = Vector3.ProjectOnPlane(elbow - shoulder, direction);
            if (bend.sqrMagnitude < 0.000001f)
                bend = Vector3.ProjectOnPlane(upperArm.up, direction);
            bend.Normalize();
            float along = (upperLength * upperLength - lowerLength * lowerLength
                + distance * distance) / (2f * distance);
            float height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            Vector3 elbowTarget = shoulder + direction * along + bend * height;
            upperArm.rotation = Quaternion.FromToRotation(elbow - shoulder,
                elbowTarget - shoulder) * upperArm.rotation;
            Vector3 reachableWrist = shoulder + direction * distance;
            forearm.rotation = Quaternion.FromToRotation(hand.position - forearm.position,
                reachableWrist - forearm.position) * forearm.rotation;
            hand.rotation = handRotation;
        }
    }
}
