using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OCDesign.LookDev.Editor
{
    /// <summary>Scene-local sculpted clouds. Source actors and shared rendering settings are untouched.</summary>
    public static class ComicCloudSceneSetup
    {
        public const string Root = ComicLightingSceneBuilder.Root;
        public const string CloudRootName = "07  Sculpted 3D clouds - flat paint";

        [MenuItem("OC Design/LookDev/Legacy/Install Sculpted Mesh Clouds")]
        public static string Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != ComicLightingSceneBuilder.ScenePath)
                throw new InvalidOperationException("Open the comic lighting sample in Edit mode first.");
            var cloudShader = Shader.Find("OC Design/Comic Lighting/Cloud Sculpture");
            var backdropShader = Shader.Find("OC Design/Comic Lighting/Cloud Backdrop");
            if (!cloudShader || !backdropShader) throw new InvalidOperationException("Import cloud shaders first.");
            Directory.CreateDirectory(Root + "/Meshes/Clouds");
            AssetDatabase.Refresh();
            var material = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/CloudSculpture.mat");
            if (!material)
            {
                material = new Material(cloudShader) { name = "Tower clouds - cool blue / amber rim" };
                material.SetColor("_ShadeColor", Hex("3A526D"));
                material.SetColor("_MidColor", Hex("5E7289"));
                material.SetColor("_LitColor", Hex("8C9BA8"));
                // The HDR color property is stored in linear space.
                material.SetColor("_HighlightColor", Hex("FFAF72").linear);
                material.SetFloat("_ShadeStep", .24f);
                material.SetFloat("_LitStep", .68f);
                material.SetFloat("_HighlightStep", .84f);
                material.SetFloat("_BandSoftness", .065f);
                material.SetFloat("_NormalVariation", .20f);
                material.SetFloat("_UpInfluence", .04f);
                material.SetFloat("_LightYawOffset", -100f);
                material.SetFloat("_LightContrast", 1.15f);
                material.SetFloat("_VolumeVariation", .25f);
                material.SetFloat("_BrushScale", 1.1f);
                material.SetFloat("_BrushStrength", .12f);
                material.SetFloat("_ShadeBrushStrength", .25f);
                material.SetFloat("_AOIntensity", .65f);
                material.SetFloat("_WarmRimStrength", .82f);
                material.SetFloat("_WarmRimWidth", .74f);
                material.SetFloat("_WarmRimPower", 2.4f);
                material.SetFloat("_WarmLightThreshold", .07f);
                material.SetFloat("_EdgeBreakup", .35f);
                material.SetVector("_CloudEnvelope", new Vector4(16,27,14,2));
                material.SetVector("_CrestHeightRange", new Vector4(2,7,0,0));
                AssetDatabase.CreateAsset(material, Root + "/Materials/CloudSculpture.mat");
            }
            var backdrop = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/CloudBackdrop.mat");
            if (!backdrop)
            {
                backdrop = new Material(backdropShader) { name = "Cloud Sculpture Backdrop" };
                backdrop.SetColor("_ZenithColor", Hex("173F59"));
                backdrop.SetColor("_HorizonColor", Hex("5D8299"));
                backdrop.SetColor("_LowerColor", Hex("355B73"));
                backdrop.SetColor("_GoldColor", Hex("C99875"));
                backdrop.SetFloat("_OpeningStrength", .06f);
                AssetDatabase.CreateAsset(backdrop, Root + "/Materials/CloudBackdrop.mat");
            }

            GameObject root = null;
            foreach (var candidate in scene.GetRootGameObjects())
                if (candidate.name == CloudRootName) { root = candidate; break; }
            if (!root)
            {
                var meshes = new Mesh[4];
                for (int i = 0; i < meshes.Length; i++)
                {
                    string path = Root + "/Meshes/Clouds/CloudSculpture_" + (i + 1) + ".asset";
                    meshes[i] = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (!meshes[i])
                    {
                        meshes[i] = ComicCloudMeshFactory.Create(41 + i * 37);
                        AssetDatabase.CreateAsset(meshes[i], path);
                    }
                }
                root = new GameObject(CloudRootName);
                Undo.RegisterCreatedObjectUndo(root, "Add sculpted cloud groups");
                // A connected rising cloud wall. High central towers and broader
                // low shoulders remain real geometry, with no extra fog layers.
                float[] azimuths = { -66, -43, -20, 24, 67, 112, 153, 194, 234 };
                float[] heights = { -4, 30, 3, -20, -2, -24, 8, -30, -10 };
                Vector3[] scales = {
                    new Vector3(2.2f,2.1f,1.9f), new Vector3(2.6f,1.9f,2.0f),
                    new Vector3(2.2f,2.0f,2.0f), new Vector3(2.1f,2.0f,1.9f),
                    new Vector3(2.1f,2.2f,2.0f), new Vector3(2.2f,2.1f,1.9f),
                    new Vector3(2.1f,2.3f,2.0f), new Vector3(2.2f,2.1f,1.9f),
                    new Vector3(2.1f,2.0f,2.0f)
                };
                for (int i = 0; i < azimuths.Length; i++)
                {
                    float azimuth = azimuths[i] * Mathf.Deg2Rad;
                    float radius = 112f + (i % 3) * 2f;
                    var go = new GameObject("Cloud sculpture " + (i + 1).ToString("00"));
                    go.transform.SetParent(root.transform, false);
                    go.transform.localPosition = new Vector3(Mathf.Sin(azimuth)*radius, heights[i], Mathf.Cos(azimuth)*radius);
                    go.transform.localRotation = Quaternion.Euler(0, azimuths[i] + (i % 2 == 0 ? 9 : -13), (i % 3 - 1) * 4);
                    go.transform.localScale = scales[i];
                    go.AddComponent<MeshFilter>().sharedMesh = meshes[i % meshes.Length];
                    var renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }
            }
            var camera = GameObject.Find("04  Preview Camera").GetComponent<Camera>();
            Undo.RecordObject(camera, "Use sculpted cloud background");
            camera.clearFlags = CameraClearFlags.Skybox;
            RenderSettings.skybox = backdrop;
            EditorSceneManager.MarkSceneDirty(scene);
            return "Sculpted clouds ready: " + root.transform.childCount + " independent mesh groups.";
        }

        static Color Hex(string value)
        {
            Color color;
            ColorUtility.TryParseHtmlString("#" + value, out color);
            return color;
        }
    }
}
