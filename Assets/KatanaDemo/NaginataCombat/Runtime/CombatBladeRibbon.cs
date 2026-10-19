using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OCDesign.Combat
{
    /// <summary>
    /// 在握持辅助完成后采样实际刀刃，按 Animator 进度生成世界空间残迹。
    /// 仅负责显示，不修改骨骼、武器、动画、移动或伤害窗口。
    /// </summary>
    [DefaultExecutionOrder(200), DisallowMultipleComponent]
    public sealed class CombatBladeRibbon : MonoBehaviour
    {
        [System.Serializable]
        public sealed class AttackProfile
        {
            public CombatAttackDefinition attack;
            public Vector2[] emissionWindows = new Vector2[0];
            [Min(0.001f)] public float edgeFadeNormalized = 0.025f;

            public AttackProfile(CombatAttackDefinition definition, Vector2[] windows, float edgeFade)
            { attack = definition; emissionWindows = windows; edgeFadeNormalized = edgeFade; }
        }

        [SerializeField] private CombatAnimationDriver driver;
        [SerializeField] private CombatAttackDefinition attack;
        [SerializeField] private Transform bladeSpace;
        [SerializeField] private Vector3 bladeRootLocal = new Vector3(0f, 1.60f, 0f);
        [SerializeField] private Vector3 bladeTipLocal = new Vector3(0.11438665f, 2.10f, 0f);
        [SerializeField] private Material ribbonMaterial;
        [SerializeField, Tooltip("横轴为本招实际动画进度，仅在快速挥砍段发射。")]
        private Vector2 emissionWindow = new Vector2(0.2666667f, 0.437037f);
        [SerializeField, Range(0.04f, 0.3f)] private float lifetime = 0.13f;
        [SerializeField, Range(0.2f, 1f), Tooltip("从刀尖向刀根覆盖的比例。")]
        private float bladeCoverage = 1f;
        [SerializeField, Tooltip("其余招式独立的挥砍窗口；同一招的多次挥击不会连成一条残迹。")]
        private AttackProfile[] additionalAttacks = new AttackProfile[0];

        private const int MaxSamples = 72;
        private const int AcrossSegments = 4;
        private const int CurveSubdivisions = 4;
        private const int MaxRows = MaxSamples * CurveSubdivisions;
        private struct Sample { public Vector3 root, tip; public float time, strength; }
        private readonly List<Sample> samples = new List<Sample>(MaxSamples);
        private readonly List<Vector3> vertices = new List<Vector3>(MaxRows * (AcrossSegments + 1));
        private readonly List<Vector2> uv = new List<Vector2>(MaxRows * (AcrossSegments + 1));
        private readonly List<Color> colors = new List<Color>(MaxRows * (AcrossSegments + 1));
        private readonly List<int> indices = new List<int>(MaxRows * AcrossSegments * 6);
        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private GameObject ribbonObject;
        private float clock, previousClock, previousProgress;
        private Vector3 previousRoot, previousTip, previousActorPosition;
        private uint previousSerial;
        private bool hasPrevious;
        private CombatAttackDefinition sampledAttack;
        private int sampledWindow = -1;

        public int VisibleTriangleCount => mesh ? mesh.vertexCount / (AcrossSegments + 1) > 1
            ? (mesh.vertexCount / (AcrossSegments + 1) - 1) * AcrossSegments * 2 : 0 : 0;
        public bool IsVisible => meshRenderer && meshRenderer.enabled;
        public int SampleCount => samples.Count;
        public Vector3 BladeRootWorld => bladeSpace ? bladeSpace.TransformPoint(bladeRootLocal) : Vector3.zero;
        public Vector3 BladeTipWorld => bladeSpace ? bladeSpace.TransformPoint(bladeTipLocal) : Vector3.zero;
        public Vector2 EmissionWindow => emissionWindow;
        public float Lifetime => lifetime;

        private AttackProfile FindProfile(CombatAttackDefinition definition)
        {
            if (!definition || additionalAttacks == null) return null;
            foreach (var profile in additionalAttacks)
                if (profile != null && profile.attack == definition) return profile;
            return null;
        }

        public int GetEmissionWindowCount(CombatAttackDefinition definition)
        {
            if (!definition) return 0;
            if (definition == attack) return 1;
            var profile = FindProfile(definition);
            return profile == null || profile.emissionWindows == null ? 0 : profile.emissionWindows.Length;
        }

        public Vector2 GetEmissionWindow(CombatAttackDefinition definition, int index)
        {
            if (definition == attack && index == 0) return emissionWindow;
            var profile = FindProfile(definition);
            return profile.emissionWindows[index];
        }

        public void ConfigureAdditionalAttacks(AttackProfile[] profiles)
        { ClearTrail(); additionalAttacks = profiles ?? new AttackProfile[0]; }

        public void Configure(CombatAnimationDriver animationDriver, CombatAttackDefinition definition,
            Transform space, Vector3 root, Vector3 tip, Material material)
        {
            ClearTrail();
            driver = animationDriver; attack = definition; bladeSpace = space;
            bladeRootLocal = root; bladeTipLocal = tip; ribbonMaterial = material;
            if (meshRenderer) meshRenderer.sharedMaterial = material;
        }

        private void LateUpdate()
        {
            var definition = driver ? driver.CurrentAttack : null;
            bool active = driver && driver.isActiveAndEnabled && driver.Target
                && driver.Target.isActiveAndEnabled && GetEmissionWindowCount(definition) > 0;
            Advance(active ? definition : null, driver ? driver.CurrentAttackNormalizedTime : 0f,
                driver ? driver.AttackSerial : 0u, Time.deltaTime);
        }

        private void EnsureMesh()
        {
            if (mesh) return;
            ribbonObject = new GameObject("Naginata Blade Wind (runtime)");
            ribbonObject.hideFlags = HideFlags.DontSave;
            ribbonObject.layer = gameObject.layer;
            ribbonObject.transform.SetParent(transform, false);
            mesh = new Mesh { name = "Naginata world-space blade ribbon", hideFlags = HideFlags.DontSave };
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

        private void Advance(CombatAttackDefinition definition, float progress, uint serial, float deltaTime)
        {
            if (!definition || !bladeSpace || !ribbonMaterial || !bladeSpace.gameObject.activeInHierarchy)
            { ClearTrail(); return; }
            EnsureMesh();
            clock += Mathf.Max(0f, deltaTime);
            Vector3 tip = BladeTipWorld;
            Vector3 root = Vector3.Lerp(tip, BladeRootWorld, bladeCoverage);
            Vector3 actorPosition = driver ? driver.transform.position : transform.position;
            bool discontinuity = !hasPrevious || sampledAttack != definition || serial != previousSerial || progress < previousProgress - 0.001f
                // 长柄重击在30fps时刀尖合法位移可超过2m；只把角色容器的跳跃位移视作瞬移。
                || Vector3.Distance(actorPosition, previousActorPosition) > 2f || deltaTime > 0.15f;
            if (discontinuity)
            {
                samples.Clear();
                sampledWindow = -1;
                hasPrevious = true;
                previousRoot = root; previousTip = tip; previousClock = clock; previousProgress = progress;
            }

            // 对跨过窗口边界的帧插值，避免低帧率下漏掉开启/关闭；不把收刀连回刀光。
            int windowCount = GetEmissionWindowCount(definition);
            var profile = definition == attack ? null : FindProfile(definition);
            float fade = profile == null ? 0.025f : Mathf.Max(0.001f, profile.edgeFadeNormalized);
            for (int windowIndex = 0; windowIndex < windowCount; windowIndex++)
            {
                Vector2 window = GetEmissionWindow(definition, windowIndex);
                if (progress <= previousProgress + 0.000001f
                    || progress < window.x || previousProgress > window.y) continue;
                float begin = Mathf.Max(previousProgress, window.x);
                float end = Mathf.Min(progress, window.y);
                if (end >= begin)
                {
                    // 双挥第二段独立起笔，绝不跨过蓄势空档连接网格。
                    if (sampledWindow != windowIndex) { samples.Clear(); sampledWindow = windowIndex; }
                    float span = progress - previousProgress;
                    float from = Mathf.Clamp01((begin - previousProgress) / span);
                    float to = Mathf.Clamp01((end - previousProgress) / span);
                    if (samples.Count == 0) AddSample(from, root, tip, progress, window, fade);
                    // 倍速叠加掉帧若一次跨完整窗口，补中点，避免首尾亮度都为0而整次漏光。
                    if (previousProgress < window.x && progress > window.y)
                        AddSample((from + to) * 0.5f, root, tip, progress, window, fade);
                    // 存真实帧采样；表面细分在 BuildMesh 完成，避免插值点挤掉仍在寿命内的旧帧。
                    AddSample(to, root, tip, progress, window, fade);
                }
            }
            while (samples.Count > 0 && clock - samples[0].time >= lifetime) samples.RemoveAt(0);
            BuildMesh();
            previousRoot = root; previousTip = tip; previousClock = clock;
            previousProgress = progress; previousSerial = serial; sampledAttack = definition;
            previousActorPosition = actorPosition;
        }

        private void AddSample(float fraction, Vector3 root, Vector3 tip, float progress, Vector2 window, float fade)
        {
            Vector3 basePoint = Vector3.Lerp(previousRoot, root, fraction);
            Vector3 direction = Vector3.Slerp(previousTip - previousRoot, tip - root, fraction);
            float p = Mathf.Lerp(previousProgress, progress, fraction);
            fade = Mathf.Min(fade, (window.y - window.x) * 0.25f);
            float envelope = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(window.x, window.x + fade, p))
                * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(window.y - fade, window.y, p)));
            if (samples.Count == MaxSamples) samples.RemoveAt(0);
            samples.Add(new Sample { root = basePoint, tip = basePoint + direction,
                time = Mathf.Lerp(previousClock, clock, fraction), strength = envelope });
        }

        private void BuildMesh()
        {
            mesh.Clear();
            if (samples.Count < 2) { meshRenderer.enabled = false; return; }
            vertices.Clear(); uv.Clear(); colors.Clear(); indices.Clear();
            Matrix4x4 worldToLocal = ribbonObject.transform.worldToLocalMatrix;
            int rows = (samples.Count - 1) * CurveSubdivisions + 1;
            for (int row = 0; row < rows; row++)
            {
                int segment = Mathf.Min(row / CurveSubdivisions, samples.Count - 2);
                float t = (row - segment * CurveSubdivisions) / (float)CurveSubdivisions;
                Sample a = samples[Mathf.Max(0, segment - 1)], b = samples[segment];
                Sample c = samples[segment + 1], d = samples[Mathf.Min(samples.Count - 1, segment + 2)];
                // 仅平滑残迹表面，最新一行仍严格落在真实刀刃端点上。
                Sample sample = new Sample { root = Curve(a.root, b.root, c.root, d.root, t),
                    tip = Curve(a.tip, b.tip, c.tip, d.tip, t), time = Mathf.Lerp(b.time, c.time, t),
                    strength = Mathf.Lerp(b.strength, c.strength, t) };
                float age = Mathf.Clamp01((clock - sample.time) / lifetime);
                float alpha = Mathf.Pow(1f - age, 1.15f) * sample.strength;
                for (int j = 0; j <= AcrossSegments; j++)
                {
                    float across = j / (float)AcrossSegments;
                    vertices.Add(worldToLocal.MultiplyPoint3x4(Vector3.Lerp(sample.root, sample.tip, across)));
                    uv.Add(new Vector2(age, across));
                    colors.Add(new Color(1f, 1f, 1f, alpha));
                    if (row == 0 || j == AcrossSegments) continue;
                    int k = row * (AcrossSegments + 1) + j;
                    int before = k - AcrossSegments - 1;
                    indices.Add(before); indices.Add(k); indices.Add(k + 1);
                    indices.Add(before); indices.Add(k + 1); indices.Add(before + 1);
                }
            }
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0); mesh.RecalculateBounds();
            meshRenderer.enabled = true;
        }

        private static Vector3 Curve(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
            => 0.5f * ((2f * b) + (-a + c) * t + (2f * a - 5f * b + 4f * c - d) * t * t
                + (-a + 3f * b - 3f * c + d) * t * t * t);

        public void ClearTrail()
        {
            samples.Clear(); hasPrevious = false; clock = 0f; previousClock = 0f;
            sampledAttack = null; sampledWindow = -1;
            if (mesh) mesh.Clear();
            if (meshRenderer) meshRenderer.enabled = false;
        }

#if UNITY_EDITOR
        // 离线逐帧预览共用真实网格路径，不替代 Play 模式验证。
        public void PreviewSample(float normalizedTime, float deltaSeconds, uint serial = 1u)
            => Advance(attack, normalizedTime, serial, deltaSeconds);
        public void PreviewSample(CombatAttackDefinition definition, float normalizedTime, float deltaSeconds, uint serial = 1u)
            => Advance(GetEmissionWindowCount(definition) > 0 ? definition : null, normalizedTime, serial, deltaSeconds);
#endif

        private void OnDisable() => ClearTrail();
        private void OnDestroy()
        {
            if (ribbonObject) { if (Application.isPlaying) Destroy(ribbonObject); else DestroyImmediate(ribbonObject); }
            if (mesh) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }
        }
    }
}
