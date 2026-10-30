using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OCDesign.LookDev.Editor
{
    public static class ComicCloudVolumeSetup
    {
        const string Root = ComicLightingSceneBuilder.Root;
        const string DensityRoot = "Assets/KatanaDemo/Rendering/ComicLighting/Textures/CloudDensity";
        public const string CloudRootName = "08  Painted density cloud wall";

        [MenuItem("OC Design/LookDev/Use Painted Density Cloud Wall")]
        public static string Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != ComicLightingSceneBuilder.ScenePath)
                throw new InvalidOperationException("Open the comic lighting sample in Edit mode first.");
            var shader = Shader.Find("OC Design/Comic Lighting/Cloud Volume");
            if (!shader) throw new InvalidOperationException("Import the density cloud shader first.");
            Directory.CreateDirectory(DensityRoot);
            AssetDatabase.Refresh();
            var root = FindRoot(scene, CloudRootName);
            if (!root)
            {
                Mesh cube = AssetDatabase.LoadAssetAtPath<Mesh>(Root + "/Meshes/Block.asset");
                if (!cube) throw new InvalidOperationException("Sample unit box mesh is missing.");
                root = new GameObject(CloudRootName);
                Undo.RegisterCreatedObjectUndo(root, "Add painted density cloud wall");
                int[] seeds = {41, 78, 115};
                float[] azimuths = {-66, -43, -20};
                float[] heights = {-2, 54, 0};
                Vector3[] sizes = {new Vector3(132,112,108),new Vector3(185,195,130),new Vector3(136,115,110)};
                for (int i = 0; i < 3; i++)
                {
                    string texPath = DensityRoot + "/CloudDensity_" + seeds[i] + ".asset";
                    var density = AssetDatabase.LoadAssetAtPath<Texture3D>(texPath);
                    if (!density)
                    {
                        density = ComicCloudDensityFactory.Create(seeds[i]);
                        density.name = "Sculpted density " + seeds[i];
                        AssetDatabase.CreateAsset(density, texPath);
                    }
                    string matPath = Root + "/Materials/CloudVolume_" + seeds[i] + ".mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (!material)
                    {
                        material = new Material(shader) { name = "Painted density cloud " + seeds[i] };
                        material.SetTexture("_DensityMap", density);
                        material.SetColor("_ShadeColor", Hex("334D68"));
                        material.SetColor("_MidColor", Hex("657B92"));
                        material.SetColor("_LitColor", Hex("A5AFB9"));
                        material.SetColor("_WarmColor", Hex("FFAF72").linear);
                        material.SetFloat("_DensityScale", 1.2f);
                        material.SetFloat("_Extinction", .42f);
                        material.SetFloat("_LightExtinction", .095f);
                        material.SetFloat("_Steps", 160);
                        material.SetFloat("_LightSteps", 10);
                        material.SetFloat("_LightYawOffset", -100);
                        material.SetFloat("_WarmStrength", 1.15f);
                        material.SetFloat("_BrushStrength", .10f);
                        material.SetFloat("_BandStrength", .22f);
                        material.SetFloat("_DensityEdgeSharpness", 2f);
                        material.SetFloat("_ErosionStrength", .16f);
                        material.SetFloat("_ErosionScale", 24f);
                        material.SetFloat("_SurfaceBrushStrength", .08f);
                        AssetDatabase.CreateAsset(material, matPath);
                    }
                    var go = new GameObject("Density cloud " + (i+1));
                    go.transform.SetParent(root.transform, false);
                    float azimuth = azimuths[i]*Mathf.Deg2Rad;
                    go.transform.localPosition = new Vector3(Mathf.Sin(azimuth)*111, heights[i], Mathf.Cos(azimuth)*111);
                    go.transform.localRotation = Quaternion.Euler(0,azimuths[i]+(i==1?-13:9),0);
                    go.transform.localScale = sizes[i];
                    go.AddComponent<MeshFilter>().sharedMesh = cube;
                    var renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }
            }
            // This menu explicitly switches sky implementations; normal scene open
            // never invokes it or resets material and transform art direction.
            root.SetActive(true);
            var old = FindRoot(scene, ComicCloudSceneSetup.CloudRootName);
            if (old) old.SetActive(false);
            string backdropPath = Root + "/Materials/CloudBackdrop.mat";
            var backdrop = AssetDatabase.LoadAssetAtPath<Material>(backdropPath);
            if (!backdrop)
            {
                var backdropShader = Shader.Find("OC Design/Comic Lighting/Cloud Backdrop");
                if (!backdropShader) throw new InvalidOperationException("Import the cloud backdrop shader first.");
                backdrop = new Material(backdropShader) { name = "CloudBackdrop" };
                backdrop.SetColor("_ZenithColor", Hex("173F59"));
                backdrop.SetColor("_HorizonColor", Hex("5D8299"));
                AssetDatabase.CreateAsset(backdrop, backdropPath);
            }
            RenderSettings.skybox = backdrop;
            var camera = GameObject.Find("04  Preview Camera").GetComponent<Camera>();
            camera.farClipPlane = Mathf.Max(camera.farClipPlane, 350f);
            EditorSceneManager.MarkSceneDirty(scene);
            return "Painted cloud wall ready: " + root.transform.childCount + " density volumes.";
        }

        static GameObject FindRoot(Scene scene, string name)
        {
            foreach(var go in scene.GetRootGameObjects()) if(go.name == name) return go;
            return null;
        }
        static Color Hex(string hex)
        {
            Color c;
            ColorUtility.TryParseHtmlString("#" + hex, out c);
            return c;
        }
    }
}
