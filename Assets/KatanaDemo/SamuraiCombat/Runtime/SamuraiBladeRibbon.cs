using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OCDesign.Samurai
{
    /// <summary>
    /// 在骨骼和武器求值后采样真实刀刃，以世界空间残迹显示挥砍。
    /// 只读取动作状态与端点，不修改命中、骨骼、武器或战斗时序。
    /// </summary>
    [DefaultExecutionOrder(300), DisallowMultipleComponent]
    public sealed class SamuraiBladeRibbon : MonoBehaviour
    {
        [System.Serializable]
        public sealed class AttackProfile
        {
            public SamuraiActionDefinition attack;
            public Vector2 emissionWindow = new Vector2(.20f, .45f);
            [Range(.04f, .3f)] public float lifetime = .10f;
            [Range(0f, 2f)] public float strength = 1f;
            [Range(.2f, 1f)] public float bladeCoverage = 1f;
        }

        [SerializeField] SamuraiCombatant actor;
        [SerializeField] Transform bladeBase;
        [SerializeField] Transform bladeTip;
        [SerializeField] Material ribbonMaterial;
        [SerializeField] AttackProfile[] profiles = new AttackProfile[0];

        const int MaxSamples = 72;
        const int AcrossSegments = 4;
        const int CurveSubdivisions = 4;
        const int MaxRows = (MaxSamples - 1) * CurveSubdivisions + 1;
        const float EdgeFadeSeconds = .025f;

        struct Sample
        {
            public Vector3 root, tip;
            public float time, envelope;
        }

        readonly List<Sample> samples = new List<Sample>(MaxSamples);
        readonly List<Vector3> vertices = new List<Vector3>(MaxRows * (AcrossSegments + 1));
        readonly List<Vector2> uv = new List<Vector2>(MaxRows * (AcrossSegments + 1));
        readonly List<Color> colors = new List<Color>(MaxRows * (AcrossSegments + 1));
        readonly List<int> indices = new List<int>((MaxRows - 1) * AcrossSegments * 6);
        SamuraiCombatant subscribedActor;
        SamuraiActionDefinition sampledAttack;
        Mesh mesh;
        MeshRenderer meshRenderer;
        GameObject ribbonObject;
        Vector3 previousRoot, previousTip, previousActorPosition;
        float clock, previousClock, previousProgress, previousAnimatorProgress;
        int previousSerial;
        bool hasPrevious;

        public bool IsVisible => meshRenderer != null && meshRenderer.enabled && meshRenderer.gameObject.activeInHierarchy;
        public int SampleCount => samples.Count;
        public int VisibleTriangleCount => mesh != null && mesh.vertexCount > AcrossSegments + 1
            ? (mesh.vertexCount / (AcrossSegments + 1) - 1) * AcrossSegments * 2 : 0;

        public void Configure(SamuraiCombatant targetActor, Transform targetBase, Transform targetTip,
            Material material, AttackProfile[] attackProfiles)
        {
            Unsubscribe();
            ClearTrail();
            actor = targetActor;
            bladeBase = targetBase;
            bladeTip = targetTip;
            ribbonMaterial = material;
            profiles = attackProfiles ?? new AttackProfile[0];
            if (meshRenderer != null) meshRenderer.sharedMaterial = material;
            if (isActiveAndEnabled) Subscribe();
        }

        void OnEnable() { ClearTrail(); Subscribe(); }
        void OnDisable() { Unsubscribe(); ClearTrail(); }
        void LateUpdate() => SampleNow(Time.deltaTime);

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

        void OnStateChanged(string state) => ClearTrail();

        AttackProfile FindProfile(SamuraiActionDefinition definition)
        {
            if (definition == null || profiles == null) return null;
            foreach (AttackProfile entry in profiles)
                if (entry != null && entry.attack == definition) return entry;
            return null;
        }

        /// <summary>与LateUpdate相同的真实动作采样；手动检查时在Animator求值后调用。</summary>
        public void SampleNow(float deltaSeconds)
        {
            if (!isActiveAndEnabled) { ClearTrail(); return; }
            // Inspector更换角色引用时也保持事件订阅成对，旧角色不再影响当前刀光。
            if (subscribedActor != actor) { ClearTrail(); Subscribe(); }
            if (actor == null || !actor.isActiveAndEnabled || !actor.IsAttacking
                || actor.Actions == null || actor.Animator == null || !actor.Animator.isActiveAndEnabled
                || actor.Animator.runtimeAnimatorController == null
                || bladeBase == null || bladeTip == null || ribbonMaterial == null
                || !bladeBase.gameObject.activeInHierarchy || !bladeTip.gameObject.activeInHierarchy
                || !Finite(deltaSeconds) || deltaSeconds < 0f)
            { ClearTrail(); return; }

            SamuraiActionDefinition definition = actor.Actions.ActiveAction;
            AttackProfile profile = FindProfile(definition);
            if (profile == null || !Finite(profile.emissionWindow.x) || !Finite(profile.emissionWindow.y)
                || !Finite(profile.lifetime) || !Finite(profile.strength) || !Finite(profile.bladeCoverage))
            { ClearTrail(); return; }
            Vector2 window = new Vector2(Mathf.Clamp01(profile.emissionWindow.x),
                Mathf.Clamp01(profile.emissionWindow.y));
            if (window.y <= window.x || profile.strength <= 0f)
            { ClearTrail(); return; }

            float progress = actor.Actions.StateNormalizedTime;
            // 动作公开进度会保持单调；额外读取Animator原始进度，捕捉手动倒播/跳回。
            if (!Finite(progress) || actor.Animation == null
                || !actor.Animation.TryProgress(definition.stateName, out float animatorProgress)
                || !Finite(animatorProgress))
            { ClearTrail(); return; }
            Advance(definition, profile, window, progress, animatorProgress,
                actor.Actions.AttackSerial, deltaSeconds);
        }

        void EnsureMesh()
        {
            if (mesh != null) return;
            ribbonObject = new GameObject("Samurai Blade Wind (runtime)");
            ribbonObject.hideFlags = HideFlags.DontSave;
            ribbonObject.layer = gameObject.layer;
            ribbonObject.transform.SetParent(transform, false);
            mesh = new Mesh { name = "Samurai world-space blade ribbon", hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            ribbonObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = ribbonObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = ribbonMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.enabled = false;
        }

        void Advance(SamuraiActionDefinition definition, AttackProfile profile, Vector2 window,
            float progress, float animatorProgress, int serial, float deltaSeconds)
        {
            EnsureMesh();
            clock += deltaSeconds;
            Vector3 tip = bladeTip.position;
            Vector3 root = Vector3.Lerp(tip, bladeBase.position, Mathf.Clamp(profile.bladeCoverage, .2f, 1f));
            Vector3 actorPosition = actor.transform.position;
            bool discontinuity = !hasPrevious || sampledAttack != definition || serial != previousSerial
                || progress < previousProgress - .001f || animatorProgress < previousAnimatorProgress - .001f
                || Vector3.Distance(actorPosition, previousActorPosition) > 2f || deltaSeconds > .15f;
            if (discontinuity)
            {
                ClearTrail();
                // 从当前真实刀位重新起笔，绝不把旧位置接到复位/换招后的端点。
                hasPrevious = true;
                previousRoot = root;
                previousTip = tip;
                previousClock = clock;
                previousProgress = progress;
            }

            float lifetime = Mathf.Clamp(profile.lifetime, .04f, .3f);
            float fade = Mathf.Min(EdgeFadeSeconds / definition.Duration, (window.y - window.x) * .25f);
            if (progress > previousProgress + .000001f && progress >= window.x && previousProgress <= window.y)
            {
                float begin = Mathf.Max(previousProgress, window.x);
                float end = Mathf.Min(progress, window.y);
                if (end >= begin)
                {
                    float span = progress - previousProgress;
                    float from = Mathf.Clamp01((begin - previousProgress) / span);
                    float to = Mathf.Clamp01((end - previousProgress) / span);
                    if (samples.Count == 0) AddSample(from, root, tip, progress, window, fade);
                    // 掉帧跨过完整窗口时补中点，避免首尾渐隐同时为零导致整次漏光。
                    if (previousProgress < window.x && progress > window.y)
                        AddSample((from + to) * .5f, root, tip, progress, window, fade);
                    AddSample(to, root, tip, progress, window, fade);
                }
            }
            while (samples.Count > 0 && clock - samples[0].time >= lifetime) samples.RemoveAt(0);
            BuildMesh(lifetime, Mathf.Clamp(profile.strength, 0f, 2f));
            previousRoot = root;
            previousTip = tip;
            previousClock = clock;
            previousProgress = progress;
            previousAnimatorProgress = animatorProgress;
            previousSerial = serial;
            sampledAttack = definition;
            previousActorPosition = actorPosition;
        }

        void AddSample(float fraction, Vector3 root, Vector3 tip, float progress, Vector2 window, float fade)
        {
            Vector3 basePoint = Vector3.Lerp(previousRoot, root, fraction);
            Vector3 direction = Vector3.Slerp(previousTip - previousRoot, tip - root, fraction);
            float p = Mathf.Lerp(previousProgress, progress, fraction);
            float envelope = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(window.x, window.x + fade, p))
                * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(window.y - fade, window.y, p)));
            if (samples.Count == MaxSamples) samples.RemoveAt(0);
            samples.Add(new Sample { root = basePoint, tip = basePoint + direction,
                time = Mathf.Lerp(previousClock, clock, fraction), envelope = envelope });
        }

        void BuildMesh(float lifetime, float strength)
        {
            mesh.Clear();
            if (samples.Count < 2) { meshRenderer.enabled = false; return; }
            vertices.Clear(); uv.Clear(); colors.Clear(); indices.Clear();
            Matrix4x4 worldToLocal = ribbonObject.transform.worldToLocalMatrix;
            int rows = (samples.Count - 1) * CurveSubdivisions + 1;
            // 低强度只减覆盖；强度超过1只提高HDR颜色，保持透明度有界。
            float brightness = Mathf.Max(1f, strength);
            for (int row = 0; row < rows; row++)
            {
                int segment = Mathf.Min(row / CurveSubdivisions, samples.Count - 2);
                float t = (row - segment * CurveSubdivisions) / (float)CurveSubdivisions;
                Sample a = samples[Mathf.Max(0, segment - 1)], b = samples[segment];
                Sample c = samples[segment + 1], d = samples[Mathf.Min(samples.Count - 1, segment + 2)];
                Vector3 root = Curve(a.root, b.root, c.root, d.root, t);
                Vector3 tip = Curve(a.tip, b.tip, c.tip, d.tip, t);
                float age = Mathf.Clamp01((clock - Mathf.Lerp(b.time, c.time, t)) / lifetime);
                float alpha = Mathf.Pow(1f - age, 1.15f)
                    * Mathf.Lerp(b.envelope, c.envelope, t) * Mathf.Clamp01(strength);
                for (int j = 0; j <= AcrossSegments; j++)
                {
                    float across = j / (float)AcrossSegments;
                    vertices.Add(worldToLocal.MultiplyPoint3x4(Vector3.Lerp(root, tip, across)));
                    // 与共用BladeWindRibbon Shader契约一致：UV.x为年龄，UV.y为刀根到刀尖。
                    uv.Add(new Vector2(age, across));
                    colors.Add(new Color(brightness, brightness, brightness, Mathf.Clamp01(alpha)));
                    if (row == 0 || j == AcrossSegments) continue;
                    int k = row * (AcrossSegments + 1) + j;
                    int before = k - AcrossSegments - 1;
                    indices.Add(before); indices.Add(k); indices.Add(k + 1);
                    indices.Add(before); indices.Add(k + 1); indices.Add(before + 1);
                }
            }
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0, false); mesh.RecalculateBounds();
            meshRenderer.enabled = true;
        }

        static Vector3 Curve(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
            => .5f * ((2f * b) + (-a + c) * t + (2f * a - 5f * b + 4f * c - d) * t * t
                + (-a + 3f * b - 3f * c + d) * t * t * t);
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public void ClearTrail()
        {
            samples.Clear();
            hasPrevious = false;
            sampledAttack = null;
            clock = previousClock = previousProgress = previousAnimatorProgress = 0f;
            if (mesh != null && mesh.vertexCount > 0) mesh.Clear();
            if (meshRenderer != null) meshRenderer.enabled = false;
        }

        void OnDestroy()
        {
            Unsubscribe();
            if (ribbonObject != null)
            {
                if (Application.isPlaying) Destroy(ribbonObject);
                else DestroyImmediate(ribbonObject);
            }
            if (mesh != null)
            {
                if (Application.isPlaying) Destroy(mesh);
                else DestroyImmediate(mesh);
            }
        }
    }
}
