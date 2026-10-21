using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace OCDesign.Samurai
{
    /// <summary>
    /// URP 17.6 RenderGraph：透明刀光之后读取完整相机色彩，热扭曲后交给后处理与独立屏幕 UI。
    /// 仅启用中的玩家相机反馈请求此 Pass；不修改 Pipeline、共享材质或全局 Shader 参数。
    /// </summary>
    public sealed class SamuraiRushHeatRendererFeature : ScriptableRendererFeature
    {
        [Tooltip("使用 KatanaDemo/VFX/SamuraiRushHeat；由定向安装器创建并引用。")]
        public Material passMaterial;

        HeatPass pass;
        public int LastRenderedFrame { get; private set; } = -1;
        public ulong LastRenderedCameraId { get; private set; }
        public float LastRenderedStrength { get; private set; }
        public int ExecutedPassCount { get; private set; }

        public void Configure(Material material)
        {
            passMaterial = material;
            if (pass == null) Create();
        }

        public override void Create() => pass = new HeatPass(this);

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!UsableMaterial(passMaterial) || !(renderer is UniversalRenderer)
                || !TryFeedback(renderingData.cameraData.camera, out _)) return;
            if (pass == null) Create();
            pass.Setup(passMaterial);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            // 临时色彩由 RenderGraph 管理；材质资产属于安装器，不在这里销毁。
            pass = null;
        }

        static bool UsableMaterial(Material material) => material != null
            && material.shader != null && material.shader.isSupported && material.passCount > 0;

        static bool TryFeedback(Camera camera, out SamuraiCombatCameraFeedback feedback)
        {
            feedback = null;
            return Application.isPlaying && camera != null && camera.cameraType == CameraType.Game
                && camera.TryGetComponent(out feedback) && feedback.isActiveAndEnabled
                && Finite(feedback.HeatDistortionStrength) && feedback.HeatDistortionStrength > .0001f;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        sealed class HeatPass : ScriptableRenderPass
        {
            const string PassName = "Samurai Rush Heat Distortion";
            static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
            static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
            static readonly int HeatParamsId = Shader.PropertyToID("_HeatParams");
            static readonly int HeatCenterId = Shader.PropertyToID("_HeatCenter");
            static readonly int HeatViewportId = Shader.PropertyToID("_HeatViewport");
            readonly SamuraiRushHeatRendererFeature owner;
            readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
            Material material;

            public HeatPass(SamuraiRushHeatRendererFeature feature)
            {
                owner = feature;
                profilingSampler = new ProfilingSampler(PassName);
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                ConfigureInput(ScriptableRenderPassInput.None);
            }

            public void Setup(Material nextMaterial)
            {
                material = nextMaterial;
                // 只有实际入队时才要求中间颜色附件；不采样只有不透明物体的 CameraOpaqueTexture。
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var cameraData = frameData.Get<UniversalCameraData>();
                if (!UsableMaterial(material) || !TryFeedback(cameraData.camera, out var feedback)) return;
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;
                TextureHandle destination = resources.activeColorTexture;
                if (!destination.IsValid()) return;

                // 与本地 FullScreenPass 同样先复制再写回，保留 camera stack 的附件身份。
                var copyDesc = renderGraph.GetTextureDesc(destination);
                copyDesc.name = "_SamuraiRushHeatSource";
                copyDesc.clearBuffer = false;
                copyDesc.msaaSamples = MSAASamples.None;
                copyDesc.bindTextureMS = false;
                copyDesc.filterMode = FilterMode.Bilinear;
                copyDesc.wrapMode = TextureWrapMode.Clamp;
                copyDesc.useMipMap = false;
                copyDesc.autoGenerateMips = false;
                copyDesc.memoryless = RenderTextureMemoryless.None;
                TextureHandle source = renderGraph.CreateTexture(copyDesc);
                renderGraph.AddBlitPass(destination, source, Vector2.one, Vector2.zero,
                    passName: "Copy Samurai Rush Heat Color");

                using (var builder = renderGraph.AddRasterRenderPass<PassData>(PassName, out var data, profilingSampler))
                {
                    float age = feedback.HeatDistortionTime;
                    Vector2 center = feedback.HeatDistortionCenter;
                    float width = Mathf.Max(1, cameraData.cameraTargetDescriptor.width);
                    float height = Mathf.Max(1, cameraData.cameraTargetDescriptor.height);
                    data.source = source;
                    data.destination = destination;
                    data.material = material;
                    data.properties = properties;
                    data.owner = owner;
                    data.frame = Time.frameCount;
                    data.cameraId = EntityId.ToULong(cameraData.camera.GetEntityId());
                    data.heat = new Vector4(Mathf.Clamp01(feedback.HeatDistortionStrength),
                        Finite(age) ? Mathf.Clamp(age, 0f, 30f) : 0f, width / height, 0f);
                    data.center = new Vector4(Finite(center.x) ? Mathf.Clamp01(center.x) : .5f,
                        Finite(center.y) ? Mathf.Clamp01(center.y) : .5f, 0f, 0f);
                    data.viewport = new Vector4(width, height, 1f / width, 1f / height);
                    builder.UseTexture(source, AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                    builder.SetRenderFunc(static (PassData d, RasterGraphContext context) => Execute(d, context));
                }
            }

            static void Execute(PassData data, RasterGraphContext context)
            {
                RTHandle source = data.source;
                Vector2 scale = source.useScaling
                    ? new Vector2(source.rtHandleProperties.rtHandleScale.x, source.rtHandleProperties.rtHandleScale.y)
                    : Vector2.one;
                bool flip = context.GetTextureUVOrigin(data.source) != context.GetTextureUVOrigin(data.destination);
                // URP 的 GetFinalBlitScaleBias 是 internal；按其本地实现计算缩放与 UV origin 差异。
                Vector4 scaleBias = flip ? new Vector4(scale.x, -scale.y, 0f, scale.y)
                    : new Vector4(scale.x, scale.y, 0f, 0f);
                data.properties.Clear();
                data.properties.SetTexture(BlitTextureId, source);
                data.properties.SetVector(BlitScaleBiasId, scaleBias);
                data.properties.SetVector(HeatParamsId, data.heat);
                data.properties.SetVector(HeatCenterId, data.center);
                data.properties.SetVector(HeatViewportId, data.viewport);
                context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0,
                    MeshTopology.Triangles, 3, 1, data.properties);
                if (data.owner != null)
                {
                    data.owner.LastRenderedFrame = data.frame;
                    data.owner.LastRenderedCameraId = data.cameraId;
                    data.owner.LastRenderedStrength = data.heat.x;
                    data.owner.ExecutedPassCount++;
                }
            }

            sealed class PassData
            {
                public TextureHandle source, destination;
                public Material material;
                public MaterialPropertyBlock properties;
                public SamuraiRushHeatRendererFeature owner;
                public Vector4 heat, center, viewport;
                public int frame;
                public ulong cameraId;
            }
        }
    }
}
