using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OCDesign.Samurai
{
    /// <summary>冬月身体聚光、短气流与世界空间碎光；只读合法冬月状态，不参与战斗或根运动。</summary>
    [DefaultExecutionOrder(340), DisallowMultipleComponent]
    public sealed class SamuraiFrostMoonVfx : MonoBehaviour
    {
        [SerializeField] SamuraiCombatant actor;
        [SerializeField] Material effectMaterial;
        [SerializeField] SamuraiActionDefinition highAction, midAction;
        [SerializeField, Range(0f, 2f)] float intensity = 1f;
        [SerializeField, Range(.1f, .5f)] float fragmentLifetime = .28f;
        [SerializeField, Range(.3f, 1.4f)] float spread = .78f;

        struct Burst { public Vector3 center, wake; public float born; public int seed; }
        readonly List<Burst> bursts = new List<Burst>(8);
        readonly List<Vector3> vertices = new List<Vector3>(1200);
        readonly List<Color> colors = new List<Color>(1200);
        readonly List<Vector2> uv = new List<Vector2>(1200), styles = new List<Vector2>(1200);
        readonly List<int> indices = new List<int>(1800);
        static readonly float[] MidBursts = { .12f, .22f, .30f, .38f, .46f };
        static readonly float[] HighBursts = { .32f, .38f, .44f, .50f };
        SamuraiCombatant subscribedActor;
        Transform chest;
        Camera view;
        GameObject runtimeObject;
        Mesh mesh;
        MeshRenderer meshRenderer;
        Matrix4x4 worldToLocal;
        Vector3 right, up, forward, previousCenter;
        float previousPhase, lastClock;
        int serial = -1, nextBurst;
        bool hasSample, explicitView;

        public bool IsVisible => meshRenderer != null && meshRenderer.enabled && RuntimeVertexCount > 0;
        public int RuntimeVertexCount => mesh != null ? mesh.vertexCount : 0;
        public int LiveBurstCount => bursts.Count;
        public int EmittedBurstCount { get; private set; }

        public void Configure(SamuraiCombatant owner, Material material, SamuraiActionDefinition high, SamuraiActionDefinition mid)
        {
            Unsubscribe(); ClearVisuals(); DestroyRuntime();
            actor = owner; effectMaterial = material; highAction = high; midAction = mid;
            chest = null; view = null; explicitView = false;
            if (Application.isPlaying && isActiveAndEnabled) Subscribe();
        }
        public void SetViewCamera(Camera camera) { view = camera; explicitView = camera != null; }
        void Awake() { if (actor == null) actor = GetComponent<SamuraiCombatant>(); }
        void OnEnable() { ClearVisuals(); if (Application.isPlaying) Subscribe(); }
        void OnDisable() { Unsubscribe(); ClearVisuals(); DestroyRuntime(); }
        void OnDestroy() { Unsubscribe(); DestroyRuntime(); }
        void LateUpdate() => SampleNow();
        void Subscribe()
        {
            if (subscribedActor == actor) return;
            Unsubscribe(); subscribedActor = actor;
            if (subscribedActor != null) subscribedActor.CombatReset += ClearVisuals;
        }
        void Unsubscribe()
        {
            if (subscribedActor != null) subscribedActor.CombatReset -= ClearVisuals;
            subscribedActor = null;
        }

        /// <summary>Animator、根运动与Tick后调用；只以战斗Clock计龄，暂停/顿帧不老化或重发。</summary>
        public void SampleNow()
        {
            if (subscribedActor != actor) { ClearVisuals(); chest = null; Subscribe(); }
            if (!Application.isPlaying || !isActiveAndEnabled || actor == null || !actor.isActiveAndEnabled
                || actor.Actions == null || actor.IsDead || !actor.IsFrostMoonAttack
                || (actor.Actions.ActiveAction != highAction && actor.Actions.ActiveAction != midAction)
                || effectMaterial == null || intensity <= 0f)
            { ClearVisuals(); return; }
            float clock = actor.Actions.Clock, phase = actor.StateNormalizedTime;
            if (!Finite(clock) || !Finite(phase)) { ClearVisuals(); return; }
            if (hasSample && (clock < lastClock - .0001f || phase < previousPhase - .001f
                || actor.Actions.AttackSerial != serial)) ClearVisuals();
            ResolveView();
            Vector3 center = BodyCenter();
            if (!hasSample)
            {
                hasSample = true; serial = actor.Actions.AttackSerial;
                previousCenter = center; previousPhase = phase; lastClock = clock;
                // 中途启用不补发已过去的碎光，正常动作起步时phase为0。
                float[] past = actor.Actions.ActiveAction == midAction ? MidBursts : HighBursts;
                while (nextBurst < past.Length && past[nextBurst] < phase) nextBurst++;
            }
            bool frozen = actor.IsHitStopped || Time.timeScale <= 0f;
            bool mid = actor.Actions.ActiveAction == midAction;
            float[] emission = mid ? MidBursts : HighBursts;
            if (!frozen)
            {
                while (nextBurst < emission.Length && phase >= emission[nextBurst])
                {
                    float fraction = Mathf.InverseLerp(previousPhase, phase, emission[nextBurst]);
                    Vector3 origin = Vector3.Lerp(previousCenter, center, fraction);
                    Vector3 wake = previousCenter - center; wake.y = 0f;
                    wake = Vector3.ClampMagnitude(wake * 2f, 1.2f);
                    bursts.Add(new Burst { center = origin, wake = wake,
                        born = Mathf.Lerp(lastClock, clock, fraction), seed = nextBurst + (mid ? 13 : 41) });
                    nextBurst++; EmittedBurstCount++;
                }
                float lifetime = Mathf.Clamp(fragmentLifetime, .1f, .5f);
                bursts.RemoveAll(b => clock - b.born >= lifetime);
            }
            EnsureMesh(); if (mesh == null) return;
            worldToLocal = runtimeObject.transform.worldToLocalMatrix;
            vertices.Clear(); colors.Clear(); uv.Clear(); styles.Clear(); indices.Clear();
            float strike = mid ? .29f : .38f;
            // 短暂的局部青白亮点，散光沿身体气流展开，不做整屏调色。
            float gather = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, .12f, phase))
                * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(strike, strike + .16f, phase)));
            if (gather > .001f)
            {
                float flash = Mathf.Exp(-Mathf.Pow((phase - strike) / .045f, 2f));
                Vector3 visibleCenter = center - forward * .34f;
                Quad(visibleCenter, right * (.46f + flash * .18f), up * (.46f + flash * .18f),
                    new Color(.30f, .80f, 1f, (gather * .24f + flash * .70f) * intensity), 0, 0);
                Quad(visibleCenter - forward * .01f, right * .12f, up * .12f,
                    new Color(.8f, 1f, 1f, flash * intensity), 0, 0);
                for (int i = 0; i < 11; i++)
                {
                    float a = i * 2.39996f; Vector3 dir = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                    float length = .30f + Hash(i + 9) * .52f;
                    Quad(visibleCenter + dir * length * .48f, dir * length,
                        Vector3.Cross(forward, dir).normalized * .045f,
                        new Color(.72f, .87f, .94f, gather * .36f * intensity), 2, i);
                }
            }
            foreach (Burst burst in bursts) BuildBurst(burst, Mathf.Max(0f, clock - burst.born));
            mesh.Clear(false); mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetUVs(0, uv);
            mesh.SetUVs(1, styles); mesh.SetTriangles(indices, 0, false); mesh.RecalculateBounds();
            meshRenderer.enabled = vertices.Count > 0;
            if (!frozen) { previousPhase = phase; previousCenter = center; lastClock = clock; }
        }

        void BuildBurst(Burst burst, float age)
        {
            float life = Mathf.Clamp(fragmentLifetime, .1f, .5f), t = Mathf.Clamp01(age / life);
            float fade = Mathf.Pow(1f - t, 1.7f) * intensity;
            for (int i = 0; i < 18; i++)
            {
                float seed = burst.seed * 31 + i, a = Hash(seed) * Mathf.PI * 2f;
                Vector3 radial = new Vector3(Mathf.Cos(a), .30f * (Hash(seed + 4) - .5f), Mathf.Sin(a));
                float radius = (.16f + .58f * Hash(seed + 2) + t * .40f) * spread;
                Vector3 point = burst.center + radial * radius + Vector3.down * (.23f + t * t * .20f)
                    + burst.wake * (.15f + t * .3f);
                float angle = Hash(seed + 5) * Mathf.PI * 2f + t * (Hash(seed + 7) - .5f);
                Vector3 axis = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                float size = .045f + Hash(seed + 8) * .09f;
                Quad(point, axis * size, Vector3.Cross(forward, axis).normalized * size * (.35f + Hash(seed + 1) * .5f),
                    new Color(.82f, .96f, 1f, fade * .72f), 1, Hash(seed + 6));
                if (i < 4)
                {
                    Vector3 dir = (radial + burst.wake).normalized;
                    Vector3 side = Vector3.Cross(forward, dir).normalized;
                    Quad(point - dir * .18f, dir * (.3f + t * .22f), side * .035f,
                        new Color(.65f, .77f, .82f, fade * .13f), 2, seed);
                }
            }
        }
        void Quad(Vector3 center, Vector3 horizontal, Vector3 vertical, Color color, int style, float seed)
        {
            int start = vertices.Count;
            vertices.Add(worldToLocal.MultiplyPoint3x4(center - horizontal - vertical));
            vertices.Add(worldToLocal.MultiplyPoint3x4(center - horizontal + vertical));
            vertices.Add(worldToLocal.MultiplyPoint3x4(center + horizontal + vertical));
            vertices.Add(worldToLocal.MultiplyPoint3x4(center + horizontal - vertical));
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(1, 0));
            for (int i = 0; i < 4; i++) { colors.Add(color); styles.Add(new Vector2(style, seed)); }
            indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
            indices.Add(start); indices.Add(start + 2); indices.Add(start + 3);
        }
        Vector3 BodyCenter()
        {
            if (chest == null && actor.Animator != null && actor.Animator.isHuman)
                chest = actor.Animator.GetBoneTransform(HumanBodyBones.Chest) ?? actor.Animator.GetBoneTransform(HumanBodyBones.Spine);
            return chest != null ? chest.position : actor.transform.position + Vector3.up * .85f;
        }
        void ResolveView()
        {
            if (view == null || (!explicitView && !view.isActiveAndEnabled))
            {
                var input = actor.GetComponent<SamuraiPlayerInput>();
                view = input != null && input.cameraTransform != null ? input.cameraTransform.GetComponent<Camera>() : null;
                if (view == null) view = Camera.main;
            }
            right = view != null ? view.transform.right : actor.transform.right;
            up = view != null ? view.transform.up : Vector3.up;
            forward = view != null ? view.transform.forward : actor.transform.forward;
        }
        void EnsureMesh()
        {
            if (mesh != null) return;
            runtimeObject = new GameObject("Samurai Frost Moon (runtime)") { hideFlags = HideFlags.DontSave };
            runtimeObject.layer = gameObject.layer; runtimeObject.transform.SetParent(transform, false);
            mesh = new Mesh { name = "Frost Moon body light and shards", hideFlags = HideFlags.DontSave }; mesh.MarkDynamic();
            runtimeObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = runtimeObject.AddComponent<MeshRenderer>(); meshRenderer.sharedMaterial = effectMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off; meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off; meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }
        public void ClearVisuals()
        {
            bursts.Clear(); hasSample = false; serial = -1; nextBurst = 0;
            if (meshRenderer != null) meshRenderer.enabled = false;
            if (mesh != null && mesh.vertexCount > 0) mesh.Clear(false);
        }
        void DestroyRuntime()
        {
            if (runtimeObject != null) { runtimeObject.SetActive(false); DestroyOwned(runtimeObject); }
            if (mesh != null) DestroyOwned(mesh);
            runtimeObject = null; mesh = null; meshRenderer = null;
        }
        static void DestroyOwned(Object value) { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
        static float Hash(float value) => Mathf.Repeat(Mathf.Sin(value * 127.1f) * 43758.5453f, 1f);
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
