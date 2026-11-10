using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai
{
    /// <summary>
    /// Opt-in execution/backstab guide. The shared action planners own the standing point
    /// and actual readiness; this component never changes an actor or approves an action.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(120)]
    public sealed class SamuraiExecutionCue : MonoBehaviour
    {
        public enum CueState { Hidden, Approach, FaceTarget, Blocked, Wait, Ready }

        [SerializeField] SamuraiCombatant actor;
        [SerializeField] SamuraiCombatant player;
        [SerializeField] Material cueMaterial;
        [Header("Visibility")]
        [Min(0f)] public float visibleDistance = 16f;
        [Tooltip("Additional visibility filtering only; execution rules remain on the combatant.")]
        public LayerMask visibilityMask = ~0;
        [Header("Body attachment (read only)")]
        public HumanBodyBones survivingBone = HumanBodyBones.RightUpperLeg;
        public HumanBodyBones lethalBone = HumanBodyBones.Hips;
        [Tooltip("World-space outward offset at a two-metre actor height; scaled for the actor's size.")]
        [Min(0f)] public float surfaceOffset = .18f;
        [Min(.01f)] public float bodyRadius = .17f;
        [Header("Backstab heart light")]
        [Tooltip("Only show the standing backstab opportunity; keep the original downed execution cue off on this actor.")]
        public bool backstabOnly;
        [Min(.01f)] public float backstabRadius = .11f;
        [ColorUsage(true, true)] public Color backstabColor = new Color(1.4f, 1.4f, 1.4f, 1f);
        [Tooltip("Heart offset from the chest bone, in actor-left/up metres at a two-metre actor height.")]
        public Vector2 backstabHeartOffset = new Vector2(.07f, .02f);
        [Header("Ground guide")]
        [Min(.05f)] public float standingRadius = .42f;
        [Min(.005f)] public float lineWidth = .025f;
        [Min(0f)] public float groundLift = .025f;
        [Min(.1f)] public float pulseHz = 1.25f;
        public Color waitingColor = new Color(1.35f, .94f, .42f, .85f);
        public Color readyColor = new Color(1.6f, 1.3f, .76f, 1f);
        public Color waitingGroundColor = new Color(.8f, .32f, .035f, .95f);
        public Color readyGroundColor = new Color(1f, .55f, .055f, 1f);

        public SamuraiCombatant Actor => actor;
        public SamuraiCombatant Player => player;
        public CueState State { get; private set; }
        public bool CueVisible => State != CueState.Hidden;
        public bool Ready => State == CueState.Ready;
        public bool IsBackstabOpportunity { get; private set; }
        public string PromptText => !Ready ? string.Empty : IsBackstabOpportunity ? "[E 背刺]" : "[E 处决]";
        public bool ApproachAvailable { get; private set; }
        public Vector3 StandingPoint { get; private set; }
        public Quaternion StandingFacing { get; private set; }
        public Vector3 BodyPoint { get; private set; }

        const int CircleSegments = 40;
        static readonly int TintId = Shader.PropertyToID("_Tint");
        static readonly int ShapeId = Shader.PropertyToID("_Shape");
        static readonly int CueTimeId = Shader.PropertyToID("_CueTime");
        static readonly int DestinationBlendId = Shader.PropertyToID("_DestinationBlend");
        static readonly int DepthTestId = Shader.PropertyToID("_DepthTest");
        readonly RaycastHit[] hits = new RaycastHit[24];
        readonly SamuraiEnemySensors sensors = new SamuraiEnemySensors();
        MaterialPropertyBlock bodyProperties;
        SamuraiCombatant subscribedActor;
        Animator boundAnimator;
        Transform survivingAttachment, lethalAttachment, backstabAttachment;
        Camera viewCamera;
        GameObject visualRoot, groundRoot;
        Transform bodyVisual;
        Mesh bodyMesh;
        Material bodyMaterial;
        MeshRenderer bodyRenderer;
        LineRenderer standingRing, directionArrow;
        float discoveryAfter, pulseClock;
        SamuraiPlayerInput viewingInput;

        public void Configure(SamuraiCombatant owner, SamuraiCombatant viewingPlayer = null)
        {
            Unsubscribe();
            Hide();
            actor = owner != null ? owner : GetComponent<SamuraiCombatant>();
            player = viewingPlayer;
            viewingInput = player != null ? player.GetComponent<SamuraiPlayerInput>() : null;
            boundAnimator = null;
            survivingAttachment = lethalAttachment = backstabAttachment = null;
            viewCamera = null;
            discoveryAfter = 0f;
            if (isActiveAndEnabled) Subscribe();
        }

        /// <summary>Installer initialization. Existing components are never reinitialized.</summary>
        public void ConfigurePresentation(Material material)
        {
            cueMaterial = material;
            DestroyVisuals();
        }

        void Awake() { if (actor == null) actor = GetComponent<SamuraiCombatant>(); }
        void OnEnable() { Subscribe(); }
        void OnDisable() { Unsubscribe(); Hide(); }
        void OnDestroy() { Unsubscribe(); DestroyVisuals(); }
        void LateUpdate() { RefreshCue(Time.deltaTime); }

        void Subscribe()
        {
            if (actor == null) actor = GetComponent<SamuraiCombatant>();
            if (subscribedActor == actor) return;
            Unsubscribe();
            subscribedActor = actor;
            if (subscribedActor == null) return;
            subscribedActor.CombatReset += Hide;
            subscribedActor.Died += Hide;
            subscribedActor.StateChanged += OnStateChanged;
        }

        void Unsubscribe()
        {
            if (subscribedActor != null)
            {
                subscribedActor.CombatReset -= Hide;
                subscribedActor.Died -= Hide;
                subscribedActor.StateChanged -= OnStateChanged;
            }
            subscribedActor = null;
        }

        void OnStateChanged(string state)
        {
            if (actor == null || actor.IsDead || (!actor.IsExecutionVulnerable && !actor.HasBackstabReceiver)
                || actor.IsBeingExecuted || actor.IsExecuting) Hide();
        }

        /// <summary>Public for paused/manual preview verification; does not advance combat.</summary>
        public void RefreshCue(float deltaTime)
        {
            if (!isActiveAndEnabled || actor == null || !actor.isActiveAndEnabled || actor.IsDead
                || actor.IsBeingExecuted || actor.IsExecuting || visibleDistance <= 0f)
            { Hide(); return; }
            bool downOpportunity = actor.IsExecutionVulnerable && actor.HasExecutionReceiver;
            if (backstabOnly && downOpportunity) { Hide(); return; }
            if (!downOpportunity && !actor.HasBackstabReceiver) { Hide(); return; }
            DiscoverViewer();
            if (player == null || viewingInput == null || !viewingInput.isActiveAndEnabled || !player.isActiveAndEnabled || player.IsDead || player.IsExecuting
                || player.IsBeingExecuted || player.Team == actor.Team || player.Profile == null
                || player.gameObject.scene != actor.gameObject.scene
                || viewCamera == null || !viewCamera.isActiveAndEnabled
                || (player.transform.position - actor.transform.position).sqrMagnitude > visibleDistance * visibleDistance)
            { Hide(); return; }

            // Backstab opportunities are shown only for the same unique candidate used by E.
            // Unaware-but-ineligible targets must not advertise an approach or blocked marker.
            bool backstabOpportunity = !downOpportunity;
            if (backstabOpportunity && (!viewingInput.HasCursorCapture || !player.IsBackstabCandidate(actor) || player.FindBackstabTarget() != actor))
            { Hide(); return; }
            if (downOpportunity && !player.Profile.executionEnabled) { Hide(); return; }

            BindAttachments();
            Vector3 point; Quaternion facing;
            ApproachAvailable = backstabOpportunity
                ? player.TryGetBackstabApproach(actor, out point, out facing)
                : player.TryGetExecutionApproach(actor, out point, out facing);
            if (backstabOpportunity && !ApproachAvailable) { Hide(); return; }
            IsBackstabOpportunity = backstabOpportunity;
            StandingPoint = point;
            StandingFacing = facing;
            BodyPoint = backstabOpportunity ? AttachedBackstabPoint() : AttachedBodyPoint(point);
            Vector3 viewport = viewCamera.WorldToViewportPoint(BodyPoint);
            if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f
                || !sensors.HasLineOfSight(player, actor, visibilityMask & player.Profile.obstructionMask)
                || !CameraCanSee(BodyPoint))
            { Hide(); return; }
            if (!EnsureVisuals()) { Hide(); return; }

            // This is intentionally the exact input gate, never a separate visual approximation.
            bool ready = ApproachAvailable && (backstabOpportunity || player.CanExecuteTarget(actor));
            if (backstabOpportunity && !ready) { Hide(); return; }
            State = ready ? CueState.Ready : ChooseHint();
            visualRoot.SetActive(true);
            pulseClock += Mathf.Max(0f, deltaTime);
            float pulse = .5f + .5f * Mathf.Sin(pulseClock * pulseHz * Mathf.PI * 2f);
            Color tint = backstabOpportunity ? backstabColor : ready ? readyColor : waitingColor;
            bodyVisual.SetPositionAndRotation(BodyPoint, viewCamera.transform.rotation);
            // A soft, irregular light patch with free space for fine radiating motes.
            // The skeleton and the contact/standing plans are strictly read-only here.
            bodyVisual.localScale = new Vector3(1f, 1.12f, 1f) * ((backstabOpportunity ? backstabRadius : bodyRadius) * 5.2f);
            bodyProperties.SetColor(TintId, new Color(tint.r, tint.g, tint.b, tint.a * Mathf.Lerp(.86f, 1f, pulse)));
            bodyProperties.SetFloat(ShapeId, backstabOpportunity ? 2f : 1f);
            bodyProperties.SetFloat(CueTimeId, pulseClock);
            bodyRenderer.SetPropertyBlock(bodyProperties);

            // A ready heart light is sufficient for backstabs. The original execution
            // standing ring/arrow remains available only to its existing downed cue.
            if (backstabOpportunity) { groundRoot.SetActive(false); return; }

            Vector3 ground = point, normal = Vector3.up;
            bool hasGround = ApproachAvailable && TryGround(point, out ground, out normal);
            // Invalid plans must never advertise a traversable standing marker.
            groundRoot.SetActive(hasGround);
            if (hasGround)
            {
                Vector3 forward = Vector3.ProjectOnPlane(facing * Vector3.forward, normal).normalized;
                if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
                Vector3 right = Vector3.Cross(normal, forward).normalized;
                Vector3 centre = ground + normal * groundLift;
                for (int i = 0; i < CircleSegments; i++)
                {
                    float angle = i * Mathf.PI * 2f / CircleSegments;
                    standingRing.SetPosition(i, centre + standingRadius * (right * Mathf.Cos(angle) + forward * Mathf.Sin(angle)));
                }
                Vector3 arrowTip = centre + forward * (standingRadius + .18f);
                directionArrow.SetPosition(0, arrowTip - forward * .22f - right * .15f);
                directionArrow.SetPosition(1, arrowTip);
                directionArrow.SetPosition(2, arrowTip - forward * .22f + right * .15f);
                Color groundTint = ready ? readyGroundColor : waitingGroundColor;
                SetLineAppearance(standingRing, groundTint);
                SetLineAppearance(directionArrow, groundTint);
            }
        }

        CueState ChooseHint()
        {
            if (!ApproachAvailable) return CueState.Blocked;
            if (!player.CanAct || player.IsHitStopped) return CueState.Wait;
            Vector3 remaining = StandingPoint - player.transform.position;
            remaining.y = 0f;
            if (remaining.sqrMagnitude > .75f * .75f) return CueState.Approach;
            Vector3 direction = actor.transform.position - player.transform.position;
            direction.y = 0f;
            // This angle only selects diagnostic state; CanExecuteTarget remains the sole ready gate.
            return direction.sqrMagnitude > .001f && Vector3.Angle(player.transform.forward, direction) > 60f
                ? CueState.FaceTarget : CueState.Approach;
        }

        void DiscoverViewer()
        {
            if (viewCamera != null && viewCamera.isActiveAndEnabled && viewingInput != null && viewingInput.isActiveAndEnabled && player != null && player.isActiveAndEnabled
                && !player.IsDead && player.gameObject.scene == actor.gameObject.scene) return;
            if (Time.unscaledTime < discoveryAfter) return;
            discoveryAfter = Time.unscaledTime + 1f;
            if (player == null || !player.isActiveAndEnabled || player.IsDead || player.gameObject.scene != actor.gameObject.scene)
            {
                var inputs = FindObjectsByType<SamuraiPlayerInput>(FindObjectsInactive.Exclude);
                player = null;
                for (int i = 0; i < inputs.Length; i++)
                {
                    if (!inputs[i].isActiveAndEnabled || inputs[i].gameObject.scene != actor.gameObject.scene) continue;
                    var candidate = inputs[i].actor != null ? inputs[i].actor : inputs[i].GetComponent<SamuraiCombatant>();
                    if (candidate != null && candidate.isActiveAndEnabled && !candidate.IsDead && candidate.Team != actor.Team)
                    { player = candidate; break; }
                }
            }
            viewingInput = player != null ? player.GetComponent<SamuraiPlayerInput>() : null;
            viewCamera = viewingInput != null && viewingInput.cameraTransform != null ? viewingInput.cameraTransform.GetComponent<Camera>() : null;
            if (viewCamera == null) viewCamera = Camera.main;
        }

        void BindAttachments()
        {
            var animator = actor.Animator;
            if (animator == boundAnimator) return;
            boundAnimator = animator;
            survivingAttachment = lethalAttachment = backstabAttachment = null;
            if (animator == null) return;
            if (animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman)
            {
                survivingAttachment = animator.GetBoneTransform(survivingBone);
                lethalAttachment = animator.GetBoneTransform(lethalBone);
                backstabAttachment = animator.GetBoneTransform(HumanBodyBones.Chest);
                if (backstabAttachment == null) backstabAttachment = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            }
            else
            {
                // Enemy1 intentionally keeps its Generic runtime rig.
                foreach (var bone in animator.GetComponentsInChildren<Transform>(true))
                {
                    if (bone.name == "Spine1") { backstabAttachment = bone; break; }
                    if (bone.name == "Spine2") backstabAttachment = bone;
                }
            }
        }

        Vector3 AttachedBackstabPoint()
        {
            float height = actor.Motor != null ? actor.Motor.height * Mathf.Abs(actor.transform.lossyScale.y) : 2f;
            Vector3 anchor = backstabAttachment != null ? backstabAttachment.position : actor.transform.position + Vector3.up * height * .55f;
            float scale = Mathf.Clamp(height * .5f, .7f, 2.5f);
            return anchor + (-actor.transform.right * backstabHeartOffset.x
                + actor.transform.up * backstabHeartOffset.y - actor.transform.forward * surfaceOffset) * scale;
        }

        Vector3 AttachedBodyPoint(Vector3 approach)
        {
            var attachment = actor.Health <= actor.ConfiguredExecutionDamage ? lethalAttachment : survivingAttachment;
            float height = actor.Motor != null ? actor.Motor.height * Mathf.Abs(actor.transform.lossyScale.y) : 2f;
            Vector3 anchor = attachment != null ? attachment.position : actor.transform.position + Vector3.up * height * .28f;
            Vector3 outward = approach - anchor;
            outward.y = 0f;
            if (outward.sqrMagnitude < .001f) outward = actor.transform.forward;
            return anchor + outward.normalized * surfaceOffset * Mathf.Clamp(height * .5f, .7f, 2.5f);
        }

        bool CameraCanSee(Vector3 point)
        {
            Vector3 ray = point - viewCamera.transform.position;
            float distance = ray.magnitude;
            if (distance < .01f) return true;
            int count = Physics.RaycastNonAlloc(viewCamera.transform.position, ray / distance, hits, distance,
                visibilityMask, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) return false;
            for (int i = 0; i < count; i++)
            {
                var owner = hits[i].collider.GetComponentInParent<SamuraiCombatant>();
                if (owner != actor && owner != player && hits[i].distance < distance - .03f) return false;
            }
            return true;
        }

        // This ray only places ink slightly above the floor. It never approves an execution.
        bool TryGround(Vector3 point, out Vector3 ground, out Vector3 normal)
        {
            ground = point; normal = Vector3.up;
            int count = Physics.RaycastNonAlloc(point + Vector3.up * .75f, Vector3.down, hits, 2f,
                visibilityMask, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) return false;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider.GetComponentInParent<SamuraiCombatant>() != null || hits[i].normal.y < .55f
                    || hits[i].distance >= nearest) continue;
                nearest = hits[i].distance; ground = hits[i].point; normal = hits[i].normal;
            }
            return !float.IsPositiveInfinity(nearest);
        }

        bool EnsureVisuals()
        {
            if (visualRoot != null) return true;
            if (cueMaterial == null) return false;
            if (bodyProperties == null) bodyProperties = new MaterialPropertyBlock();
            visualRoot = new GameObject("Execution opportunity (runtime)") { hideFlags = HideFlags.DontSave };
            SceneManager.MoveGameObjectToScene(visualRoot, actor.gameObject.scene);
            visualRoot.SetActive(false);
            var body = new GameObject("Vulnerability glow", typeof(MeshFilter), typeof(MeshRenderer));
            body.transform.SetParent(visualRoot.transform, false);
            bodyVisual = body.transform;
            bodyMesh = new Mesh { name = "Execution cue billboard", hideFlags = HideFlags.DontSave };
            bodyMesh.vertices = new[] { new Vector3(-.5f,-.5f,0), new Vector3(.5f,-.5f,0), new Vector3(.5f,.5f,0), new Vector3(-.5f,.5f,0) };
            bodyMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            bodyMesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            bodyMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            bodyMesh.RecalculateBounds();
            body.GetComponent<MeshFilter>().sharedMesh = bodyMesh;
            bodyRenderer = body.GetComponent<MeshRenderer>();
            // Visibility queries reject walls first. This copy only bypasses the actor/player's
            // own armour depth; the shared floor-line material retains its normal depth test.
            bodyMaterial = new Material(cueMaterial) { name = "Execution body cue (runtime)", hideFlags = HideFlags.DontSave };
            bodyMaterial.SetFloat(DepthTestId, (float)CompareFunction.Always);
            bodyMaterial.SetFloat(DestinationBlendId, (float)BlendMode.One);
            bodyRenderer.sharedMaterial = bodyMaterial;
            bodyRenderer.shadowCastingMode = ShadowCastingMode.Off;
            bodyRenderer.receiveShadows = false;
            groundRoot = new GameObject("Standing point and facing");
            groundRoot.transform.SetParent(visualRoot.transform, false);
            standingRing = NewLine("Standing ring", groundRoot.transform, CircleSegments, true);
            directionArrow = NewLine("Face this way", groundRoot.transform, 3, false);
            return true;
        }

        LineRenderer NewLine(string name, Transform parent, int count, bool loop)
        {
            var lineObject = new GameObject(name, typeof(LineRenderer));
            lineObject.transform.SetParent(parent, false);
            var line = lineObject.GetComponent<LineRenderer>();
            line.sharedMaterial = cueMaterial;
            line.useWorldSpace = true; line.loop = loop; line.positionCount = count;
            line.alignment = LineAlignment.View; line.numCornerVertices = 2; line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
            line.startWidth = line.endWidth = lineWidth;
            return line;
        }

        void SetLineAppearance(LineRenderer line, Color tint)
        {
            line.startColor = line.endColor = tint;
            line.startWidth = line.endWidth = lineWidth;
        }

        public void Hide()
        {
            State = CueState.Hidden;
            IsBackstabOpportunity = false;
            ApproachAvailable = false;
            if (visualRoot != null) visualRoot.SetActive(false);
        }

        void DestroyVisuals()
        {
            Hide();
            if (visualRoot != null) Destroy(visualRoot);
            if (bodyMesh != null) Destroy(bodyMesh);
            if (bodyMaterial != null) Destroy(bodyMaterial);
            visualRoot = groundRoot = null;
            bodyMesh = null;
            bodyMaterial = null;
        }
    }
}
