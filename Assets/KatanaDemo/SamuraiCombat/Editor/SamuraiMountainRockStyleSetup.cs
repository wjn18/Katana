using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.Editor
{
    /// <summary>Continuous rock beds and slope-based vegetation, independent of face material IDs.</summary>
    public static class SamuraiMountainRockStyleSetup
    {
        const string ShaderName = "OC Design/Comic Lighting/Stratified Mountain Rock";

        [MenuItem("OC Design/Samurai Combat/Refine Mountain Rock Materials")]
        public static void Install()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != SamuraiComicStyleSetup.ScenePath)
                throw new InvalidOperationException("Open SamuraiCombatDemo in Edit mode first.");
            var dressing = scene.GetRootGameObjects().FirstOrDefault(g => g.name == SamuraiCourtyardDecorationSetup.RootName);
            var root = dressing ? dressing.transform.Find(SamuraiMountainCourtyardSetup.RootName) : null;
            if (!root) throw new InvalidOperationException("Install the mountain environment first.");
            int changed = Apply(root);
            Debug.Log($"Mountain rock style: {changed} surfaces updated. Existing rock material tuning preserved.");
        }

        public static int Apply(Transform root)
        {
            var shader = Shader.Find(ShaderName);
            if (!shader || !shader.isSupported) throw new InvalidOperationException("Import the stratified mountain shader first.");
            var near = GetMaterial("Rock_Strata_Near", shader, "9A9E96", "526C7C", "7C8B76", "4D655F", .60f, .32f, .04f, .06f, .08f, .065f);
            var middle = GetMaterial("Rock_Strata_Middle", shader, "83999F", "607C8D", "738F80", "58776E", .52f, .15f, .06f, .026f, .055f, .025f);
            var far = GetMaterial("Rock_Strata_Far", shader, "9CB3C1", "839EAF", "8CA5A3", "799791", .30f, -.08f, .10f, .012f, 0, .01f);
            int changed = 0;
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.transform.parent.GetComponent<Renderer>()) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh) continue;
                string name = filter.sharedMesh.name;
                Material material = name.EndsWith("_Crag") || name.StartsWith("Boulder_") || name.StartsWith("Landscape_0_") ? near :
                    name.StartsWith("Landscape_1_") ? middle : name.StartsWith("Landscape_2_") ? far : null;
                if (!material) continue;
                var wanted = Enumerable.Repeat(material, filter.sharedMesh.subMeshCount).ToArray();
                if (renderer.sharedMaterials.SequenceEqual(wanted)) continue;
                Undo.RecordObject(renderer, "Apply continuous mountain rock style");
                renderer.sharedMaterials = wanted;
                changed++;
            }
            if (changed > 0) EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            return changed;
        }

        static Material GetMaterial(string name, Shader shader, string lit, string shade, string moss, string mossShade,
            float coverage, float threshold, float feather, float strata, float seams, float erosion)
        {
            string path = SamuraiMountainCourtyardSetup.Root + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material) return material;
            material = new Material(shader) { name = name, enableInstancing = true };
            material.SetColor("_BaseColor", Hex(lit)); material.SetColor("_ShadowColor", Hex(shade));
            material.SetColor("_MossColor", Hex(moss)); material.SetColor("_MossShadowColor", Hex(mossShade));
            material.SetFloat("_MossAmount", coverage); material.SetFloat("_MossSlope", .74f); material.SetFloat("_MossFade", .18f);
            material.SetFloat("_Threshold", threshold); material.SetFloat("_Feather", feather);
            material.SetFloat("_StrataScale", .11f); material.SetFloat("_StrataStrength", strata);
            material.SetFloat("_SeamStrength", seams); material.SetFloat("_ErosionStrength", erosion);
            material.SetFloat("_PaperStrength", .008f); material.SetFloat("_PaperScale", .12f);
            material.SetFloat("_ShadowStrength", name.EndsWith("Far") ? 0 : 1);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static Color Hex(string value) { Color result; ColorUtility.TryParseHtmlString("#" + value, out result); return result; }
    }
}
