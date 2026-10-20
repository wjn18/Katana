using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OCDesign.Samurai
{
    /// <summary>
    /// 只读蓄力状态，在实际刀网格表面叠加青白发光；不修改原材质、网格或战斗数据。
    /// 叠层仅在运行时创建，刀根/刀尖的世界空间遮罩排除刀柄与装具。
    /// </summary>
    [DefaultExecutionOrder(310), DisallowMultipleComponent]
    public sealed class SamuraiChargeBladeGlow : MonoBehaviour
    {
        [SerializeField] SamuraiCombatant actor;
        [SerializeField] Transform bladeBase;
        [SerializeField] Transform bladeTip;
        [SerializeField] MeshFilter[] bladeMeshes = new MeshFilter[0];
        [SerializeField] Material glowMaterial;
        [SerializeField, Range(0f, 8f)] float peakIntensity = 1.8f;
        [SerializeField, Range(1f, 8f)] float pulseFrequency = 3.5f;

        static readonly int BladeBaseId = Shader.PropertyToID("_BladeBaseWS");
        static readonly int BladeTipId = Shader.PropertyToID("_BladeTipWS");
        static readonly int IntensityId = Shader.PropertyToID("_GlowIntensity");
        static readonly int ProgressId = Shader.PropertyToID("_ChargeProgress");

        sealed class Overlay
        {
            public MeshFilter source;
            public MeshRenderer sourceRenderer;
            public GameObject gameObject;
            public MeshRenderer renderer;
        }

        readonly List<Overlay> overlays = new List<Overlay>();
        MaterialPropertyBlock properties;
        SamuraiCombatant subscribedActor;

        public float GlowIntensity { get; private set; }
        public bool IsVisible
        {
            get
            {
                if (GlowIntensity <= .0001f) return false;
                foreach (Overlay overlay in overlays)
                    if (overlay.renderer != null && overlay.renderer.enabled
                        && overlay.renderer.gameObject.activeInHierarchy) return true;
                return false;
            }
        }

        public void Configure(SamuraiCombatant targetActor, Transform targetBase, Transform targetTip,
            MeshFilter[] sourceMeshes, Material material)
        {
            Unsubscribe();
            Extinguish();
            DestroyOverlays();
            actor = targetActor;
            bladeBase = targetBase;
            bladeTip = targetTip;
            bladeMeshes = sourceMeshes ?? new MeshFilter[0];
            glowMaterial = material;
            // 编辑器装配只保存配置，绝不把展示叠层保存进Prefab。
            if (Application.isPlaying && isActiveAndEnabled) Subscribe();
        }

        void OnEnable()
        {
            Extinguish();
            if (Application.isPlaying) Subscribe();
        }

        void OnDisable() { Unsubscribe(); Extinguish(); }
        void LateUpdate() => UpdateVisual();

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

        void OnStateChanged(string state) => Extinguish();

        /// <summary>与LateUpdate相同的更新入口，供真实动作/蓄力检查使用。</summary>
        public void UpdateVisual()
        {
            if (!Application.isPlaying || !isActiveAndEnabled)
            { Extinguish(); return; }
            if (subscribedActor != actor) { Extinguish(); Subscribe(); }
            if (actor == null || !actor.isActiveAndEnabled || !actor.IsCharging
                || actor.Animator == null || !actor.Animator.isActiveAndEnabled
                || bladeBase == null || bladeTip == null || glowMaterial == null
                || !bladeBase.gameObject.activeInHierarchy || !bladeTip.gameObject.activeInHierarchy)
            { Extinguish(); return; }

            Vector3 basePosition = bladeBase.position;
            Vector3 tipPosition = bladeTip.position;
            if ((tipPosition - basePosition).sqrMagnitude < .000001f)
            { Extinguish(); return; }
            float progress = Mathf.Clamp01(actor.ChargeNormalizedTime);
            float elapsed = actor.ChargeElapsedSeconds;
            if (!Finite(progress) || !Finite(elapsed) || !Finite(peakIntensity) || !Finite(pulseFrequency))
            { Extinguish(); return; }
            float peak = Mathf.Clamp(peakIntensity, 0f, 8f);
            float intensity = peak * .70f * Mathf.Pow(progress, 1.3f);
            if (progress >= 1f)
            {
                float required = actor.Profile != null ? Mathf.Max(.1f, actor.Profile.chargeSeconds) : 1.5f;
                float chargedTime = Mathf.Max(0f, elapsed - required);
                // 满蓄时从峰值起闪，暴击窗口内更明亮，之后保持柔和的平滑脉冲。
                float pulse = .5f + .5f * Mathf.Cos(chargedTime * Mathf.PI * 2f
                    * Mathf.Clamp(pulseFrequency, 1f, 8f));
                intensity = peak * (actor.IsChargeCriticalWindowOpen
                    ? Mathf.Lerp(.72f, 1f, pulse)
                    : Mathf.Lerp(.38f, .64f, pulse));
            }
            if (intensity <= .0001f) { Extinguish(); return; }

            EnsureOverlays();
            if (properties == null) properties = new MaterialPropertyBlock();
            properties.Clear();
            properties.SetVector(BladeBaseId, new Vector4(basePosition.x, basePosition.y, basePosition.z, 1f));
            properties.SetVector(BladeTipId, new Vector4(tipPosition.x, tipPosition.y, tipPosition.z, 1f));
            properties.SetFloat(IntensityId, intensity);
            properties.SetFloat(ProgressId, progress);
            bool anyVisible = false;
            foreach (Overlay overlay in overlays)
            {
                if (overlay.renderer == null) continue;
                bool visible = overlay.source != null && overlay.source.sharedMesh != null
                    && overlay.source.gameObject.activeInHierarchy
                    && (overlay.sourceRenderer == null || overlay.sourceRenderer.enabled);
                overlay.renderer.enabled = visible;
                if (!visible) continue;
                overlay.renderer.SetPropertyBlock(properties);
                anyVisible = true;
            }
            GlowIntensity = anyVisible ? intensity : 0f;
        }

        void EnsureOverlays()
        {
            if (!Application.isPlaying || overlays.Count > 0 || bladeMeshes == null) return;
            var used = new HashSet<MeshFilter>();
            foreach (MeshFilter source in bladeMeshes)
            {
                if (source == null || source.sharedMesh == null || !used.Add(source)) continue;
                var instance = new GameObject("Samurai Charge Blade Glow (runtime)");
                instance.hideFlags = HideFlags.DontSave;
                instance.layer = source.gameObject.layer;
                // 局部单位变换复用源的完整父级缩放/旋转；不假设武器缩放为1。
                instance.transform.SetParent(source.transform, false);
                var filter = instance.AddComponent<MeshFilter>();
                filter.sharedMesh = source.sharedMesh;
                var renderer = instance.AddComponent<MeshRenderer>();
                var materials = new Material[Mathf.Max(1, source.sharedMesh.subMeshCount)];
                for (int i = 0; i < materials.Length; i++) materials[i] = glowMaterial;
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.enabled = false;
                overlays.Add(new Overlay { source = source, sourceRenderer = source.GetComponent<MeshRenderer>(),
                    gameObject = instance, renderer = renderer });
            }
        }

        void Extinguish()
        {
            GlowIntensity = 0f;
            foreach (Overlay overlay in overlays)
                if (overlay.renderer != null) overlay.renderer.enabled = false;
        }

        void DestroyOverlays()
        {
            foreach (Overlay overlay in overlays)
            {
                if (overlay.gameObject == null) continue;
                overlay.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(overlay.gameObject);
                else DestroyImmediate(overlay.gameObject);
            }
            overlays.Clear();
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        void OnDestroy() { Unsubscribe(); DestroyOverlays(); }
    }
}
