using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OCDesign.Samurai
{
    /// <summary>格挡碰撞和弹反回流；世界空间轨迹由角色 Clock 推进。</summary>
    [DefaultExecutionOrder(340), DisallowMultipleComponent]
    public sealed class SamuraiDeflectFeedback : MonoBehaviour
    {
        [SerializeField] SamuraiCombatant actor;
        [Tooltip("序列化引用保证构建保留独立金属碰撞 Shader。")]
        [SerializeField] Shader sparkShader;
        [SerializeField] bool enableVisuals = true;
        [SerializeField] bool enableSound = true;
        [Tooltip("保留旧碰撞亮芯时间；飞溅与回收有独立寿命。")]
        [SerializeField, Range(.1f, .3f)] float sparkSeconds = .18f;
        [SerializeField, Range(.6f, 1f)] float recoverySeconds = .76f;
        [SerializeField, Range(.3f, 2f)] float visualScale = 1.4f;
        [SerializeField, Range(.3f, 3f)] float intensity = 1.9f;
        [Tooltip("仅把碰撞爆闪、火星和回流起点抬高（世界米）；物理接触点与身体吸收点保持。")]
        [SerializeField, Range(-.25f, .75f)] float heightOffset = .20f;
        [SerializeField, Range(0f, .08f)] float hitStopSeconds = .04f;
        [SerializeField, Range(0f, .5f)] float soundVolume = .24f;
        [SerializeField] Color sparkColor = new Color(1f, .73f, .22f, 1f);

        struct Burst
        {
            public Vector3 point, origin, right, up, forward;
            public float started;
            public int seed;
            public bool active, perfect, recovery;
        }
        // 连击保留前一击的尾迹；只有八击同时存活时才复用最老的一格。
        readonly Burst[] bursts = new Burst[8];
        readonly List<Vector3> vertices = new List<Vector3>(4096);
        readonly List<Color> colors = new List<Color>(4096);
        readonly List<Vector2> uv0 = new List<Vector2>(4096);
        readonly List<Vector2> uv1 = new List<Vector2>(4096);
        readonly List<int> indices = new List<int>(6144);
        SamuraiCombatant subscribedActor;
        GameObject runtimeObject;
        Mesh mesh;
        MeshRenderer meshRenderer;
        Material runtimeMaterial;
        AudioSource audioSource;
        AudioClip metalSound, blockSound;
        Camera view;
        Transform chest;
        Matrix4x4 worldToLocal;
        Vector3 viewRight, viewUp, viewForward;
        int nextBurst, sequence;
        float lastClock;
        bool hasClock, soundPaused;

        public int SuccessCount { get; private set; }
        public int BlockCount { get; private set; }
        public int ActiveBurstCount { get; private set; }
        public int ActiveRecoveryCount { get; private set; }
        public float LastRecovered { get; private set; }
        public Vector3 LastHitPoint { get; private set; }
        public Vector3 LastClashPoint { get; private set; }
        /// <summary>表现起点快照，包含世界向上偏移及三厘米镜头偏移，不用于伤害判定。</summary>
        public Vector3 LastEffectOrigin { get; private set; }
        public bool LastWasPerfect { get; private set; }
        public bool IsVisible => meshRenderer != null && meshRenderer.enabled && RuntimeVertexCount > 0;
        public int RuntimeVertexCount => mesh != null ? mesh.vertexCount : 0;

        public void Configure(SamuraiCombatant targetActor, Shader shader = null)
        {
            Unsubscribe(); ClearFeedback(); DestroyRuntimeObjects();
            actor = targetActor;
            if (shader != null) sparkShader = shader;
            view = null; chest = null;
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
        void OnApplicationFocus(bool focused) { if (!focused) ClearFeedback(); }
        void OnApplicationPause(bool paused) { if (paused) ClearFeedback(); }
        void LateUpdate() => UpdateVisual();
        void Subscribe()
        {
            if (subscribedActor == actor) return;
            Unsubscribe(); subscribedActor = actor;
            if (subscribedActor == null) return;
            subscribedActor.GuardContacted += OnGuardContacted;
            subscribedActor.CombatReset += ClearFeedback;
        }
        void Unsubscribe()
        {
            if (subscribedActor != null)
            {
                subscribedActor.GuardContacted -= OnGuardContacted;
                subscribedActor.CombatReset -= ClearFeedback;
            }
            subscribedActor = null;
        }

        void OnGuardContacted(SamuraiGuardContact contact)
        {
            if (!Application.isPlaying || !isActiveAndEnabled || actor == null || actor.Actions == null
                || !actor.isActiveAndEnabled || actor.Profile == null) return;
            bool perfect = contact.Result == SamuraiHitResult.PerfectGuard;
            if (!perfect && contact.Result != SamuraiHitResult.Blocked) return;
            float clock = actor.Actions.Clock;
            if (!Finite(clock)) return;
            if (hasClock && clock < lastClock) ClearFeedback();
            Vector3 point = contact.HasClashPoint ? contact.ClashPoint : contact.Point;
            if (!Finite(point)) point = actor.transform.position + Vector3.up * 1.1f + actor.transform.forward * .4f;
            LastHitPoint = contact.Point;
            LastClashPoint = point;
            LastRecovered = Mathf.Max(0f, contact.RestoredStamina);
            LastWasPerfect = perfect;
            if (perfect) SuccessCount++; else BlockCount++;
            lastClock = clock; hasClock = true;
            ResolveView();
            LastEffectOrigin = point + Vector3.up * Mathf.Clamp(heightOffset, -.25f, .75f) - viewForward * .035f;
            bool recovery = perfect && actor.Profile.deflectEnabled;
            bursts[nextBurst] = new Burst
            {
                point = point, origin = LastEffectOrigin, right = viewRight, up = viewUp, forward = viewForward,
                started = clock, seed = ++sequence, active = true, perfect = perfect, recovery = recovery
            };
            nextBurst = (nextBurst + 1) % bursts.Length;
            // 保留原新式弹反顿帧；普通格挡与旧式完美格挡不在这里新增顿帧。
            if (recovery)
            {
                actor.ApplyHitStop(hitStopSeconds);
                var attacker = contact.Source as SamuraiCombatant;
                if (attacker != null && attacker != actor) attacker.ApplyHitStop(hitStopSeconds);
            }
            PlayImpactSound(perfect);
            // 命中当帧立刻呈现亮芯，而不是等顿帧结束。
            UpdateVisual();
        }

        public void UpdateVisual()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || actor == null || !actor.isActiveAndEnabled
                || actor.Actions == null || actor.IsDead || actor.Profile == null)
            { ClearFeedback(); return; }
            if (subscribedActor != actor) { ClearFeedback(); chest = null; Subscribe(); }
            float clock = actor.Actions.Clock;
            if (!Finite(clock)) { ClearFeedback(); return; }
            if (hasClock && clock + .0001f < lastClock) ClearFeedback();
            lastClock = clock; hasClock = true;
            UpdateAudio();
            ActiveBurstCount = ActiveRecoveryCount = 0;
            for (int i = 0; i < bursts.Length; i++)
            {
                float age = clock - bursts[i].started;
                float duration = bursts[i].recovery ? Mathf.Clamp(recoverySeconds, .6f, 1f) : bursts[i].perfect ? .46f : .32f;
                if (bursts[i].active && (age < 0f || age >= duration)) bursts[i].active = false;
                if (!bursts[i].active) continue;
                ActiveBurstCount++;
                if (bursts[i].recovery) ActiveRecoveryCount++;
            }
            if (!enableVisuals || intensity <= 0f || ActiveBurstCount == 0) { HideMesh(); return; }
            EnsureRuntimeObjects();
            if (mesh == null || meshRenderer == null) return;
            ResolveView();
            worldToLocal = runtimeObject.transform.worldToLocalMatrix;
            runtimeMaterial.SetFloat("_Intensity", Mathf.Clamp(intensity, .3f, 3f));
            runtimeMaterial.SetFloat("_FlowTime", clock);
            vertices.Clear(); colors.Clear(); uv0.Clear(); uv1.Clear(); indices.Clear();
            Vector3 target = RecoveryCenter();
            for (int i = 0; i < bursts.Length; i++)
                if (bursts[i].active) BuildBurst(bursts[i], clock - bursts[i].started, target);
            mesh.Clear(false);
            mesh.SetVertices(vertices); mesh.SetColors(colors);
            mesh.SetUVs(0, uv0); mesh.SetUVs(1, uv1); mesh.SetTriangles(indices, 0, false);
            mesh.RecalculateBounds();
            meshRenderer.enabled = vertices.Count > 0;
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
        Vector3 RecoveryCenter()
        {
            if (chest == null && actor.Animator != null && actor.Animator.isHuman)
            {
                chest = actor.Animator.GetBoneTransform(HumanBodyBones.Chest);
                if (chest == null) chest = actor.Animator.GetBoneTransform(HumanBodyBones.Spine);
            }
            Vector3 center = chest != null ? chest.position : actor.transform.position + Vector3.up * 1.03f;
            // 实际胸腹的可见表面，避免光点埋在身体中心。目标随当前骨骼移动，爆裂起点留在世界空间。
            return center - Vector3.up * .07f - viewForward * .16f;
        }

        void BuildBurst(Burst burst, float age, Vector3 target)
        {
            float scale = Mathf.Clamp(visualScale, .3f, 2f);
            // 偏移只在命中时快照，镜头和角色后续移动不会拖着爆裂起点走。
            // 所有爆闪、火星及回流共享同一起点，胸腹吸收点仍随真实骨骼移动。
            Vector3 center = burst.origin;
            float flashDuration = Mathf.Clamp(sparkSeconds, .1f, .3f) * (burst.perfect ? .78f : .5f);
            if (age < flashDuration)
            {
                float t = age / flashDuration;
                float fade = Mathf.Pow(1f - t, 1.7f);
                float size = (burst.perfect ? .66f : .48f) * scale * (1f + .28f * t);
                AddQuad(center, viewRight * size, viewUp * size,
                    new Color(1f, .37f, .035f, fade * (burst.perfect ? 1.05f : .62f)), 2f, burst.seed);
                float core = (burst.perfect ? .19f : .105f) * scale * (1f - .4f * t);
                AddQuad(center, viewRight * core, viewUp * core, new Color(1f, .90f, .58f, fade * 1.5f), 3f, burst.seed);
                float glare = (burst.perfect ? .77f : .5f) * scale * (1f - .35f * t);
                AddQuad(center, viewRight * glare, viewUp * .025f * scale,
                    new Color(1f, .70f, .18f, fade * (burst.perfect ? 1.1f : .65f)), 4f, burst.seed);
                if (burst.perfect)
                    AddQuad(center, (viewRight * .35f + viewUp * .94f) * .26f * scale,
                        (viewRight * -.94f + viewUp * .35f) * .016f * scale,
                        new Color(1f, .88f, .51f, fade * .58f), 4f, burst.seed + 1);
            }
            // 撞击的首帧已有不等长放射火星。不能等发射延迟过去才出现爆裂。
            float rayDuration = burst.perfect ? .075f : .052f;
            if (age < rayDuration)
            {
                float rayFade = Mathf.Pow(1f - age / rayDuration, 1.3f);
                int rayCount = burst.perfect ? 17 : 8;
                for (int i = 0; i < rayCount; i++)
                {
                    float seed = burst.seed * 17.31f + i * 5.17f;
                    float angle = i * 2.399963f + Hash(seed) * .62f;
                    Vector3 ray = (burst.right * Mathf.Cos(angle) + burst.up * Mathf.Sin(angle)
                        + burst.forward * ((Hash(seed + 1f) - .5f) * .32f)).normalized;
                    Vector3 side = Vector3.Cross(viewForward, ray).normalized;
                    if (side.sqrMagnitude < .01f) side = viewUp;
                    float length = (burst.perfect ? Mathf.Lerp(.35f, .76f, Hash(seed + 2f)) : Mathf.Lerp(.27f, .50f, Hash(seed + 2f))) * scale;
                    float width = Mathf.Lerp(.008f, .021f, Hash(seed + 3f)) * scale;
                    Vector3 tail = center + ray * .025f * scale;
                    Vector3 head = center + ray * length * (1f + age * 3f);
                    AddQuad((head + tail) * .5f, (head - tail) * .5f, side * width,
                        new Color(1f, .64f, .13f, rayFade * .9f), 1f, seed);
                }
            }
            int count = burst.perfect ? 46 : 22;
            for (int i = 0; i < count; i++)
            {
                float seed = burst.seed * 31.17f + i * 7.91f;
                float life = burst.perfect ? Mathf.Lerp(.23f, .45f, Hash(seed + 1f)) : Mathf.Lerp(.16f, .31f, Hash(seed + 1f));
                float a = age - (i % 3 == 0 ? -Mathf.Lerp(.012f, .03f, Hash(seed + 7f)) : Hash(seed + 7f) * .014f);
                if (a < 0f || a >= life) continue;
                float angle = i * 2.399963f + burst.seed * .63f;
                Vector3 direction = (burst.right * Mathf.Cos(angle) + burst.up * Mathf.Sin(angle) * .88f
                    + burst.forward * ((Hash(seed + 2f) - .5f) * .8f)).normalized;
                float radius = (burst.perfect ? Mathf.Lerp(.38f, .91f, Hash(seed + 3f)) : Mathf.Lerp(.23f, .52f, Hash(seed + 3f))) * scale;
                Vector3 head = SparkPosition(center, direction, a, radius);
                Vector3 tail = SparkPosition(center, direction, Mathf.Max(0f, a - Mathf.Lerp(.018f, .043f, Hash(seed + 8f))), radius);
                Vector3 along = (head - tail) * .5f;
                if (along.sqrMagnitude < .000006f) along = direction * .003f * scale;
                Vector3 side = Vector3.Cross(viewForward, along).normalized;
                if (side.sqrMagnitude < .01f) side = viewUp;
                float width = Mathf.Lerp(.004f, .012f, Mathf.Pow(Hash(seed + 4f), 2f)) * scale;
                float fade = Mathf.Pow(1f - a / life, .7f);
                Color tint = Color.Lerp(new Color(1f, .34f, .025f, 1f), new Color(1f, .82f, .27f, 1f), fade);
                tint.a = fade * Mathf.Lerp(.6f, 1.1f, Hash(seed + 5f));
                AddQuad((head + tail) * .5f, along, side * width, tint, 1f, seed);
                if (i % 3 == 0)
                    AddQuad(head, viewRight * width * 2f, viewUp * width * 2f,
                        new Color(1f, .66f, .12f, fade * .7f), 2f, seed);
            }
            if (burst.recovery) BuildRecovery(burst, center, target, age, scale);
        }
        static Vector3 SparkPosition(Vector3 origin, Vector3 direction, float age, float radius)
        {
            return origin + direction * (radius * (1f - Mathf.Exp(-age * 15f))) - Vector3.up * (age * age * .95f);
        }

        void BuildRecovery(Burst burst, Vector3 center, Vector3 target, float age, float scale)
        {
            float duration = Mathf.Clamp(recoverySeconds, .6f, 1f);
            float t = age / duration;
            if (t < 0f || t >= 1f) return;
            float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.86f, 1f, t));
            const int strands = 12, segments = 16;
            for (int strand = 0; strand < strands; strand++)
            {
                float seed = burst.seed * 13.13f + strand * 4.71f;
                float stagger = Hash(seed + 2f) * .027f;
                float headTime = Mathf.Clamp01((t - stagger) / (1f - stagger));
                if (headTime <= .001f) continue;
                // 足够长的运动历史让慢速段仍然保留弯曲火丝，不退化为光点。
                float trailTime = Mathf.Lerp(.28f, .42f, Hash(seed + 6f));
                float baseWidth = Mathf.Lerp(.033f, .066f, Hash(seed + 4f)) * scale;
                int first = vertices.Count;
                for (int j = 0; j <= segments; j++)
                {
                    float u = j / (float)segments;
                    Vector3 point = RecoveryTrailPoint(burst, center, target, headTime, trailTime, u, strand, scale);
                    Vector3 before = RecoveryTrailPoint(burst, center, target, headTime, trailTime, Mathf.Max(0f, u - .008f), strand, scale);
                    Vector3 after = RecoveryTrailPoint(burst, center, target, headTime, trailTime, Mathf.Min(1f, u + .008f), strand, scale);
                    Vector3 side = Vector3.Cross(viewForward, after - before).normalized;
                    if (side.sqrMagnitude < .01f) side = viewRight;
                    float taper = Mathf.Pow(Mathf.Clamp01(Mathf.Sin(u * Mathf.PI)), .65f);
                    float width = baseWidth * (.35f + taper * .9f) * (.85f + .15f * Mathf.Sin(u * 12f + seed + t * 11f));
                    Color tint = Color.Lerp(new Color(1f, .17f, .008f, 1f), new Color(1f, .63f, .075f, 1f), .3f + u * .7f);
                    tint = Color.Lerp(tint, sparkColor, .035f);
                    tint.a = fade * Mathf.Lerp(.34f, .94f, u) * Mathf.Min(1f, headTime * 30f);
                    AddVertex(point - side * width, new Vector2(u, 0f), tint, 0f, seed);
                    AddVertex(point + side * width, new Vector2(u, 1f), tint, 0f, seed);
                    if (j == 0) continue;
                    int k = first + j * 2;
                    indices.Add(k - 2); indices.Add(k); indices.Add(k + 1);
                    indices.Add(k - 2); indices.Add(k + 1); indices.Add(k - 1);
                }
                // 只有部分火丝分叉；不使用圆形亮头，避免慢流阶段看成一圈发光豆点。
                if (strand % 3 == 0 && headTime > .13f)
                    BuildRecoveryFork(burst, center, target, headTime, trailTime, strand, scale, fade, baseWidth);
            }
            // 胸前只留很小的暖色余辉，不把整个角色照白。
            float arrival = Mathf.InverseLerp(.84f, .93f, t) * (1f - Mathf.InverseLerp(.94f, 1f, t));
            if (arrival > 0f)
                AddQuad(target, viewRight * .105f * scale, viewUp * .13f * scale,
                    new Color(1f, .65f, .16f, arrival * .35f), 2f, burst.seed);
        }
        static Vector3 RecoveryTrailPoint(Burst burst, Vector3 center, Vector3 target,
            float headTime, float trailTime, float u, int strand, float scale)
        {
            float pathTime = Mathf.Lerp(Mathf.Max(0f, headTime - trailTime), headTime, u);
            Vector3 point = RecoveryPath(burst, center, target, pathTime, strand, scale);
            float seed = burst.seed * 13.13f + strand * 4.71f;
            float angle = strand * Mathf.PI * 2f / 12f + seed * .13f;
            Vector3 curl = -burst.right * Mathf.Sin(angle) + burst.up * Mathf.Cos(angle);
            Vector3 radial = burst.right * Mathf.Cos(angle) + burst.up * Mathf.Sin(angle);
            float drift = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.08f, .28f, headTime))
                * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.82f, .98f, headTime)));
            float tail = 1f - u;
            // 悬停时保留约 20–35cm 的独立弯曲弧尾，不让历史采样因速度变慢而坍缩。
            Vector3 shape = curl * (tail * .24f + Mathf.Sin(tail * Mathf.PI) * .10f)
                + radial * (Mathf.Sin(tail * Mathf.PI * 2.5f + seed) * .055f * tail)
                + burst.up * (Mathf.Sin(tail * Mathf.PI) * .055f);
            return point + shape * drift * scale;
        }
        void BuildRecoveryFork(Burst burst, Vector3 center, Vector3 target,
            float headTime, float trailTime, int strand, float scale, float fade, float width)
        {
            float seed = burst.seed * 13.13f + strand * 4.71f;
            Vector3 branchSide = (burst.right * Mathf.Cos(seed) + burst.up * Mathf.Sin(seed)).normalized;
            int first = vertices.Count;
            const int segments = 7;
            for (int j = 0; j <= segments; j++)
            {
                float u = j / (float)segments;
                float mainU = Mathf.Lerp(.34f, .82f, u);
                Vector3 point = RecoveryTrailPoint(burst, center, target, headTime, trailTime, mainU, strand, scale);
                point += branchSide * (Mathf.Pow(1f - u, 1.3f) * (.13f + .09f * Hash(seed)) * scale);
                float w = width * .42f * Mathf.Sin(u * Mathf.PI);
                Color tint = new Color(1f, .42f, .035f, fade * .58f);
                AddVertex(point - viewUp * w, new Vector2(u, 0f), tint, 0f, seed + 8f);
                AddVertex(point + viewUp * w, new Vector2(u, 1f), tint, 0f, seed + 8f);
                if (j == 0) continue;
                int k = first + j * 2;
                indices.Add(k - 2); indices.Add(k); indices.Add(k + 1);
                indices.Add(k - 2); indices.Add(k + 1); indices.Add(k - 1);
            }
        }
        static Vector3 RecoveryPath(Burst burst, Vector3 center, Vector3 target, float t, int strand, float scale)
        {
            float seed = burst.seed * 13.13f + strand * 4.71f;
            float angle = strand * Mathf.PI * 2f / 12f + (Hash(seed) - .5f) * .38f + burst.seed * .27f;
            Vector3 radial = burst.right * Mathf.Cos(angle) + burst.up * Mathf.Sin(angle) * .85f
                + burst.forward * ((Hash(seed + 1f) - .5f) * .28f);
            Vector3 curl = -burst.right * Mathf.Sin(angle) + burst.up * Mathf.Cos(angle);
            float radius = Mathf.Lerp(.35f, .74f, Hash(seed + 3f)) * scale;
            Vector3 crest = center + radial * radius + Vector3.up * .025f * scale;
            const float explodeEnd = .16f, hoverEnd = .49f, arrive = .94f;
            if (t <= explodeEnd)
            {
                float u = t / explodeEnd;
                float fastOut = 1f - Mathf.Pow(1f - u, 3f);
                return Vector3.LerpUnclamped(center, crest, fastOut)
                    + curl * (Mathf.Sin(u * Mathf.PI) * .045f * scale);
            }
            if (t <= hoverEnd)
            {
                float u = (t - explodeEnd) / (hoverEnd - explodeEnd);
                return crest + curl * (Mathf.Sin(u * Mathf.PI * .75f) * .095f * scale)
                    + radial * (.03f * u * scale) + burst.up * (Mathf.Sin(u * Mathf.PI) * .025f * scale);
            }
            Vector3 p0 = crest + curl * (.095f * Mathf.Sin(Mathf.PI * .75f) * scale) + radial * (.03f * scale);
            Vector3 p1 = p0 + curl * (.26f * scale) + burst.up * (.04f * scale);
            Vector3 p2 = target - radial * (.12f * scale) + curl * (.055f * scale);
            float progress = Mathf.Pow(Mathf.Clamp01((t - hoverEnd) / (arrive - hoverEnd)), 2.25f);
            float v = 1f - progress;
            return v * v * v * p0 + 3f * v * v * progress * p1 + 3f * v * progress * progress * p2
                + progress * progress * progress * target;
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
            vertices.Add(worldToLocal.MultiplyPoint3x4(position));
            colors.Add(color); uv0.Add(uv); uv1.Add(new Vector2(style, seed));
        }
        void EnsureRuntimeObjects()
        {
            if (runtimeObject == null)
            {
                runtimeObject = new GameObject("Samurai guard clash (runtime)");
                runtimeObject.hideFlags = HideFlags.DontSave;
                runtimeObject.layer = gameObject.layer;
                runtimeObject.transform.SetParent(transform, false);
            }
            if (!enableVisuals || mesh != null) return;
            Shader shader = sparkShader;
            if (shader == null || shader.name == "KatanaDemo/VFX/SamuraiChargeWind")
                shader = Shader.Find("KatanaDemo/VFX/SamuraiGuardClash");
            if (shader == null) return;
            runtimeMaterial = new Material(shader) { name = "Samurai guard clash material", hideFlags = HideFlags.DontSave };
            mesh = new Mesh { name = "Samurai guard clash and recovery", hideFlags = HideFlags.DontSave };
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
        void PlayImpactSound(bool perfect)
        {
            if (!enableSound || soundVolume <= 0f) return;
            EnsureRuntimeObjects();
            if (audioSource == null)
            {
                audioSource = runtimeObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false; audioSource.loop = false;
                audioSource.spatialBlend = .6f; audioSource.minDistance = 3f; audioSource.maxDistance = 18f;
                audioSource.priority = 110;
            }
            if (perfect && metalSound == null) metalSound = CreateMetalSound(true);
            if (!perfect && blockSound == null) blockSound = CreateMetalSound(false);
            audioSource.clip = perfect ? metalSound : blockSound;
            audioSource.volume = Mathf.Clamp(soundVolume, 0f, .5f);
            audioSource.pitch = Mathf.Clamp(Time.timeScale, .1f, 2f);
            // 连续碰撞不切掉上一击余音，局部顿帧也不推迟冲击声。
            audioSource.PlayOneShot(audioSource.clip, perfect ? 1f : .7f);
            soundPaused = false;
        }
        void UpdateAudio()
        {
            if (audioSource == null) return;
            if (!enableSound) { audioSource.Stop(); soundPaused = false; return; }
            if (Time.timeScale <= 0f && !soundPaused && audioSource.isPlaying)
            { audioSource.Pause(); soundPaused = true; }
            else if (Time.timeScale > 0f && soundPaused) { audioSource.UnPause(); soundPaused = false; }
            audioSource.pitch = Mathf.Clamp(Time.timeScale, .1f, 2f);
        }
        static AudioClip CreateMetalSound(bool perfect)
        {
            const int sampleRate = 24000;
            float duration = perfect ? .19f : .135f;
            float[] samples = new float[Mathf.CeilToInt(sampleRate * duration)];
            uint noiseState = 117u;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)sampleRate, phase = t * Mathf.PI * 2f;
                noiseState = unchecked(noiseState * 1664525u + 1013904223u);
                float noise = (noiseState & 65535u) / 32767.5f - 1f;
                float attack = Mathf.Clamp01(t / .001f), tail = Mathf.Clamp01((duration - t) / .025f);
                float pitch = perfect ? 1f : .79f;
                float metal = Mathf.Sin(phase * 1350f * pitch) * .40f * Mathf.Exp(-t * 25f)
                    + Mathf.Sin(phase * 2190f * pitch) * .23f * Mathf.Exp(-t * 36f)
                    + Mathf.Sin(phase * 3230f * pitch) * .12f * Mathf.Exp(-t * 55f)
                    + noise * .18f * Mathf.Exp(-t * 120f);
                samples[i] = metal * attack * tail;
            }
            AudioClip clip = AudioClip.Create(perfect ? "Deflect metal (runtime)" : "Guard metal (runtime)", samples.Length, 1, sampleRate, false);
            clip.hideFlags = HideFlags.DontSave; clip.SetData(samples, 0);
            return clip;
        }
        public void ClearFeedback()
        {
            for (int i = 0; i < bursts.Length; i++) bursts[i].active = false;
            hasClock = soundPaused = false; nextBurst = 0;
            ActiveBurstCount = ActiveRecoveryCount = 0;
            if (audioSource != null) audioSource.Stop();
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
            if (metalSound != null) DestroyOwned(metalSound);
            if (blockSound != null) DestroyOwned(blockSound);
            runtimeObject = null; mesh = null; meshRenderer = null; runtimeMaterial = null;
            audioSource = null; metalSound = null; blockSound = null;
        }
        static float Hash(float seed) => Mathf.Repeat(Mathf.Sin(seed * 12.9898f) * 43758.5453f, 1f);
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        static void DestroyOwned(Object value)
        {
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
