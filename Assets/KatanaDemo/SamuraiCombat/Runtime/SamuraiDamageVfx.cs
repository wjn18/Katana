using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OCDesign.Samurai
{
    /// <summary>订阅实际扣血：血滴、短红雾与冲击碎光。世界坐标保留命中点，不参与伤害或物理。</summary>
    [DefaultExecutionOrder(350), DisallowMultipleComponent]
    public sealed class SamuraiDamageVfx : MonoBehaviour
    {
        [SerializeField] SamuraiCombatant actor;
        [Tooltip("序列化引用保证构建保留专用受击 Shader。")]
        [SerializeField] Shader burstShader;
        [SerializeField] bool enableVisuals = true;
        [Header("伤害分档（实际扣血）")]
        [SerializeField, Min(0f)] float mediumDamage = 15f;
        [SerializeField, Min(0f)] float highDamage = 35f;
        [Header("外观")]
        [SerializeField] Color darkBlood = new Color(.30f, .012f, .025f, .95f);
        [SerializeField] Color brightBlood = new Color(.86f, .035f, .045f, .95f);
        [SerializeField] Color impactColor = new Color(1.9f, .74f, .32f, .95f);
        [SerializeField, Range(.3f, 1.2f)] float lifetime = .58f;
        [SerializeField, Range(.3f, 2f)] float size = 1f;
        [SerializeField, Range(0f, 10f)] float gravity = 3.2f;
        [SerializeField, Range(1, 6)] int maxConcurrentBursts = 6;

        struct Burst
        {
            public Vector3 point, direction;
            public float age, scale, duration;
            public int tier, seed;
            public bool active;
        }
        readonly Burst[] bursts = new Burst[6];
        readonly List<Vector3> vertices = new List<Vector3>(1024);
        readonly List<Color> colors = new List<Color>(1024);
        readonly List<Vector2> uv0 = new List<Vector2>(1024);
        readonly List<Vector2> uv1 = new List<Vector2>(1024);
        readonly List<int> indices = new List<int>(1536);
        SamuraiCombatant subscribedActor;
        GameObject runtimeObject;
        Mesh mesh;
        MeshRenderer meshRenderer;
        Material runtimeMaterial;
        Camera view;
        Vector3 viewRight, viewUp, viewForward;
        int nextBurst;

        public int BurstCount { get; private set; }
        public int ActiveBurstCount { get; private set; }
        public Vector3 LastHitPoint { get; private set; }
        public float LastHealthDamage { get; private set; }
        public int LastDamageTier { get; private set; }
        public int RuntimeVertexCount => mesh != null ? mesh.vertexCount : 0;
        public bool IsVisible => meshRenderer != null && meshRenderer.enabled && RuntimeVertexCount > 0;

        public void Configure(SamuraiCombatant targetActor, Shader shader = null)
        {
            Unsubscribe(); ClearFeedback(); DestroyRuntimeObjects();
            actor = targetActor;
            if (shader != null) burstShader = shader;
            view = null;
            if (Application.isPlaying && isActiveAndEnabled) Subscribe();
        }
        void Awake() { if (actor == null) actor = GetComponent<SamuraiCombatant>(); }
        void OnEnable()
        {
            ClearFeedback();
            if (!Application.isPlaying) return;
            if (actor == null) actor = GetComponent<SamuraiCombatant>();
            Subscribe();
        }
        void OnDisable() { Unsubscribe(); ClearFeedback(); DestroyRuntimeObjects(); }
        void OnDestroy() { Unsubscribe(); DestroyRuntimeObjects(); }
        void LateUpdate() => UpdateVisual(Time.deltaTime);
        void Subscribe()
        {
            if (subscribedActor == actor) return;
            Unsubscribe();
            subscribedActor = actor;
            if (subscribedActor == null) return;
            subscribedActor.DamageReceived += OnDamageReceived;
            subscribedActor.CombatReset += ClearFeedback;
        }
        void Unsubscribe()
        {
            if (subscribedActor != null)
            {
                subscribedActor.DamageReceived -= OnDamageReceived;
                subscribedActor.CombatReset -= ClearFeedback;
            }
            subscribedActor = null;
        }
        void OnDamageReceived(SamuraiDamageFeedback feedback)
        {
            if (!Application.isPlaying || !isActiveAndEnabled || !enableVisuals || actor == null
                || !actor.isActiveAndEnabled || !Finite(feedback.HealthDamage) || feedback.HealthDamage <= 0f) return;
            Vector3 point = feedback.Hit.Point;
            if (!Finite(point)) point = actor.transform.position + Vector3.up;
            Vector3 direction = actor.transform.position - feedback.Hit.SourcePosition;
            direction.y = 0f;
            if (!Finite(direction) || direction.sqrMagnitude < .001f) direction = -actor.transform.forward;
            direction.Normalize();
            int tier = feedback.HealthDamage >= Mathf.Max(mediumDamage, highDamage) ? 2
                : feedback.HealthDamage >= mediumDamage ? 1 : 0;
            LastHitPoint = point;
            LastHealthDamage = feedback.HealthDamage;
            LastDamageTier = tier;
            BurstCount++;
            int limit = Mathf.Clamp(maxConcurrentBursts, 1, bursts.Length);
            nextBurst %= limit;
            bursts[nextBurst] = new Burst
            {
                point = point, direction = direction, age = 0f, tier = tier, seed = BurstCount,
                scale = Mathf.Clamp(size, .3f, 2f) * (.78f + .20f * tier),
                duration = Mathf.Clamp(lifetime, .3f, 1.2f) * (.9f + .1f * tier), active = true
            };
            nextBurst = (nextBurst + 1) % limit;
            // 接触这一帧即显示，双方局部 HitStop 不延迟出生或冻结粒子；全局暂停仍冻结。
            UpdateVisual(0f);
        }

        /// <summary>正常 LateUpdate 的公开入口。使用缩放时间，暂停不推进；不依赖角色战斗 Clock。</summary>
        public void UpdateVisual(float deltaTime)
        {
            if (!Application.isPlaying || !isActiveAndEnabled || !enableVisuals || actor == null
                || !actor.isActiveAndEnabled) { ClearFeedback(); return; }
            if (subscribedActor != actor) { ClearFeedback(); Subscribe(); }
            float dt = Time.timeScale > 0f && Finite(deltaTime) ? Mathf.Max(0f, deltaTime) : 0f;
            ActiveBurstCount = 0;
            int limit = Mathf.Clamp(maxConcurrentBursts, 1, bursts.Length);
            for (int i = 0; i < bursts.Length; i++)
            {
                if (!bursts[i].active) continue;
                bursts[i].age += dt;
                if (i >= limit || bursts[i].age >= bursts[i].duration) bursts[i].active = false;
                else ActiveBurstCount++;
            }
            if (ActiveBurstCount == 0) { HideMesh(); return; }
            EnsureRuntimeObjects();
            if (mesh == null) return;
            ResolveView();
            vertices.Clear(); colors.Clear(); uv0.Clear(); uv1.Clear(); indices.Clear();
            for (int i = 0; i < bursts.Length; i++) if (bursts[i].active) BuildBurst(bursts[i]);
            mesh.Clear(false);
            mesh.SetVertices(vertices); mesh.SetColors(colors);
            mesh.SetUVs(0, uv0); mesh.SetUVs(1, uv1); mesh.SetTriangles(indices, 0, false);
            mesh.RecalculateBounds();
            meshRenderer.enabled = vertices.Count > 0;
        }

        void BuildBurst(Burst burst)
        {
            float t = burst.age / burst.duration, s = burst.scale;
            Vector3 side = Vector3.Cross(Vector3.up, burst.direction);
            Vector3 center = burst.point - viewForward * .035f;
            // 透明红雾先于血滴提交，保持血滴轮廓清楚；整个效果有正常场景深度遮挡。
            int clouds = 3 + burst.tier;
            for (int i = 0; i < clouds; i++)
            {
                float n = Noise(burst.seed * 41 + i * 19);
                Vector3 direction = burst.direction * (.25f + n * .2f)
                    + side * (n - .5f) * .65f + Vector3.up * (.12f + .2f * n);
                Vector3 point = center + direction * (burst.age * s + .05f);
                float radius = s * (.10f + n * .07f + t * .20f);
                Color color = Color.Lerp(darkBlood, brightBlood, n * .6f);
                color.a *= .23f * Mathf.Pow(1f - t, 1.7f);
                AddQuad(point, viewRight * radius, viewUp * radius, color, 1f, n);
            }
            int drops = 10 + burst.tier * 4;
            for (int i = 0; i < drops; i++)
            {
                float n = Noise(burst.seed * 73 + i * 13), r = Noise(burst.seed * 17 + i * 71);
                Vector3 velocity = (burst.direction * (.75f + n) + side * ((r - .5f) * 2.1f)
                    + Vector3.up * (.20f + Noise(i * 31 + burst.seed) * 1.45f)) * s;
                Vector3 point = center + velocity * (burst.age + .025f)
                    - Vector3.up * (.5f * gravity * burst.age * burst.age);
                Vector3 projected = Vector3.ProjectOnPlane(velocity - Vector3.up * gravity * burst.age, viewForward);
                Vector3 along = projected.sqrMagnitude > .001f ? projected.normalized : viewUp;
                Vector3 across = Vector3.Cross(viewForward, along).normalized;
                Color color = Color.Lerp(darkBlood, brightBlood, n);
                color.a *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.48f, 1f, t));
                float radius = s * (.012f + n * .018f);
                AddQuad(point, along * radius * (1.8f + (1f - t) * 1.7f), across * radius, color, 0f, n);
            }
            float flash = Mathf.Clamp01(1f - burst.age / .16f);
            if (flash <= 0f) return;
            Color core = impactColor; core.a *= flash * .85f;
            float coreSize = s * (.17f + burst.age * .35f);
            AddQuad(center, viewRight * coreSize, viewUp * coreSize, core, 3f, 0f);
            int sparks = 5 + burst.tier * 2;
            for (int i = 0; i < sparks; i++)
            {
                float n = Noise(i * 41 + burst.seed * 29);
                float angle = (i + n * .4f) * Mathf.PI * 2f / sparks;
                Vector3 radial = viewRight * Mathf.Cos(angle) + viewUp * Mathf.Sin(angle);
                Vector3 across = Vector3.Cross(viewForward, radial).normalized;
                Vector3 point = center + radial * s * (.08f + burst.age * (1.3f + n));
                Color color = Color.Lerp(brightBlood, impactColor, .55f + n * .45f);
                color.a *= flash;
                AddQuad(point, radial * s * (.06f + n * .045f) * flash,
                    across * s * .009f, color, 2f, n);
            }
        }
        void AddQuad(Vector3 center, Vector3 along, Vector3 across, Color color, float style, float seed)
        {
            int start = vertices.Count;
            AddVertex(center - along - across, new Vector2(0, 0), color, style, seed);
            AddVertex(center - along + across, new Vector2(0, 1), color, style, seed);
            AddVertex(center + along - across, new Vector2(1, 0), color, style, seed);
            AddVertex(center + along + across, new Vector2(1, 1), color, style, seed);
            indices.Add(start); indices.Add(start + 2); indices.Add(start + 3);
            indices.Add(start); indices.Add(start + 3); indices.Add(start + 1);
        }
        void AddVertex(Vector3 position, Vector2 uv, Color color, float style, float seed)
        {
            vertices.Add(position); colors.Add(color); uv0.Add(uv); uv1.Add(new Vector2(style, seed));
        }
        void ResolveView()
        {
            if (view == null || !view.isActiveAndEnabled)
            {
                var input = actor.GetComponent<SamuraiPlayerInput>();
                view = input != null && input.cameraTransform != null ? input.cameraTransform.GetComponent<Camera>() : null;
                if (view == null) view = Camera.main;
            }
            viewRight = view != null ? view.transform.right : actor.transform.right;
            viewUp = view != null ? view.transform.up : Vector3.up;
            viewForward = view != null ? view.transform.forward : actor.transform.forward;
        }
        void EnsureRuntimeObjects()
        {
            if (mesh != null) return;
            Shader shader = burstShader != null ? burstShader : Shader.Find("KatanaDemo/VFX/SamuraiDamageBurst");
            if (shader == null) return;
            // 独立单位变换：已喷出的血滴不会随受击者的击退、转向或死亡姿态移动。
            runtimeObject = new GameObject("Samurai damage burst (runtime)") { hideFlags = HideFlags.DontSave };
            runtimeObject.layer = gameObject.layer;
            runtimeMaterial = new Material(shader) { name = "Samurai damage burst material", hideFlags = HideFlags.DontSave };
            mesh = new Mesh { name = "Samurai damage burst mesh", hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            runtimeObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = runtimeObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = runtimeMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            meshRenderer.enabled = false;
        }
        public void ClearFeedback()
        {
            for (int i = 0; i < bursts.Length; i++) bursts[i].active = false;
            nextBurst = ActiveBurstCount = 0;
            HideMesh();
        }
        void HideMesh()
        {
            if (meshRenderer != null) meshRenderer.enabled = false;
            if (mesh != null && mesh.vertexCount > 0) mesh.Clear(false);
        }
        void DestroyRuntimeObjects()
        {
            if (runtimeObject != null) { runtimeObject.SetActive(false); DestroyOwned(runtimeObject); }
            if (mesh != null) DestroyOwned(mesh);
            if (runtimeMaterial != null) DestroyOwned(runtimeMaterial);
            runtimeObject = null; mesh = null; meshRenderer = null; runtimeMaterial = null;
        }
        static float Noise(int value)
        {
            uint v = unchecked((uint)value * 747796405u + 2891336453u);
            v = ((v >> (int)((v >> 28) + 4)) ^ v) * 277803737u;
            return ((v >> 22) ^ v) / (float)uint.MaxValue;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        static void DestroyOwned(Object value)
        {
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
