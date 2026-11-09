using System;
using System.IO;
using System.Linq;
using OCDesign.LookDev;
using OCDesign.LookDev.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.Editor
{
    /// <summary>Installs comic presentation on the existing combat scene; never rebuilds gameplay.</summary>
    public static class SamuraiComicStyleSetup
    {
        public const string ScenePath = "Assets/KatanaDemo/SamuraiCombat/Scenes/SamuraiCombatDemo.unity";
        public const string Root = "Assets/KatanaDemo/SamuraiCombat/Presentation/Comic";
        public const string Shared = "Assets/KatanaDemo/Rendering/ComicLighting";
        const string Sample = "Assets/KatanaDemo/LookDev/ComicLighting";
        const string RootName = "Comic presentation";
        const string HullName = "Comic ink contour";

        [MenuItem("Katana/Level/Install Comic Presentation In Current Scene")]
        public static string Install()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != ScenePath)
                throw new InvalidOperationException("Open SamuraiCombatDemo in Edit mode first.");
            if (!Shader.Find("OC Design/Comic Lighting/Surface"))
                throw new InvalidOperationException("Import the shared comic shaders first.");
            foreach (string folder in new[] {"Materials", "Meshes/OutlineHulls"})
                Directory.CreateDirectory(Root + "/" + folder);
            AssetDatabase.Refresh();
            GameObject presentation = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            bool firstInstall = !presentation;
            if (firstInstall)
            {
                presentation = new GameObject(RootName);
                Undo.RegisterCreatedObjectUndo(presentation, "Install comic presentation");
            }

            Material architectureInk = CopyMaterial("Ink_Architecture", "Ink_Architecture");
            Material actorInk = CopyMaterial("Ink", "Ink");
            Transform dressing = scene.GetRootGameObjects().FirstOrDefault(g => g.name == SamuraiCourtyardDecorationSetup.RootName)?.transform;
            Renderer[] renderers = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true))
                .Where(r => (r is MeshRenderer || r is SkinnedMeshRenderer)
                    && !r.GetComponent<ComicContourFollower>() && !r.transform.IsChildOf(presentation.transform)
                    && (!dressing || !r.transform.IsChildOf(dressing))
                    && !r.sharedMaterials.Any(m => m && m.shader && m.shader.name == "OC Design/Comic Lighting/Outline")).ToArray();
            int surfaces = 0, contours = 0;
            foreach (Renderer renderer in renderers)
            {
                Mesh mesh = SourceMesh(renderer);
                if (!mesh) continue;
                bool architecture = renderer.transform.root.name == "Floor";
                bool grid = architecture && renderer.name.StartsWith("Grid ", StringComparison.Ordinal);
                var before = renderer.sharedMaterials;
                var after = before.Select(m => Palette(m, architecture, grid)).ToArray();
                if (!before.SequenceEqual(after))
                {
                    Undo.RecordObject(renderer, "Apply scene comic palette");
                    renderer.sharedMaterials = after;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                    surfaces++;
                }
                if (grid) continue;
                Transform existing = renderer.transform.Find(HullName);
                if (existing) continue; // Preserve contour visibility, mesh and material hand edits.
                var go = new GameObject(HullName);
                Undo.RegisterCreatedObjectUndo(go, "Add comic contour");
                go.transform.SetParent(renderer.transform, false);
                go.layer = renderer.gameObject.layer;
                ComicOutlineHullFactory.Diagnostics diagnostics;
                Mesh hull = ComicOutlineHullFactory.GetOrCreate(mesh, Root + "/Meshes/OutlineHulls", out diagnostics);
                Renderer edge;
                if (renderer is SkinnedMeshRenderer skin)
                {
                    var outline = go.AddComponent<SkinnedMeshRenderer>();
                    outline.sharedMesh = hull;
                    outline.bones = skin.bones;
                    outline.rootBone = skin.rootBone;
                    outline.localBounds = skin.localBounds;
                    outline.quality = skin.quality;
                    outline.updateWhenOffscreen = skin.updateWhenOffscreen;
                    edge = outline;
                }
                else
                {
                    go.AddComponent<MeshFilter>().sharedMesh = hull;
                    edge = go.AddComponent<MeshRenderer>();
                }
                edge.sharedMaterials = Enumerable.Repeat(architecture ? architectureInk : actorInk, mesh.subMeshCount).ToArray();
                edge.shadowCastingMode = ShadowCastingMode.Off;
                edge.receiveShadows = false;
                edge.lightProbeUsage = LightProbeUsage.Off;
                edge.reflectionProbeUsage = ReflectionProbeUsage.Off;
                var follower = go.AddComponent<ComicContourFollower>();
                follower.source = renderer;
                follower.contour = edge;
                follower.Synchronize();
                contours++;
            }

            Camera camera = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true))
                .FirstOrDefault(c => c.CompareTag("MainCamera"));
            if (!camera) throw new InvalidOperationException("Combat camera is missing.");
            var screen = camera.GetComponent<ComicScreenOutline>();
            bool addedScreen = !screen;
            if (!screen)
            {
                screen = Undo.AddComponent<ComicScreenOutline>(camera.gameObject);
                screen.viewCamera = camera;
                screen.restrictToPreviewScene = false;
                screen.screenOutlineShader = AssetDatabase.LoadAssetAtPath<Shader>(Shared + "/Shaders/ComicScreenOutline.shader");
                screen.minimumPixels = actorInk.GetFloat("_MinOutlinePixels");
                screen.outlineColor = actorInk.GetColor("_OutlineColor");
            }
            screen.RefreshSources();
            if (firstInstall)
            {
                Undo.RecordObject(camera, "Use comic sky and color grading");
                camera.clearFlags = CameraClearFlags.Skybox;
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, 500f);
                var data = camera.GetComponent<UniversalAdditionalCameraData>();
                if (!data) data = Undo.AddComponent<UniversalAdditionalCameraData>(camera.gameObject);
                Undo.RecordObject(data, "Use comic camera grading");
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.High;
                RenderSettings.skybox = CopyMaterial("CloudBackdrop", "CloudBackdrop");
                RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Hex("738395");
                RenderSettings.ambientIntensity = .3f;
                RenderSettings.reflectionIntensity = 0;
                InstallGrading(presentation.transform);
                InstallClouds(presentation.transform);
            }
            if (firstInstall || surfaces > 0 || contours > 0 || addedScreen)
                EditorSceneManager.MarkSceneDirty(scene);
            return "Comic combat presentation: " + surfaces + " surfaces updated, " + contours + " contours added.";
        }

        static Mesh SourceMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skin) return skin.sharedMesh;
            var filter = renderer.GetComponent<MeshFilter>();
            return filter ? filter.sharedMesh : null;
        }

        static Material Palette(Material source, bool architecture, bool grid)
        {
            if (!source) return source;
            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (sourcePath.StartsWith(Root + "/", StringComparison.Ordinal)) return source;
            if (source.shader && source.shader.name == "OC Design/Comic Lighting/Surface") return source;
            if (architecture)
            {
                string palette = grid ? "Practice_grid" : source.name == "TrainingStone" ? "Warm_paving" : "Indigo_stone";
                string path = Root + "/Materials/" + palette + ".mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (existing) return existing;
                var material = new Material(Shader.Find("OC Design/Comic Lighting/Surface")) { name = palette };
                material.SetColor("_BaseColor", Hex(grid ? "B4B5B4" : palette == "Warm_paving" ? "E7DDDA" : "738A98"));
                material.SetColor("_ShadowColor", Hex(grid ? "536F87" : palette == "Warm_paving" ? "476780" : "344F6A"));
                material.SetFloat("_Threshold", .16f);
                material.SetFloat("_Feather", .018f);
                material.SetFloat("_PaperStrength", grid ? 0f : .035f);
                if (grid)
                {
                    // Keep the old practice objects and transforms, but replace
                    // their subpixel geometry with filtered paving in the floor shader.
                    var color = material.GetColor("_BaseColor");
                    color.a = 0;
                    material.SetColor("_BaseColor", color);
                    material.SetFloat("_AlphaClip", 1);
                    material.EnableKeyword("_ALPHATEST_ON");
                }
                else if (palette == "Warm_paving") material.SetFloat("_PavingStrength", 1);
                AssetDatabase.CreateAsset(material, path);
                return material;
            }
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId);
            string actorPath = Root + "/Materials/Actor_" + guid + "_" + localId + ".mat";
            var actor = AssetDatabase.LoadAssetAtPath<Material>(actorPath);
            if (actor) return actor;
            actor = new Material(Shader.Find("OC Design/Comic Lighting/Surface")) { name = source.name + " Comic" };
            Texture tex = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : source.mainTexture;
            Color tint = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : Color.white;
            actor.SetTexture("_BaseMap", tex);
            if (source.HasProperty("_BaseMap"))
            {
                actor.SetTextureScale("_BaseMap", source.GetTextureScale("_BaseMap"));
                actor.SetTextureOffset("_BaseMap", source.GetTextureOffset("_BaseMap"));
            }
            actor.SetColor("_BaseColor", tint * Hex("FFF3DC"));
            actor.SetColor("_ShadowColor", tint * Hex("718EA8"));
            actor.SetFloat("_TextureStrength", tex ? 1 : 0);
            actor.SetFloat("_PaperStrength", 0);
            actor.SetFloat("_Threshold", .12f);
            actor.SetFloat("_Feather", .028f);
            if (source.HasProperty("_Cull")) actor.SetFloat("_Cull", source.GetFloat("_Cull"));
            if (source.IsKeywordEnabled("_ALPHATEST_ON"))
            {
                actor.EnableKeyword("_ALPHATEST_ON");
                actor.SetFloat("_AlphaClip", 1);
                if (source.HasProperty("_Cutoff")) actor.SetFloat("_Cutoff", source.GetFloat("_Cutoff"));
            }
            AssetDatabase.CreateAsset(actor, actorPath);
            return actor;
        }

        static Material CopyMaterial(string templateName, string name)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material) return material;
            var template = AssetDatabase.LoadAssetAtPath<Material>(Sample + "/Materials/" + templateName + ".mat");
            if (!template) throw new InvalidOperationException("Missing palette template: " + templateName);
            material = new Material(template) { name = name };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void InstallGrading(Transform parent)
        {
            string path = Root + "/Materials/ComicVolume.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (!profile)
            {
                if (!AssetDatabase.CopyAsset(Sample + "/Materials/ComicVolume.asset", path))
                    throw new InvalidOperationException("Could not create local comic grading profile.");
                profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            }
            var go = new GameObject("Comic neutral grading");
            go.transform.SetParent(parent, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100;
            volume.sharedProfile = profile;
        }

        static void InstallClouds(Transform parent)
        {
            var root = new GameObject("Painted density clouds").transform;
            root.SetParent(parent, false);
            root.localPosition = new Vector3(-12.5f, 0, 0);
            int[] seeds = {78, 115, 41, 78, 115, 41};
            Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            if (!cube)
            {
                var temporary = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube = temporary.GetComponent<MeshFilter>().sharedMesh;
                UnityEngine.Object.DestroyImmediate(temporary);
            }
            for (int i = 0; i < seeds.Length; i++)
            {
                int seed = seeds[i];
                string materialPath = Root + "/Materials/CloudVolume_" + seed + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (!material)
                {
                    material = CopyMaterial("CloudVolume_" + seed, "CloudVolume_" + seed);
                    material.SetFloat("_Steps", 128);
                    material.SetFloat("_LightSteps", 8);
                    EditorUtility.SetDirty(material);
                    AssetDatabase.SaveAssetIfDirty(material);
                }
                float angle = i * 60f;
                float azimuth = angle * Mathf.Deg2Rad;
                var go = new GameObject("Density cloud " + (i + 1));
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3(Mathf.Sin(azimuth) * 205, seed == 78 ? 54 : 5, Mathf.Cos(azimuth) * 205);
                go.transform.localRotation = Quaternion.Euler(0, angle + (seed == 78 ? -13 : 9), 0);
                go.transform.localScale = seed == 78 ? new Vector3(185, 195, 130) : new Vector3(174, 120, 108);
                go.AddComponent<MeshFilter>().sharedMesh = cube;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
        }

        static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString("#" + value, out var color);
            return color;
        }
    }
}
