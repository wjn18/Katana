using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace OCDesign.Samurai
{
    // Capture the fading architectural pieces separately, then blend the
    // nearest surface over the revealed scene with its original lighting.
    internal sealed class SamuraiCameraOcclusion : IDisposable
    {
        const float FadeInSeconds = .12f;
        const float ClearHoldSeconds = .13f;
        const float RestoreSeconds = .25f;
        const float HiddenFade = 1f / .85f;
        const float NearDecorationDepth = 2.6f;

        static readonly int EnabledId = Shader.PropertyToID("_SamuraiOcclusionEnabled");
        static readonly int FadeId = Shader.PropertyToID("_SamuraiOcclusionFade");
        static readonly int PlayerId = Shader.PropertyToID("_SamuraiOcclusionPlayer");
        static readonly Vector3[] BodySamples =
        {
            new Vector3(0, .16f, 0),
            new Vector3(0, .42f, 0),
            new Vector3(0, .94f, 0),
            new Vector3(0, 1.65f, 0),
            new Vector3(-.24f, .94f, 0),
            new Vector3(.24f, .94f, 0)
        };
        static readonly Vector2[] DecorationViewportSamples =
        {
            new Vector2(.5f, .90f), new Vector2(.5f, .70f),
            new Vector2(.24f, .79f), new Vector2(.76f, .79f),
            new Vector2(.18f, .61f), new Vector2(.82f, .61f)
        };

        readonly SamuraiOrbitCamera orbit;
        readonly Camera camera;
        readonly List<GroupState> groups = new List<GroupState>();
        readonly RaycastHit[] hits = new RaycastHit[32];
        readonly List<Material> shared = new List<Material>();
        readonly List<SurfaceOverride> overrides = new List<SurfaceOverride>();
        readonly MaterialPropertyBlock working = new MaterialPropertyBlock();
        readonly List<Draw> draws = new List<Draw>();
        readonly Vector3[] visualTargets = new Vector3[BodySamples.Length + DecorationViewportSamples.Length];

        Material composite;
        ForegroundPass foreground;
        Transform target;
        double previousTime = -1;
        int overrideCount;

        sealed class GroupState
        {
            public SamuraiOcclusionGroup group;
            public double lastHit;
            public float progress;
        }

        sealed class SurfaceOverride
        {
            public Renderer renderer;
            public int index;
            public readonly MaterialPropertyBlock original = new MaterialPropertyBlock();
        }

        struct Draw
        {
            public Renderer renderer;
            public Material material;
            public int index;
            public int pass;
        }

        public SamuraiCameraOcclusion(SamuraiOrbitCamera orbit)
        {
            this.orbit = orbit;
            camera = orbit.GetComponent<Camera>();
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            RestoreSurfaces();
            CoreUtils.Destroy(composite);
        }

        void BeginCamera(ScriptableRenderContext context, Camera renderingCamera)
        {
            if (renderingCamera != camera || camera.cameraType != CameraType.Game) return;
            if (!orbit.softenPlayerOcclusion || !orbit.target || !orbit.target.gameObject.activeInHierarchy ||
                !SamuraiOcclusionGroup.HasBlockers)
            {
                groups.Clear();
                previousTime = -1;
                return;
            }
            if (!camera.TryGetComponent(out UniversalAdditionalCameraData data) ||
                data.renderType != CameraRenderType.Base || !(data.scriptableRenderer is UniversalRenderer renderer))
                return;

            double now = Time.realtimeSinceStartupAsDouble;
            if (target != orbit.target)
            {
                target = orbit.target;
                groups.Clear();
                previousTime = -1;
            }
            float delta = previousTime < 0 ? 0f : Mathf.Min(.1f, (float)(now - previousTime));
            previousTime = now;
            UpdateGroups(now, delta);
            if (groups.Count == 0) return;
            if (!composite)
            {
                Shader shader = Resources.Load<Shader>("SamuraiOcclusionForegroundComposite");
                if (!shader || !shader.isSupported) return;
                composite = CoreUtils.CreateEngineMaterial(shader);
                foreground = new ForegroundPass(composite, draws);
            }

            draws.Clear();
            foreach (GroupState state in groups)
            {
                float fade = Mathf.SmoothStep(0f, 1f, state.progress);
                if (fade <= .0001f) continue;
                bool hide = state.group.hideWhenOccluding;
                foreach (var surface in state.group.surfaces)
                    BindSurface(surface, hide ? -fade * HiddenFade : fade, !hide || state.progress < 1f);
            }
            if (overrideCount == 0) return;
            Vector3 player = orbit.target.position;
            Vector3 forward = camera.transform.forward;
            float farDepth = float.NegativeInfinity;
            foreach (Vector3 sample in BodySamples)
                farDepth = Mathf.Max(farDepth, Vector3.Dot(
                    player + Vector3.up * sample.y + camera.transform.right * sample.x - camera.transform.position, forward));
            Shader.SetGlobalVector(PlayerId, new Vector4(player.x, player.y, player.z, farDepth + .2f));
            Shader.SetGlobalFloat(EnabledId, 1f);
            if (draws.Count != 0)
            {
                draws.Sort((a, b) => a.material.renderQueue.CompareTo(b.material.renderQueue));
                renderer.EnqueuePass(foreground);
            }
        }

        void UpdateGroups(double now, float delta)
        {
            Vector3 origin = camera.transform.position;
            Vector3 player = orbit.target.position;
            Vector3 right = camera.transform.right;
            var bodyBounds = new Bounds(origin, Vector3.zero);
            for (int i = 0; i < BodySamples.Length; i++)
            {
                Vector3 sample = BodySamples[i];
                Vector3 point = player + Vector3.up * sample.y + right * sample.x;
                visualTargets[i] = Vector3.MoveTowards(point, origin, .08f);
                bodyBounds.Encapsulate(visualTargets[i]);
                Probe(origin, point, now, false);
            }
            // A leaf close to the lens can cover much of the frame even if the
            // player's torso is already visible. Only light decoration uses
            // these shorter, wider probes.
            Bounds decorBounds = bodyBounds;
            float decorDepth = Mathf.Min(NearDecorationDepth, Vector3.Dot(player - origin, camera.transform.forward));
            int decorCount = BodySamples.Length;
            if (decorDepth > camera.nearClipPlane)
                foreach (Vector2 sample in DecorationViewportSamples)
                {
                    Vector3 point = camera.ViewportToWorldPoint(new Vector3(sample.x, sample.y, decorDepth));
                    visualTargets[decorCount++] = point;
                    decorBounds.Encapsulate(point);
                }
            foreach (SamuraiOcclusionGroup group in SamuraiOcclusionGroup.VisualGroups)
                if (group && group.gameObject.scene == camera.gameObject.scene &&
                    group.IntersectsVisualSegments(origin, visualTargets, group.hideWhenOccluding ? decorCount : BodySamples.Length,
                        group.hideWhenOccluding ? decorBounds : bodyBounds, camera.cullingMask))
                    MarkOccluded(group, now);

            // The usual ray misses a solid the camera is already inside. A
            // reverse torso ray can see that solid's exit surface.
            Probe(player + Vector3.up * .94f, origin, now, true);

            for (int i = groups.Count - 1; i >= 0; --i)
            {
                GroupState state = groups[i];
                if (!state.group || !state.group.isActiveAndEnabled)
                {
                    groups.RemoveAt(i);
                    continue;
                }
                bool occluded = now - state.lastHit <= ClearHoldSeconds;
                state.progress = Mathf.MoveTowards(state.progress, occluded ? 1f : 0f,
                    delta / (occluded ? FadeInSeconds : RestoreSeconds));
                if (!occluded && state.progress == 0f)
                    groups.RemoveAt(i);
            }
        }

        void Probe(Vector3 from, Vector3 to, double now, bool cameraInside)
        {
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length <= .08f) return;
            Vector3 direction = delta / length;
            float range = length - (cameraInside ? 0f : .08f);
            if (!SamuraiOcclusionGroup.HasColliderProbes) return;
            int count = Physics.RaycastNonAlloc(from, direction, hits, range, ~0, QueryTriggerInteraction.Ignore);
            RaycastHit[] result = hits;
            if (count == hits.Length)
            {
                result = Physics.RaycastAll(from, direction, range, ~0, QueryTriggerInteraction.Ignore);
                count = result.Length;
            }
            for (int i = 0; i < count; i++)
            {
                Collider collider = result[i].collider;
                if (!SamuraiOcclusionGroup.TryFind(collider, out var group) ||
                    group.gameObject.scene != camera.gameObject.scene ||
                    (cameraInside && !collider.bounds.Contains(to)))
                    continue;

                MarkOccluded(group, now);
            }
        }

        void MarkOccluded(SamuraiOcclusionGroup group, double now)
        {
            foreach (GroupState state in groups)
                if (state.group == group)
                {
                    state.lastHit = now;
                    return;
                }
            groups.Add(new GroupState { group = group, lastHit = now });
        }

        void BindSurface(SamuraiOcclusionGroup.Surface surface, float fade, bool drawForeground)
        {
            Renderer renderer = surface.renderer;
            if (!renderer || !renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy ||
                (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0)
                return;
            renderer.GetSharedMaterials(shared);
            int first = surface.materialIndex < 0 ? 0 : surface.materialIndex;
            int end = surface.materialIndex < 0 ? shared.Count : Mathf.Min(first + 1, shared.Count);
            for (int i = first; i < end; i++)
            {
                Material material = shared[i];
                if (!material) continue;
                int pass = material.FindPass("SamuraiOcclusionForeground");
                if (pass < 0) continue;

                if (overrideCount == overrides.Count)
                    overrides.Add(new SurfaceOverride());
                SurfaceOverride entry = overrides[overrideCount++];
                entry.renderer = renderer;
                entry.index = i;
                renderer.GetPropertyBlock(entry.original, i);
                if (entry.original.isEmpty) renderer.GetPropertyBlock(working);
                else renderer.GetPropertyBlock(working, i);
                working.SetFloat(FadeId, fade);
                renderer.SetPropertyBlock(working, i);
                if (drawForeground)
                    draws.Add(new Draw { renderer = renderer, material = material, index = i, pass = pass });
            }
        }

        void EndCamera(ScriptableRenderContext context, Camera renderingCamera)
        {
            if (renderingCamera == camera)
                RestoreSurfaces();
        }

        void RestoreSurfaces()
        {
            if (overrideCount == 0) return;
            Shader.SetGlobalFloat(EnabledId, 0f);
            for (int i = overrideCount - 1; i >= 0; --i)
            {
                SurfaceOverride entry = overrides[i];
                if (entry.renderer)
                    entry.renderer.SetPropertyBlock(entry.original.isEmpty ? null : entry.original, entry.index);
                entry.renderer = null;
            }
            overrideCount = 0;
        }

        sealed class ForegroundPass : ScriptableRenderPass
        {
            static readonly int ForegroundColorId = Shader.PropertyToID("_SamuraiForegroundColor");
            readonly Material material;
            readonly List<Draw> draws;
            readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();

            sealed class CaptureData
            {
                public List<Draw> draws;
            }

            sealed class CompositeData
            {
                public TextureHandle foregroundColor;
                public Material material;
                public MaterialPropertyBlock properties;
            }

            public ForegroundPass(Material material, List<Draw> draws)
            {
                this.material = material;
                this.draws = draws;
                // Blend after the scene's screen ink and before post-processing.
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing + 1;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                TextureHandle output = resources.activeColorTexture;
                TextureHandle sceneDepth = resources.cameraDepthTexture;
                if (!output.IsValid() || !sceneDepth.IsValid()) return;

                var cameraData = frameData.Get<UniversalCameraData>();
                var desc = new TextureDesc(cameraData.cameraTargetDescriptor)
                {
                    name = "Samurai translucent foreground",
                    colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                    msaaSamples = MSAASamples.None,
                    bindTextureMS = false,
                    clearBuffer = true,
                    clearColor = Color.clear,
                    memoryless = RenderTextureMemoryless.None
                };
                TextureHandle foregroundColor = graph.CreateTexture(desc);
                desc.name = "Samurai foreground depth";
                desc.depthBufferBits = DepthBits.Depth32;
                TextureHandle foregroundDepth = graph.CreateTexture(desc);

                using (var builder = graph.AddRasterRenderPass<CaptureData>("Samurai capture foreground", out var data))
                {
                    data.draws = draws;
                    builder.UseAllGlobalTextures(true);
                    builder.UseTexture(sceneDepth, AccessFlags.Read);
                    builder.SetRenderAttachment(foregroundColor, 0, AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(foregroundDepth, AccessFlags.Write);
                    builder.SetRenderFunc(static (CaptureData data, RasterGraphContext context) =>
                    {
                        foreach (Draw draw in data.draws)
                            context.cmd.DrawRenderer(draw.renderer, draw.material, draw.index, draw.pass);
                    });
                }

                using (var builder = graph.AddRasterRenderPass<CompositeData>("Samurai blend foreground", out var data))
                {
                    data.foregroundColor = foregroundColor;
                    data.material = material;
                    data.properties = properties;
                    builder.UseTexture(foregroundColor, AccessFlags.Read);
                    builder.SetRenderAttachment(output, 0, AccessFlags.ReadWrite);
                    builder.SetRenderFunc(static (CompositeData data, RasterGraphContext context) =>
                    {
                        data.properties.SetTexture(ForegroundColorId, data.foregroundColor);
                        context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0,
                            MeshTopology.Triangles, 3, 1, data.properties);
                    });
                }
            }
        }
    }
}
