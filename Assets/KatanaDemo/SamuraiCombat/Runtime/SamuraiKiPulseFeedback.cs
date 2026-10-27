using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OCDesign.Samurai
{
    /// <summary>只读残心状态：胸腰内聚光点、满额短闪和成功音。所有对象均为可清理的运行时资源。</summary>
    [DefaultExecutionOrder(330), DisallowMultipleComponent]
    public sealed class SamuraiKiPulseFeedback : MonoBehaviour
    {
        [SerializeField] SamuraiCombatant actor;
        [Tooltip("复用 SamuraiChargeWind 的局部光点分支。序列化引用保证构建时保留该 Shader。")]
        [SerializeField] Shader pulseShader;
        [SerializeField] bool enableVisuals = true;
        [SerializeField] bool enableSound = true;
        [SerializeField, Range(4, 14)] int moteCount = 8;
        [SerializeField, Range(.2f, .8f)] float gatheringRadius = .48f;
        [SerializeField, Range(.01f, .09f)] float moteSize = .044f;
        [Tooltip("统一缩放聚集范围、光点、满额闪光及成功散射，不改变时机和持续时间。")]
        [SerializeField, Range(.5f, 3f)] float visualScale = 1f;
        [Tooltip("光效中心向镜头的净空，避免亮部埋进身体或披风；仍保留正常深度遮挡。")]
        [SerializeField, Range(.1f, .6f)] float cameraClearance = .18f;
        [SerializeField, Range(0f, 2f)] float intensity = .8f;
        [SerializeField, Range(.08f, .2f)] float readyFlashSeconds = .12f;
        [SerializeField, Range(.12f, .5f)] float successSeconds = .26f;
        [SerializeField, Range(0f, .6f)] float soundVolume = .18f;
        [SerializeField] Color pulseColor = new Color(.61f, .91f, 1f, 1f);

        readonly List<Vector3> vertices = new List<Vector3>(80);
        readonly List<Color> colors = new List<Color>(80);
        readonly List<Vector2> uv0 = new List<Vector2>(80);
        readonly List<Vector2> uv1 = new List<Vector2>(80);
        readonly List<int> indices = new List<int>(120);
        SamuraiCombatant subscribedActor;
        Transform chest;
        Camera view;
        GameObject runtimeObject;
        Mesh mesh;
        MeshRenderer meshRenderer;
        Material runtimeMaterial;
        AudioSource source;
        AudioClip normalSound, perfectSound;
        Vector3 viewRight, viewUp, viewForward, successCenter;
        Matrix4x4 worldToLocal;
        float lastClock, readyStarted = float.NegativeInfinity, successStarted = float.NegativeInfinity;
        bool hasClock, readyObserved, successWasPerfect, soundPaused, soundJustStarted;

        public bool IsVisible => meshRenderer != null && meshRenderer.enabled && RuntimeVertexCount > 0;
        public int RuntimeVertexCount => mesh != null ? mesh.vertexCount : 0;
        public int ReadyFlashCount { get; private set; }
        public int SuccessCount { get; private set; }
        public float LastRecovered { get; private set; }
        public bool LastWasPerfect { get; private set; }
        float VisualScale => Mathf.Clamp(visualScale, .5f, 3f);

        public void Configure(SamuraiCombatant targetActor, Shader shader = null)
        {
            Unsubscribe();
            ClearFeedback();
            DestroyRuntimeObjects();
            actor = targetActor;
            if (shader != null) pulseShader = shader;
            chest = null;
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
        void OnApplicationFocus(bool focused) { if (!focused) ClearFeedback(); }
        void OnApplicationPause(bool paused) { if (paused) ClearFeedback(); }
        void LateUpdate() => UpdateVisual();

        void Subscribe()
        {
            if (subscribedActor == actor) return;
            Unsubscribe();
            subscribedActor = actor;
            if (subscribedActor == null) return;
            subscribedActor.KiPulsePerformed += OnKiPulsePerformed;
            // 中途启用只恢复已有光点，不补播先前已经到达的满额提示。
            readyObserved = actor.IsPerfectKiPulseWindow;
        }
        void Unsubscribe()
        {
            if (subscribedActor != null) subscribedActor.KiPulsePerformed -= OnKiPulsePerformed;
            subscribedActor = null;
        }

        void OnKiPulsePerformed(float recovered, bool perfect)
        {
            if (!isActiveAndEnabled || actor == null || actor.Actions == null || !actor.isActiveAndEnabled) return;
            LastRecovered = Mathf.Max(0f, recovered);
            LastWasPerfect = successWasPerfect = perfect;
            SuccessCount++;
            successStarted = actor.Actions.Clock;
            readyStarted = float.NegativeInfinity;
            ResolveView();
            successCenter = Center();
            if (!enableSound || soundVolume <= 0f) return;
            EnsureRuntimeObjects();
            if (source == null)
            {
                source = runtimeObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.loop = false;
                source.priority = 160;
            }
            if (normalSound == null) normalSound = CreateTone(false);
            if (perfectSound == null) perfectSound = CreateTone(true);
            source.Stop();
            source.clip = perfect ? perfectSound : normalSound;
            source.volume = Mathf.Clamp(soundVolume, 0f, .6f) * (perfect ? 1f : .72f);
            source.pitch = Mathf.Clamp(Time.timeScale, .1f, 2f);
            source.Play();
            soundPaused = false;
            soundJustStarted = true;
        }

        /// <summary>和动作同用 Clock：暂停、顿帧及手动模拟都不会让提示抢跑。</summary>
        public void UpdateVisual()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || actor == null
                || !actor.isActiveAndEnabled || actor.Actions == null || actor.IsDead)
            { ClearFeedback(); return; }
            if (subscribedActor != actor) { ClearFeedback(); chest = null; Subscribe(); }
            float clock = actor.Actions.Clock;
            if (!Finite(clock)) { ClearFeedback(); return; }
            if (hasClock && clock + .0001f < lastClock) ClearFeedback();
            float elapsed = hasClock ? Mathf.Max(0f, clock - lastClock) : 0f;
            lastClock = clock;
            hasClock = true;
            UpdateAudio(elapsed);

            bool window = actor.IsKiPulseWindowOpen;
            if (!window) { readyObserved = false; readyStarted = float.NegativeInfinity; }
            else if (!actor.IsPerfectKiPulseWindow) readyObserved = false;
            else if (!readyObserved)
            {
                readyObserved = true;
                readyStarted = clock;
                ReadyFlashCount++;
            }
            float successAge = clock - successStarted;
            bool success = successAge >= 0f && successAge < Mathf.Clamp(successSeconds, .12f, .5f);
            if (!enableVisuals || intensity <= 0f || (!window && !success)) { HideMesh(); return; }
            EnsureRuntimeObjects();
            if (mesh == null || meshRenderer == null) return;
            ResolveView();
            worldToLocal = runtimeObject.transform.worldToLocalMatrix;
            vertices.Clear(); colors.Clear(); uv0.Clear(); uv1.Clear(); indices.Clear();
            Vector3 center = Center();
            if (window) BuildMotes(center, Mathf.Clamp01(actor.KiPulseProgress));
            float readyAge = clock - readyStarted;
            if (window && readyAge >= 0f && readyAge < readyFlashSeconds)
                AddMote(center, .15f, 1.1f * Mathf.Pow(1f - readyAge / Mathf.Max(.01f, readyFlashSeconds), 2f));
            if (success) BuildSuccess(successAge);
            mesh.Clear(false);
            mesh.SetVertices(vertices); mesh.SetColors(colors);
            mesh.SetUVs(0, uv0); mesh.SetUVs(1, uv1); mesh.SetTriangles(indices, 0, false);
            mesh.RecalculateBounds();
            meshRenderer.enabled = vertices.Count > 0;
        }

        void BuildMotes(Vector3 center, float progress)
        {
            float inward = Mathf.SmoothStep(0f, 1f, progress);
            float radius = Mathf.Lerp(Mathf.Clamp(gatheringRadius, .2f, .8f), .065f, inward) * VisualScale;
            int count = Mathf.Clamp(moteCount, 4, 14);
            for (int i = 0; i < count; i++)
            {
                float angle = (i + .18f) * Mathf.PI * 2f / count + .22f * inward;
                Vector3 radial = viewRight * Mathf.Cos(angle) + viewUp * Mathf.Sin(angle) * .72f;
                Vector3 point = center + radial * radius * (.82f + .18f * Mathf.Sin(i * 2.7f));
                float size = Mathf.Clamp(moteSize, .01f, .09f) * Mathf.Lerp(.65f, 1f, inward);
                AddMote(point, size, Mathf.Lerp(.4f, .85f, inward));
            }
        }

        void BuildSuccess(float age)
        {
            float t = Mathf.Clamp01(age / Mathf.Clamp(successSeconds, .12f, .5f));
            float fade = (1f - t) * (1f - t);
            float strength = successWasPerfect ? 1.25f : .75f;
            AddMote(successCenter, (successWasPerfect ? .21f : .14f) * (1f + .35f * t), fade * strength);
            int count = successWasPerfect ? 8 : 5;
            for (int i = 0; i < count; i++)
            {
                float angle = (i + .35f) * Mathf.PI * 2f / count;
                float radius = Mathf.Lerp(.06f, successWasPerfect ? .31f : .20f, t) * VisualScale;
                Vector3 point = successCenter + (viewRight * Mathf.Cos(angle) + viewUp * Mathf.Sin(angle)) * radius;
                AddMote(point, .035f * (1f - .4f * t), fade * strength * .65f);
            }
        }

        void AddMote(Vector3 center, float radius, float alpha)
        {
            radius *= VisualScale;
            int start = vertices.Count;
            Color color = pulseColor;
            color.a = Mathf.Clamp01(alpha) * Mathf.Clamp(intensity, 0f, 2f);
            AddVertex(center - viewRight * radius - viewUp * radius, new Vector2(0, 0), color);
            AddVertex(center - viewRight * radius + viewUp * radius, new Vector2(0, 1), color);
            AddVertex(center + viewRight * radius - viewUp * radius, new Vector2(1, 0), color);
            AddVertex(center + viewRight * radius + viewUp * radius, new Vector2(1, 1), color);
            indices.Add(start); indices.Add(start + 2); indices.Add(start + 3);
            indices.Add(start); indices.Add(start + 3); indices.Add(start + 1);
        }
        void AddVertex(Vector3 position, Vector2 uv, Color color)
        {
            vertices.Add(worldToLocal.MultiplyPoint3x4(position));
            colors.Add(color); uv0.Add(uv); uv1.Add(new Vector2(2f, 0f));
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
        Vector3 Center()
        {
            if (chest == null && actor.Animator != null && actor.Animator.isHuman)
                chest = actor.Animator.GetBoneTransform(HumanBodyBones.Chest)
                    ?? actor.Animator.GetBoneTransform(HumanBodyBones.Spine);
            Vector3 center = chest != null ? chest.position : actor.transform.position + Vector3.up *
                (actor.Motor != null ? actor.Motor.height * .62f : .95f);
            // 向镜头留出很小净空，聚合点不埋进胸部网格；仍由正常深度测试遮挡。
            return center - viewForward * Mathf.Clamp(cameraClearance, .1f, .6f);
        }

        void EnsureRuntimeObjects()
        {
            if (runtimeObject == null)
            {
                runtimeObject = new GameObject("Samurai ki pulse (runtime)");
                runtimeObject.hideFlags = HideFlags.DontSave;
                runtimeObject.layer = gameObject.layer;
                runtimeObject.transform.SetParent(transform, false);
            }
            if (!enableVisuals || mesh != null) return;
            Shader shader = pulseShader != null ? pulseShader : Shader.Find("KatanaDemo/VFX/SamuraiChargeWind");
            if (shader == null) return;
            runtimeMaterial = new Material(shader) { name = "Samurai ki pulse material", hideFlags = HideFlags.DontSave };
            runtimeMaterial.SetFloat("_Intensity", .9f);
            mesh = new Mesh { name = "Samurai ki pulse motes", hideFlags = HideFlags.DontSave };
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

        void UpdateAudio(float clockDelta)
        {
            if (source == null) return;
            if (!enableSound) { source.Stop(); soundPaused = soundJustStarted = false; return; }
            bool paused = actor.IsHitStopped || Time.timeScale <= 0f || (clockDelta <= 0f && !soundJustStarted);
            if (paused && !soundPaused && source.isPlaying) { source.Pause(); soundPaused = true; }
            else if (!paused && soundPaused) { source.UnPause(); soundPaused = false; }
            source.pitch = Mathf.Clamp(Time.timeScale, .1f, 2f);
            soundJustStarted = false;
        }
        static AudioClip CreateTone(bool perfect)
        {
            const int sampleRate = 24000;
            float duration = perfect ? .22f : .14f;
            float[] samples = new float[Mathf.CeilToInt(sampleRate * duration)];
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)sampleRate, phase = t * Mathf.PI * 2f;
                float attack = Mathf.Clamp01(t / .006f);
                float fade = Mathf.Pow(1f - t / duration, 2.4f);
                // 中频短铃音，无噪声尖峰；完美音加入较轻的纯五度泛音。
                float tone = Mathf.Sin(phase * (perfect ? 740f : 620f)) * .52f
                    + Mathf.Sin(phase * (perfect ? 1110f : 930f)) * (perfect ? .17f : .08f);
                samples[i] = tone * attack * fade;
            }
            AudioClip clip = AudioClip.Create(perfect ? "Ki pulse perfect (runtime)" : "Ki pulse (runtime)", samples.Length, 1, sampleRate, false);
            clip.hideFlags = HideFlags.DontSave;
            clip.SetData(samples, 0);
            return clip;
        }

        public void ClearFeedback()
        {
            readyStarted = successStarted = float.NegativeInfinity;
            readyObserved = hasClock = soundPaused = soundJustStarted = false;
            if (source != null) source.Stop();
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
            if (normalSound != null) DestroyOwned(normalSound);
            if (perfectSound != null) DestroyOwned(perfectSound);
            runtimeObject = null; mesh = null; meshRenderer = null; runtimeMaterial = null;
            source = null; normalSound = perfectSound = null;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static void DestroyOwned(Object value)
        {
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
