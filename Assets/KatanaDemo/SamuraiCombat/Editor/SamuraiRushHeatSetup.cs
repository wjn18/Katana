using System;
using System.Linq;
using OCDesign.Samurai;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace OCDesign.EditorTools
{
    /// <summary>只安装当前桌面 Renderer 上的按相机触发热浪，不重建战斗资源。</summary>
    public static class SamuraiRushHeatSetup
    {
        const string RendererPath = "Assets/Settings/PC_Renderer.asset";
        const string MaterialPath = SamuraiCombatSetup.Root + "/VFX/SamuraiRushHeat.mat";

        [MenuItem("OC Design/Samurai/Install Rush Heat")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play。");
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            var shader = Shader.Find("KatanaDemo/VFX/SamuraiRushHeat");
            if (renderer == null || shader == null || !shader.isSupported)
                throw new InvalidOperationException("缺少桌面 URP Renderer 或热扭曲 Shader 尚未编译。");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "SamuraiRushHeat" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            var feature = renderer.rendererFeatures.OfType<SamuraiRushHeatRendererFeature>().FirstOrDefault();
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<SamuraiRushHeatRendererFeature>();
                feature.name = "Samurai Rush Heat";
                AssetDatabase.AddObjectToAsset(feature, renderer);
                renderer.rendererFeatures.Add(feature);
            }
            feature.Configure(material);
            feature.SetActive(true);
            // 与 URP 自带 Inspector 一样维护子资产映射，避免重载时凭旧表恢复引用。
            var serializedRenderer = new SerializedObject(renderer);
            var featureMap = serializedRenderer.FindProperty("m_RendererFeatureMap");
            featureMap.arraySize = renderer.rendererFeatures.Count;
            for (int i = 0; i < renderer.rendererFeatures.Count; i++)
            {
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(renderer.rendererFeatures[i],
                    out string guid, out long localId))
                    throw new InvalidOperationException("无法解析 Renderer Feature 的子资产 ID。");
                featureMap.GetArrayElementAtIndex(i).longValue = localId;
            }
            serializedRenderer.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(renderer);
            renderer.SetDirty();
            AssetDatabase.SaveAssetIfDirty(renderer);
            AssetDatabase.SaveAssetIfDirty(material);
        }
    }
}
