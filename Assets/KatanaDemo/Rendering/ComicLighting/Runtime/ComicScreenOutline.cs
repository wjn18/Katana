using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace OCDesign.LookDev
{
    /// <summary>Camera-local screen-width floor for explicitly installed comic ink hulls.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class ComicScreenOutline : MonoBehaviour
    {
        public Camera viewCamera;
        public Shader screenOutlineShader;
        public bool restrictToPreviewScene = true;
        [Range(0f, 8f)] public float minimumPixels = 3f;
        public Color outlineColor = new Color(0.055f, 0.036f, 0.027f, 1f);
        [Min(0f)] public float depthTolerance = 0.0005f;
        public int LastDrawCount { get; private set; }

        const string ShaderName = "Hidden/OC Design/Comic Screen Outline";
        const string HullShaderName = "OC Design/Comic Lighting/Outline";
        static readonly int MaskId = Shader.PropertyToID("_ComicOutlineMask");
        static readonly int DepthId = Shader.PropertyToID("_ComicSceneDepth");
        static readonly int MaskTexelId = Shader.PropertyToID("_ComicOutlineMask_TexelSize");
        static readonly int DepthTexelId = Shader.PropertyToID("_ComicSceneDepth_TexelSize");
        static readonly int PixelsId = Shader.PropertyToID("_OutlinePixels");
        static readonly int ColorId = Shader.PropertyToID("_OutlineColor");
        static readonly int ToleranceId = Shader.PropertyToID("_DepthTolerance");

        struct Source
        {
            public Renderer body, hull;
        }

        struct Draw
        {
            public Renderer renderer;
            public Material maskMaterial;
            public int submesh;
        }

        readonly List<Source> sources = new List<Source>();
        readonly List<GameObject> roots = new List<GameObject>();
        readonly List<Renderer> scan = new List<Renderer>();
        readonly List<Material> shared = new List<Material>();
        readonly List<Draw> draws = new List<Draw>();
        readonly Dictionary<Material, Material> maskMaterials = new Dictionary<Material, Material>();
        readonly HashSet<Material> liveMaterials = new HashSet<Material>();
        readonly List<Material> staleMaterials = new List<Material>();
        readonly Plane[] frustum = new Plane[6];
        Camera preparedCamera;
        Material composite;
        OutlinePass pass;
        double nextScan;

        void OnEnable()
        {
            if (!viewCamera) viewCamera = GetComponent<Camera>();
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RefreshSources();
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            ReleaseMaterials();
            sources.Clear();
            LastDrawCount = 0;
        }

        void OnDestroy() => OnDisable();
        void OnValidate() => RefreshSources();

        /// <summary>Request a rescan after adding/replacing preview actors or generic hulls.</summary>
        public void RefreshSources() => nextScan = 0d;

        bool AcceptCamera(Camera camera)
        {
            Scene scene = gameObject.scene;
            return isActiveAndEnabled && camera && camera == viewCamera &&
                camera.cameraType == CameraType.Game && scene.IsValid() && scene.isLoaded &&
                (!restrictToPreviewScene || scene.name == "ComicLightingCourtyard") && camera.gameObject.scene == scene &&
                SceneManager.GetActiveScene() == scene;
        }

        void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!AcceptCamera(camera) || minimumPixels <= 0f || outlineColor.a <= 0f) return;
            if (!camera.TryGetComponent<UniversalAdditionalCameraData>(out var cameraData) ||
                cameraData.renderType != CameraRenderType.Base) return;
            ScriptableRenderer renderer = cameraData.scriptableRenderer;
            if (renderer == null) return;
            if (preparedCamera != camera)
            {
                ReleaseMaterials();
                preparedCamera = camera;
                RefreshSources();
            }
            if (!composite)
            {
                Shader shader = screenOutlineShader ? screenOutlineShader : Shader.Find(ShaderName);
                if (!shader || !shader.isSupported) return;
                composite = CoreUtils.CreateEngineMaterial(shader);
                composite.name = "Comic preview screen outline (temporary)";
                pass = new OutlinePass(this);
            }
            if (Time.realtimeSinceStartupAsDouble >= nextScan)
            {
                CollectSources();
                nextScan = Time.realtimeSinceStartupAsDouble + 0.75d;
            }
            BuildDraws(camera);
            LastDrawCount = draws.Count;
            if (draws.Count == 0) return;
            pass.Setup(camera, draws.ToArray(), composite, minimumPixels, outlineColor, depthTolerance);
            renderer.EnqueuePass(pass);
        }

        void CollectSources()
        {
            sources.Clear();
            roots.Clear();
            liveMaterials.Clear();
            gameObject.scene.GetRootGameObjects(roots);
            foreach (GameObject root in roots)
            {
                scan.Clear();
                root.GetComponentsInChildren(true, scan);
                foreach (Renderer hull in scan)
                {
                    if (!hull || !hull.transform.parent || !IsGenericHull(hull)) continue;
                    Renderer body = hull.transform.parent.GetComponent<Renderer>();
                    if (!body || body == hull || body.gameObject.scene != gameObject.scene ||
                        (!(body is MeshRenderer) && !(body is SkinnedMeshRenderer))) continue;
                    sources.Add(new Source { body = body, hull = hull });
                    body.GetSharedMaterials(shared);
                    foreach (Material material in shared) if (material) liveMaterials.Add(material);
                }
            }
            staleMaterials.Clear();
            foreach (var entry in maskMaterials)
                if (!entry.Key || !liveMaterials.Contains(entry.Key)) staleMaterials.Add(entry.Key);
            foreach (Material source in staleMaterials)
            {
                CoreUtils.Destroy(maskMaterials[source]);
                maskMaterials.Remove(source);
            }
        }

        bool IsGenericHull(Renderer renderer)
        {
            renderer.GetSharedMaterials(shared);
            foreach (Material material in shared)
                if (material && material.shader && material.shader.name == HullShaderName &&
                    (!material.HasProperty("_BoxOutline") || material.GetFloat("_BoxOutline") <= 0.5f))
                    return true;
            return false;
        }

        void BuildDraws(Camera camera)
        {
            draws.Clear();
            GeometryUtility.CalculateFrustumPlanes(camera, frustum);
            foreach (Source source in sources)
            {
                Renderer body = source.body, hull = source.hull;
                if (!body || !hull || !body.enabled || !hull.enabled || body.forceRenderingOff || hull.forceRenderingOff ||
                    !body.gameObject.activeInHierarchy || !hull.gameObject.activeInHierarchy ||
                    body.gameObject.scene != gameObject.scene ||
                    (camera.cullingMask & (1 << body.gameObject.layer)) == 0 ||
                    (camera.cullingMask & (1 << hull.gameObject.layer)) == 0 ||
                    !GeometryUtility.TestPlanesAABB(frustum, body.bounds)) continue;
                Mesh mesh = body is SkinnedMeshRenderer skin ? skin.sharedMesh : body.GetComponent<MeshFilter>()?.sharedMesh;
                if (!mesh) continue;
                body.GetSharedMaterials(shared);
                int count = Mathf.Min(mesh.subMeshCount, shared.Count);
                for (int submesh = 0; submesh < count; submesh++)
                {
                    Material original = shared[submesh];
                    if (!original) continue;
                    Material mask = GetMaskMaterial(original);
                    draws.Add(new Draw { renderer = body, maskMaterial = mask, submesh = submesh });
                }
            }
        }

        Material GetMaskMaterial(Material original)
        {
            if (!maskMaterials.TryGetValue(original, out Material mask) || !mask)
            {
                mask = CoreUtils.CreateEngineMaterial(composite.shader);
                mask.name = "Comic mask: " + original.name;
                maskMaterials[original] = mask;
            }
            string textureProperty = original.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
            bool hasMap = original.HasProperty(textureProperty);
            mask.SetTexture("_BaseMap", hasMap ? original.GetTexture(textureProperty) : Texture2D.whiteTexture);
            mask.SetTextureScale("_BaseMap", hasMap ? original.GetTextureScale(textureProperty) : Vector2.one);
            mask.SetTextureOffset("_BaseMap", hasMap ? original.GetTextureOffset(textureProperty) : Vector2.zero);
            mask.SetColor("_BaseColor", original.HasProperty("_BaseColor") ? original.GetColor("_BaseColor") : Color.white);
            bool clipped = original.IsKeywordEnabled("_ALPHATEST_ON") ||
                (original.HasProperty("_AlphaClip") && original.GetFloat("_AlphaClip") > 0.5f);
            mask.SetFloat("_AlphaClip", clipped ? 1f : 0f);
            CoreUtils.SetKeyword(mask, "_ALPHATEST_ON", clipped);
            mask.SetFloat("_Cutoff", original.HasProperty("_Cutoff") ? original.GetFloat("_Cutoff") : 0.5f);
            mask.SetFloat("_Cull", original.HasProperty("_Cull") ? original.GetFloat("_Cull") : (float)CullMode.Back);
            mask.SetFloat("_SamuraiOcclusionFade", original.HasProperty("_SamuraiOcclusionFade")
                ? original.GetFloat("_SamuraiOcclusionFade") : 0f);
            return mask;
        }

        void ReleaseMaterials()
        {
            foreach (Material material in maskMaterials.Values) CoreUtils.Destroy(material);
            maskMaterials.Clear();
            CoreUtils.Destroy(composite);
            composite = null;
            pass = null;
            preparedCamera = null;
        }

        sealed class OutlinePass : ScriptableRenderPass
        {
            readonly ComicScreenOutline owner;
            readonly MaterialPropertyBlock compositeProperties = new MaterialPropertyBlock();
            Camera camera;
            Draw[] draws;
            Material composite;
            float pixels, tolerance;
            Color color;

            sealed class MaskData
            {
                public Draw[] draws;
            }

            sealed class CompositeData
            {
                public TextureHandle mask, depth;
                public Material material;
                public MaterialPropertyBlock properties;
                public Vector4 maskTexel, depthTexel;
                public float pixels, tolerance;
                public Color color;
            }

            public OutlinePass(ComicScreenOutline owner)
            {
                this.owner = owner;
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public void Setup(Camera camera, Draw[] draws, Material material, float pixels, Color color, float tolerance)
            {
                this.camera = camera; this.draws = draws; composite = material;
                this.pixels = pixels; this.color = color; this.tolerance = tolerance;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var cameraData = frameData.Get<UniversalCameraData>();
                if (!owner || !owner.AcceptCamera(cameraData.camera) || cameraData.camera != camera || !composite || draws == null) return;
                var resources = frameData.Get<UniversalResourceData>();
                TextureHandle depth = resources.cameraDepthTexture;
                TextureHandle depthAttachment = resources.activeDepthTexture;
                TextureHandle colorTarget = resources.activeColorTexture;
                if (!depth.IsValid() || !depthAttachment.IsValid() || !colorTarget.IsValid()) return;
                // Use precisely the attachment's size, dimension and MSAA count.
                // Hardware depth testing then agrees with the source render, while
                // RG resolves (eyeDepth, coverage) before the composite samples it.
                TextureDesc desc = graph.GetTextureDesc(depthAttachment);
                desc.name = "Comic visible source depth and coverage";
                desc.colorFormat = GraphicsFormat.R32G32_SFloat;
                desc.depthBufferBits = DepthBits.None;
                desc.bindTextureMS = false;
                desc.useMipMap = false;
                desc.autoGenerateMips = false;
                desc.enableRandomWrite = false;
                desc.clearBuffer = true;
                desc.clearColor = Color.clear;
                desc.filterMode = FilterMode.Point;
                desc.wrapMode = TextureWrapMode.Clamp;
                TextureHandle mask = graph.CreateTexture(desc);
                TextureDesc depthDesc = graph.GetTextureDesc(depth);
                int maskWidth = desc.width > 0 ? desc.width : cameraData.cameraTargetDescriptor.width;
                int maskHeight = desc.height > 0 ? desc.height : cameraData.cameraTargetDescriptor.height;
                int depthWidth = depthDesc.width > 0 ? depthDesc.width : maskWidth;
                int depthHeight = depthDesc.height > 0 ? depthDesc.height : maskHeight;

                using (var builder = graph.AddRasterRenderPass<MaskData>("Comic source silhouette mask", out var data))
                {
                    data.draws = draws;
                    builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(depthAttachment, AccessFlags.Read);
                    builder.SetRenderFunc(static (MaskData data, RasterGraphContext context) =>
                    {
                        foreach (Draw draw in data.draws)
                        {
                            if (!draw.renderer || !draw.maskMaterial) continue;
                            context.cmd.DrawRenderer(draw.renderer, draw.maskMaterial, draw.submesh, 0);
                        }
                    });
                }
                using (var builder = graph.AddRasterRenderPass<CompositeData>("Comic minimum screen ink", out var data))
                {
                    data.mask = mask; data.depth = depth; data.material = composite;
                    data.properties = compositeProperties;
                    data.maskTexel = new Vector4(1f / maskWidth, 1f / maskHeight, maskWidth, maskHeight);
                    data.depthTexel = new Vector4(1f / depthWidth, 1f / depthHeight, depthWidth, depthHeight);
                    data.pixels = pixels; data.color = color; data.tolerance = tolerance;
                    builder.UseTexture(mask, AccessFlags.Read);
                    builder.UseTexture(depth, AccessFlags.Read);
                    // Blend reads the destination attachment; no sampled color copy.
                    builder.SetRenderAttachment(colorTarget, 0, AccessFlags.ReadWrite);
                    builder.SetRenderFunc(static (CompositeData data, RasterGraphContext context) =>
                    {
                        MaterialPropertyBlock properties = data.properties;
                        properties.SetTexture(MaskId, data.mask);
                        properties.SetTexture(DepthId, data.depth);
                        properties.SetVector(MaskTexelId, data.maskTexel);
                        properties.SetVector(DepthTexelId, data.depthTexel);
                        properties.SetFloat(PixelsId, data.pixels);
                        properties.SetColor(ColorId, data.color);
                        properties.SetFloat(ToleranceId, data.tolerance);
                        context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 1, MeshTopology.Triangles, 3, 1, properties);
                    });
                }
            }
        }
    }
}
