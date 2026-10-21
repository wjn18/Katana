using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OCDesign.Samurai
{
    /// <summary>
    /// 实际刀刃周围的蓄力风丝和一次性满蓄爆闪。仅读取动作，不改刀身光、挂点或伤害。
    /// 一个运行时网格同时承载风丝、风带、世界空间锁定的爆发线/环和朝向镜头的闪光。
    /// </summary>
    [DefaultExecutionOrder(320), DisallowMultipleComponent]
    public sealed class SamuraiChargeWind : MonoBehaviour
    {
        [SerializeField] SamuraiCombatant actor;
        [SerializeField] Transform bladeBase;
        [SerializeField] Transform bladeTip;
        [SerializeField] Material windMaterial;
        [SerializeField, Range(0f, 3f)] float flowIntensity = 1f;
        [SerializeField, Range(.03f, .4f)] float initialRadius = .10f;
        [SerializeField, Range(.05f, .5f)] float fullRadius = .24f;
        [SerializeField, Range(.15f, 1f)] float heldFlowMultiplier = .44f;
        [SerializeField, Range(.12f, .6f)] float completionBurstSeconds = .30f;
        [SerializeField, Range(0f, 3f)] float burstIntensity = 1f;
        [SerializeField, Range(.1f, .8f)] float burstRadius = .46f;
        [SerializeField, Range(.1f, .7f)] float flashRadius = .38f;
        [SerializeField, ColorUsage(true, true)] Color windColor = new Color(.80f, .94f, 1f, 1f);

        const int FilamentCount = 12;
        const int BandCount = 5;
        const int FilamentSegments = 20;
        const int BandSegments = 28;
        const int BurstRayCount = 14;
        const int RingSegments = 40;
        const int BurstWindArcCount = 3;
        const int BurstWindArcSegments = 13;
        const float Tau = Mathf.PI * 2f;
        static readonly int FlowTimeId = Shader.PropertyToID("_FlowTime");

        readonly List<Vector3> vertices = new List<Vector3>(1024);
        readonly List<Color> colors = new List<Color>(1024);
        readonly List<Vector2> uv0 = new List<Vector2>(1024);
        readonly List<Vector2> uv1 = new List<Vector2>(1024);
        readonly List<int> indices = new List<int>(3072);
        SamuraiCombatant subscribedActor;
        GameObject runtimeObject;
        Mesh mesh;
        MeshRenderer meshRenderer;
        MaterialPropertyBlock properties;
        Camera cachedCamera;
        Matrix4x4 worldToLocal;
        Vector3 viewPosition, viewRight, viewUp, viewForward;
        Vector3 burstOrigin, burstAxis, burstRight, burstUp;
        float previousChargeElapsed, burstAge, burstFlowTime;
        bool chargeArmed, completionObserved, hasChargeSample;
        bool burstActive, burstJustStarted;

        public bool IsVisible => meshRenderer != null && meshRenderer.enabled
            && meshRenderer.gameObject.activeInHierarchy && RuntimeVertexCount > 0;
        public bool IsBurstVisible { get; private set; }
        public float FlowStrength { get; private set; }
        public int CompletionBurstCount { get; private set; }
        public int RuntimeVertexCount => mesh != null ? mesh.vertexCount : 0;
        public float BurstAge => burstActive ? burstAge : 0f;

        public void Configure(SamuraiCombatant targetActor, Transform targetBase, Transform targetTip,
            Material material)
        {
            Unsubscribe();
            ClearVisual();
            DestroyRuntimeObjects();
            actor = targetActor;
            bladeBase = targetBase;
            bladeTip = targetTip;
            windMaterial = material;
            cachedCamera = null;
            if (Application.isPlaying && isActiveAndEnabled)
            {
                Subscribe();
                ObserveExistingCharge();
            }
        }

        void OnEnable()
        {
            ClearVisual();
            if (!Application.isPlaying) return;
            Subscribe();
            ObserveExistingCharge();
        }

        void OnDisable() { Unsubscribe(); ClearVisual(); DestroyRuntimeObjects(); }
        void OnDestroy() { Unsubscribe(); DestroyRuntimeObjects(); }
        void LateUpdate() => UpdateVisual(Time.deltaTime);

        void Subscribe()
        {
            if (subscribedActor == actor) return;
            Unsubscribe();
            subscribedActor = actor;
            if (subscribedActor != null) subscribedActor.StateChanged += OnStateChanged;
        }

        void Unsubscribe()
        {
            if (subscribedActor != null) subscribedActor.StateChanged -= OnStateChanged;
            subscribedActor = null;
        }

        void ObserveExistingCharge()
        {
            if (actor == null || !actor.isActiveAndEnabled || !actor.IsCharging) return;
            chargeArmed = true;
            // 启用/重新接线时只恢复流动，绝不补播早已达到的满蓄瞬间。
            completionObserved = actor.ChargeNormalizedTime >= 1f;
            previousChargeElapsed = actor.ChargeElapsedSeconds;
            hasChargeSample = Finite(previousChargeElapsed);
        }

        void OnStateChanged(string state)
        {
            if (actor == null || !actor.isActiveAndEnabled) { ClearVisual(); return; }
            if (actor.IsCharging)
            {
                ClearVisual();
                chargeArmed = true;
                return;
            }
            bool releasedCharged = actor.Profile != null && actor.Profile.charged != null
                && state == actor.Profile.charged.stateName && actor.IsAttacking;
            if (chargeArmed && releasedCharged)
            {
                // Enter在新动作完整快照之前发事件。已进入过Charge且成功转Charged即是有效满蓄释放；
                // 可覆盖同一Tick刚到阈值就松开的情况，直接TryStartAttack(Charged)不会走这里。
                if (!completionObserved) StartCompletionBurst();
                chargeArmed = false;
                FlowStrength = 0f;
                // 旧风网格立即隐藏；LateUpdate只重建已锁在完成位置的爆发尾迹。
                if (meshRenderer != null) meshRenderer.enabled = false;
                return;
            }
            ClearVisual();
        }

        public void UpdateVisual() => UpdateVisual(Time.deltaTime);

        /// <summary>只更新表现。风相位用蓄力经过时间；爆发用缩放时间，首可见帧不推进年龄。</summary>
        public void UpdateVisual(float deltaTime)
        {
            if (!Application.isPlaying || !isActiveAndEnabled || !Finite(deltaTime) || deltaTime < 0f)
            { ClearVisual(); return; }
            if (subscribedActor != actor)
            {
                ClearVisual();
                Subscribe();
                ObserveExistingCharge();
            }
            if (!ValidReferences()) { ClearVisual(); return; }
            bool charging = actor.IsCharging;
            bool releasedCharged = actor.IsAttacking && actor.Profile != null
                && actor.Actions != null && actor.Actions.ActiveAction == actor.Profile.charged;
            if (!charging && !(releasedCharged && burstActive)) { ClearVisual(); return; }

            float elapsed = charging ? actor.ChargeElapsedSeconds : burstFlowTime;
            float progress = charging ? actor.ChargeNormalizedTime : 0f;
            if (!Finite(elapsed) || !Finite(progress) || elapsed < 0f)
            { ClearVisual(); return; }
            progress = Mathf.Clamp01(progress);
            if (charging)
            {
                if (hasChargeSample && elapsed + .0001f < previousChargeElapsed)
                {
                    // 兼容外部运行时复位/倒回：旧爆发不连接到新一轮。
                    ClearVisual();
                    chargeArmed = true;
                    completionObserved = progress >= 1f;
                }
                if (chargeArmed && !completionObserved && progress >= 1f) StartCompletionBurst();
                previousChargeElapsed = elapsed;
                hasChargeSample = true;
            }

            if (burstActive && !burstJustStarted) burstAge += deltaTime;
            float burstDuration = Safe(completionBurstSeconds, .30f, .12f, .6f);
            if (burstActive && burstAge >= burstDuration) burstActive = false;
            float required = actor.Profile != null ? Mathf.Max(.1f, actor.Profile.chargeSeconds) : 1.5f;
            float holdFade = progress >= 1f ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - required) / .18f)) : 0f;
            FlowStrength = charging ? Safe(flowIntensity, 1f, 0f, 3f) * Mathf.Pow(progress, 1.15f)
                * Mathf.Lerp(1f, Safe(heldFlowMultiplier, .44f, .15f, 1f), holdFade) : 0f;
            IsBurstVisible = burstActive && Safe(burstIntensity, 1f, 0f, 3f) > .0001f;
            if (FlowStrength <= .0001f && !IsBurstVisible)
            {
                HideMesh();
                burstJustStarted = false;
                return;
            }

            EnsureRuntimeObjects();
            ResolveView();
            worldToLocal = runtimeObject.transform.worldToLocalMatrix;
            vertices.Clear(); colors.Clear(); uv0.Clear(); uv1.Clear(); indices.Clear();
            if (FlowStrength > .0001f) BuildFlow(progress, elapsed);
            if (IsBurstVisible) BuildBurst(burstDuration);
            mesh.Clear(false);
            if (vertices.Count > 0)
            {
                mesh.SetVertices(vertices);
                mesh.SetColors(colors);
                mesh.SetUVs(0, uv0);
                mesh.SetUVs(1, uv1);
                mesh.SetTriangles(indices, 0, false);
                mesh.RecalculateBounds();
            }
            properties.SetFloat(FlowTimeId, charging ? elapsed : burstFlowTime);
            meshRenderer.SetPropertyBlock(properties);
            meshRenderer.sharedMaterial = windMaterial;
            meshRenderer.enabled = vertices.Count > 0;
            burstJustStarted = false;
        }

        bool ValidReferences()
        {
            if (actor == null || !actor.isActiveAndEnabled || actor.Animator == null
                || !actor.Animator.isActiveAndEnabled || bladeBase == null || bladeTip == null
                || windMaterial == null || !bladeBase.gameObject.activeInHierarchy
                || !bladeTip.gameObject.activeInHierarchy) return false;
            Vector3 scale = transform.lossyScale;
            return Finite(bladeBase.position) && Finite(bladeTip.position) && Finite(scale)
                && Mathf.Abs(scale.x * scale.y * scale.z) > .00000001f
                && (bladeTip.position - bladeBase.position).sqrMagnitude > .000001f;
        }

        void StartCompletionBurst()
        {
            completionObserved = true;
            if (!ValidReferences()) return;
            Vector3 direction = bladeTip.position - bladeBase.position;
            burstAxis = direction.normalized;
            BladeBasis(burstAxis, out burstRight, out burstUp);
            burstOrigin = bladeBase.position + direction * .72f;
            burstFlowTime = actor.IsCharging ? actor.ChargeElapsedSeconds
                : actor.Profile != null ? Mathf.Max(.1f, actor.Profile.chargeSeconds) : 1.5f;
            burstAge = 0f;
            burstActive = burstJustStarted = true;
            CompletionBurstCount++;
        }

        void EnsureRuntimeObjects()
        {
            if (runtimeObject != null && mesh != null && meshRenderer != null) return;
            DestroyRuntimeObjects();
            runtimeObject = new GameObject("Samurai charge wind (runtime)");
            runtimeObject.hideFlags = HideFlags.DontSave;
            runtimeObject.layer = gameObject.layer;
            runtimeObject.transform.SetParent(transform, false);
            mesh = new Mesh { name = "Samurai charge wind mesh", hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            runtimeObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = runtimeObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = windMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            meshRenderer.enabled = false;
            if (properties == null) properties = new MaterialPropertyBlock();
        }

        void ResolveView()
        {
            if (cachedCamera == null || !cachedCamera.isActiveAndEnabled)
            {
                var playerInput = actor.GetComponent<SamuraiPlayerInput>();
                cachedCamera = playerInput != null && playerInput.cameraTransform != null
                    ? playerInput.cameraTransform.GetComponent<Camera>() : null;
                if (cachedCamera == null) cachedCamera = Camera.main;
            }
            if (cachedCamera != null)
            {
                Transform cameraTransform = cachedCamera.transform;
                viewPosition = cameraTransform.position;
                viewRight = cameraTransform.right;
                viewUp = cameraTransform.up;
                viewForward = cameraTransform.forward;
            }
            else
            {
                viewPosition = actor.transform.position - actor.transform.forward * 5f + Vector3.up * 2f;
                viewRight = actor.transform.right;
                viewUp = Vector3.up;
                viewForward = actor.transform.forward;
            }
        }

        void BladeBasis(Vector3 axis, out Vector3 right, out Vector3 up)
        {
            right = Vector3.ProjectOnPlane(bladeBase.right, axis);
            if (right.sqrMagnitude < .0001f) right = Vector3.ProjectOnPlane(bladeBase.up, axis);
            if (right.sqrMagnitude < .0001f)
                right = Vector3.Cross(axis, Mathf.Abs(axis.y) < .9f ? Vector3.up : Vector3.right);
            right.Normalize();
            up = Vector3.Cross(axis, right).normalized;
        }

        void BuildFlow(float progress, float elapsed)
        {
            Vector3 root = bladeBase.position;
            Vector3 blade = bladeTip.position - root;
            float length = blade.magnitude;
            Vector3 axis = blade / length;
            BladeBasis(axis, out Vector3 right, out Vector3 up);
            float radius = Mathf.Lerp(Safe(initialRadius, .10f, .03f, .4f),
                Safe(fullRadius, .24f, .05f, .5f), Mathf.SmoothStep(0f, 1f, progress));
            float span = length * Mathf.Lerp(1.10f, 1.45f, progress);
            Vector3 start = root + axis * (length * .57f - span * .5f);
            for (int i = 0; i < FilamentCount + BandCount; i++)
            {
                bool band = i >= FilamentCount;
                float seed = (i + 1) * 1.719f;
                float portion = band ? .72f + Hash(i + 81f) * .20f : .40f + Hash(i + 17f) * .36f;
                float travel = elapsed * (band ? .43f + Hash(i + 2f) * .22f : .70f + Hash(i + 2f) * .26f);
                float head = Mathf.Repeat(travel + Hash(i + 37f) * (1f + portion), 1f + portion);
                float from = Mathf.Max(0f, head - portion), to = Mathf.Min(1f, head);
                if (to - from < .003f) continue;
                int segments = band ? BandSegments : FilamentSegments;
                int first = vertices.Count;
                // 不同的低圈数、平滑曲率与转速，让长弧像沿刀抽动的风，不形成等距线圈。
                float turns = band ? .48f + Hash(i + 11f) * .38f : .62f + Hash(i + 9f) * .50f;
                float rotationSpeed = band ? .42f + Hash(i + 23f) * .33f : .64f + Hash(i + 23f) * .46f;
                float angleOffset = seed * 2.43f - elapsed * rotationSpeed * Mathf.Lerp(.8f, 1.15f, progress);
                float shapePhase = seed * .83f + elapsed * (.45f + Hash(i + 29f) * .40f);
                float radialScale = band ? .66f + Hash(i + 55f) * .28f : .65f + Hash(i + 56f) * .35f;
                float width = band ? .09f + Hash(i + 61f) * .07f : .012f + Hash(i + 43f) * .011f;
                width *= Mathf.Lerp(.72f, 1f, progress);
                float alpha = FlowStrength * (band ? .45f + Hash(i + 69f) * .15f : .80f + Hash(i + 67f) * .18f);
                for (int row = 0; row <= segments; row++)
                {
                    float u = row / (float)segments;
                    float along = Mathf.Lerp(from, to, u);
                    Vector3 point = FlowPoint(start, axis, right, up, span, radius * radialScale,
                        along, angleOffset, turns, shapePhase);
                    Vector3 tangent = FlowPoint(start, axis, right, up, span, radius * radialScale,
                        Mathf.Min(1f, along + .004f), angleOffset, turns, shapePhase)
                        - FlowPoint(start, axis, right, up, span, radius * radialScale,
                            Mathf.Max(0f, along - .004f), angleOffset, turns, shapePhase);
                    Vector3 side = RibbonSide(point, tangent, right);
                    float taper = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(u * Mathf.PI)), .65f);
                    float widthWave = .86f + .14f * Mathf.Sin(along * 7.4f + shapePhase);
                    float pointWidth = width * Mathf.Lerp(.22f, 1f, taper) * widthWave;
                    Color color = Tint(band ? 1.20f : 1.45f, Mathf.Clamp01(alpha * taper));
                    AddVertex(point - side * pointWidth * .5f, new Vector2(u, 0f), 0f, seed, color);
                    AddVertex(point + side * pointWidth * .5f, new Vector2(u, 1f), 0f, seed, color);
                    if (row > 0) AddStripSection(first + row * 2 - 2);
                }
            }
        }

        static Vector3 FlowPoint(Vector3 start, Vector3 axis, Vector3 right, Vector3 up,
            float span, float radius, float along, float offset, float turns, float shapePhase)
        {
            float curvedAlong = along + .075f * Mathf.Sin(along * Mathf.PI)
                * Mathf.Sin(shapePhase + along * 2.6f);
            float angle = offset + curvedAlong * turns * Tau
                + .30f * Mathf.Sin(along * 4.4f + shapePhase);
            float envelope = .28f + .72f * Mathf.Sin(Mathf.Clamp01(along) * Mathf.PI);
            float swelling = .84f + .16f * Mathf.Sin(along * 6.4f + shapePhase)
                + .06f * Mathf.Sin(along * 11f - shapePhase * .6f);
            return start + axis * (span * along)
                + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * (radius * envelope * swelling);
        }

        void BuildBurst(float duration)
        {
            float t = Mathf.Clamp01(burstAge / duration);
            float intensity = Safe(burstIntensity, 1f, 0f, 3f);
            // 前约两帧（60 fps）保持亮峰，再快速消退；不延长整次爆发的寿命。
            float peakHold = Mathf.Min(2f / 60f, duration * .15f);
            float fade = Mathf.Clamp01((burstAge - peakHold) / Mathf.Max(.001f, duration - peakHold));
            float envelope = Mathf.Pow(1f - fade, 2.4f);
            float expansion = 1f - Mathf.Pow(1f - t, 2f);
            float radius = Safe(burstRadius, .46f, .1f, .8f);
            for (int i = 0; i < BurstRayCount; i++)
            {
                float seed = i * 2.317f + 5f;
                float angle = (i + Hash(i + 107f) * .45f) * Tau / BurstRayCount;
                Vector3 direction = (burstRight * Mathf.Cos(angle) + burstUp * Mathf.Sin(angle)
                    + burstAxis * ((Hash(i + 83f) - .5f) * .9f)).normalized;
                float inner = .025f + radius * expansion * (.45f + Hash(i + 101f) * .35f);
                float rayLength = radius * (.28f + Hash(i + 91f) * .34f) * (1f - .45f * t);
                Vector3 a = burstOrigin + direction * inner;
                Vector3 b = a + direction * rayLength;
                Vector3 side = RibbonSide((a + b) * .5f, direction, burstRight);
                float width = (.023f + Hash(i + 113f) * .012f) * (1f - t * .55f);
                int first = vertices.Count;
                Color color = Tint(2.40f, Mathf.Clamp01(intensity * envelope * .88f));
                AddVertex(a - side * width * .5f, new Vector2(0f, 0f), 1f, seed, color);
                AddVertex(a + side * width * .5f, new Vector2(0f, 1f), 1f, seed, color);
                AddVertex(b - side * width * .16f, new Vector2(1f, 0f), 1f, seed, color);
                AddVertex(b + side * width * .16f, new Vector2(1f, 1f), 1f, seed, color);
                AddStripSection(first);
            }
            int ringFirst = vertices.Count;
            float ringRadius = .09f + radius * expansion;
            float ringWidth = .036f * (1f - t * .5f);
            Color ringColor = Tint(1.85f, Mathf.Clamp01(intensity * envelope * .60f));
            for (int row = 0; row <= RingSegments; row++)
            {
                float u = row / (float)RingSegments;
                float angle = u * Tau;
                Vector3 radial = burstRight * Mathf.Cos(angle) + burstUp * Mathf.Sin(angle);
                float localRadius = ringRadius * (1f + .055f * Mathf.Sin(angle * 3f + .4f));
                float localWidth = ringWidth * (.82f + .18f * Mathf.Sin(angle * 2f));
                AddVertex(burstOrigin + radial * (localRadius - localWidth * .5f), new Vector2(u, 0f), 1f, 41f, ringColor);
                AddVertex(burstOrigin + radial * (localRadius + localWidth * .5f), new Vector2(u, 1f), 1f, 41f, ringColor);
                if (row > 0) AddStripSection(ringFirst + row * 2 - 2);
            }
            // 环外叠少量有面积的软风弧，仍锁定满蓄瞬间的刀基向量。
            for (int arc = 0; arc < BurstWindArcCount; arc++)
            {
                int first = vertices.Count;
                float seed = 73f + arc * 3.14f;
                float startAngle = arc * Tau / BurstWindArcCount + .18f * expansion;
                float arcAngle = Tau * (.19f + Hash(arc + 127f) * .11f);
                float arcRadius = ringRadius * (.90f + arc * .08f);
                float width = (.075f + Hash(arc + 131f) * .035f) * (1f - .45f * t);
                for (int row = 0; row <= BurstWindArcSegments; row++)
                {
                    float u = row / (float)BurstWindArcSegments;
                    float angle = startAngle + u * arcAngle;
                    Vector3 radial = burstRight * Mathf.Cos(angle) + burstUp * Mathf.Sin(angle);
                    float localRadius = arcRadius * (1f + .07f * Mathf.Sin(angle * 2f + seed));
                    Vector3 arcCenter = burstOrigin + burstAxis * (.025f * Mathf.Sin(angle * 1.6f + seed));
                    float taper = Mathf.Sin(u * Mathf.PI);
                    float localWidth = width * Mathf.Lerp(.2f, 1f, taper);
                    Color color = Tint(1.6f, Mathf.Clamp01(intensity * envelope * .55f * taper));
                    AddVertex(arcCenter + radial * (localRadius - localWidth * .5f), new Vector2(u, 0f), 0f, seed, color);
                    AddVertex(arcCenter + radial * (localRadius + localWidth * .5f), new Vector2(u, 1f), 0f, seed, color);
                    if (row > 0) AddStripSection(first + row * 2 - 2);
                }
            }
            float flashFade = Mathf.Clamp01((burstAge - peakHold) / Mathf.Max(.001f, duration * .43f));
            float flashEnvelope = (1f - flashFade) * (1f - flashFade);
            if (flashEnvelope <= .0001f) return;
            float size = Safe(flashRadius, .38f, .1f, .7f) * Mathf.Lerp(1.02f, 1.40f, expansion);
            Vector3 center = burstOrigin - viewForward * .012f;
            int flashFirst = vertices.Count;
            Color flashColor = Tint(3.3f, Mathf.Clamp01(intensity * flashEnvelope * .90f));
            AddVertex(center - viewRight * size - viewUp * size, new Vector2(0f, 0f), 2f, 59f, flashColor);
            AddVertex(center - viewRight * size + viewUp * size, new Vector2(0f, 1f), 2f, 59f, flashColor);
            AddVertex(center + viewRight * size - viewUp * size, new Vector2(1f, 0f), 2f, 59f, flashColor);
            AddVertex(center + viewRight * size + viewUp * size, new Vector2(1f, 1f), 2f, 59f, flashColor);
            AddStripSection(flashFirst);
        }

        Vector3 RibbonSide(Vector3 point, Vector3 tangent, Vector3 fallback)
        {
            Vector3 side = Vector3.Cross(viewPosition - point, tangent);
            return side.sqrMagnitude > .00000001f ? side.normalized : fallback;
        }

        void AddVertex(Vector3 worldPosition, Vector2 textureUv, float style, float seed, Color color)
        {
            vertices.Add(worldToLocal.MultiplyPoint3x4(worldPosition));
            colors.Add(color);
            uv0.Add(textureUv);
            uv1.Add(new Vector2(style, seed));
        }

        void AddStripSection(int first)
        {
            indices.Add(first); indices.Add(first + 2); indices.Add(first + 3);
            indices.Add(first); indices.Add(first + 3); indices.Add(first + 1);
        }

        Color Tint(float brightness, float alpha)
        {
            return new Color(Safe(windColor.r, .8f, 0f, 8f) * brightness,
                Safe(windColor.g, .94f, 0f, 8f) * brightness,
                Safe(windColor.b, 1f, 0f, 8f) * brightness,
                alpha * Safe(windColor.a, 1f, 0f, 1f));
        }

        void HideMesh()
        {
            if (meshRenderer != null) meshRenderer.enabled = false;
            if (mesh != null) mesh.Clear(false);
        }

        /// <summary>清显示和本轮锁存；累计爆发计数保持，供跨轮/禁用诊断使用。</summary>
        public void ClearVisual()
        {
            FlowStrength = 0f;
            IsBurstVisible = false;
            chargeArmed = completionObserved = hasChargeSample = false;
            burstActive = burstJustStarted = false;
            previousChargeElapsed = burstAge = burstFlowTime = 0f;
            HideMesh();
            vertices.Clear(); colors.Clear(); uv0.Clear(); uv1.Clear(); indices.Clear();
        }

        void DestroyRuntimeObjects()
        {
            if (runtimeObject != null)
            {
                runtimeObject.SetActive(false);
                DestroyOwned(runtimeObject);
            }
            if (mesh != null) DestroyOwned(mesh);
            runtimeObject = null;
            mesh = null;
            meshRenderer = null;
        }

        static float Hash(float x) => Mathf.Repeat(Mathf.Sin(x * 12.9898f + 4.1414f) * 43758.5453f, 1f);
        static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        static float Safe(float value, float fallback, float minimum, float maximum)
            => Mathf.Clamp(Finite(value) ? value : fallback, minimum, maximum);
        static void DestroyOwned(Object value)
        {
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
