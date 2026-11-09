using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace OCDesign.Samurai.EditorTools
{
    /// <summary>敌人视野、Enemy1首段前摇和两名敌人倒地贴地维护。只保存指定资产。</summary>
    public static class SamuraiEnemyReadabilitySetup
    {
        public static string ReportFolder => Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../斗笠剑士/动画资源/samurai-combat/analysis/enemy-readability-20261027"));
        public static string PrefabPath(string name) => "Assets/KatanaDemo/Characters/" + name + "/Prefabs/" + name + ".prefab";

        [MenuItem("Katana/Samurai/Enemies/Install Enemy Perception And Timing Fixes")]
        public static void Install()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Exit Play before maintaining enemy assets.");
            Directory.CreateDirectory(ReportFolder);
            foreach (string name in new[] { "Enemy1", "Elite1" })
            {
                var root = PrefabUtility.LoadPrefabContents(PrefabPath(name));
                try { if (InitializeBrain(root.GetComponent<SamuraiTrainingEnemy>())) PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(name)); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                var report = GroundDeath(name, true);
                File.WriteAllText(Path.Combine(ReportFolder, name + "-grounding.json"), JsonUtility.ToJson(report, true));
            }
            foreach (string state in new[] { "Light01", "Light02", "Heavy" })
            {
                var action = AssetDatabase.LoadAssetAtPath<SamuraiActionDefinition>(
                    "Assets/KatanaDemo/SamuraiCombat/Config/Enemy1/Enemy1_" + state + ".asset");
                if (InitializeWindup(action)) { EditorUtility.SetDirty(action); AssetDatabase.SaveAssetIfDirty(action); }
            }
        }

        public static bool InitializeBrain(SamuraiTrainingEnemy brain)
        {
            if (brain == null || brain.perceptionConfigurationVersion >= 1) return false;
            brain.horizontalViewAngle = 180f; brain.verticalViewAngle = 160f;
            brain.perceptionConfigurationVersion = 1; return true;
        }
        public static bool InitializeWindup(SamuraiActionDefinition action)
        {
            if (action == null || !action.name.StartsWith("Enemy1_") || action.windupConfigurationVersion >= 1) return false;
            action.attackWindupSpeed = .65f; action.windupConfigurationVersion = 1; return true;
        }

        [Serializable] public sealed class GroundReport
        {
            public string actor, curve; public int samples; public float duration;
            public float minimumBefore, maximumBefore, finalBefore;
            public float minimumAfter, maximumAfter, finalAfter, maximumCorrection;
            public bool changed;
        }

        // 仅修正派生死亡片段的竖直平移；所有旋转、水平根曲线和源FBX保持。
        public static GroundReport GroundDeath(string name, bool repair)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(name));
            var clip = prefab.GetComponent<SamuraiCombatant>().Profile.death;
            var binding = AnimationUtility.GetCurveBindings(clip).First(b => clip.humanMotion
                ? b.propertyName == "RootT.y" : b.propertyName == "m_LocalPosition.y" && b.path.EndsWith("/Hips"));
            var report = new GroundReport { actor = name, curve = binding.path + "/" + binding.propertyName,
                duration = clip.length, samples = Mathf.CeilToInt(clip.length * 960) + 1 };
            float[] before = SampleDeath(prefab, clip, report.samples);
            report.minimumBefore = before.Min(); report.maximumBefore = before.Max(); report.finalBefore = before.Last();
            float conversion;
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
                var animator = root.GetComponentInChildren<Animator>();
                var hips = root.GetComponentsInChildren<Transform>().First(t => t.name == "Hips");
                conversion = clip.humanMotion ? animator.humanScale * animator.transform.lossyScale.y
                    : hips.parent.TransformVector(Vector3.up).y;
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            if (Mathf.Abs(conversion) < .00001f) throw new InvalidOperationException("Invalid death height conversion.");
            float[] after = before;
            for (int iteration = 0; repair && iteration < 3; iteration++)
            {
                float maximum = after.Max(h => Mathf.Abs(.0015f - h));
                if (maximum < .0005f) break;
                var original = AnimationUtility.GetEditorCurve(clip, binding);
                var keys = new Keyframe[report.samples];
                for (int f = 0; f < keys.Length; f++)
                {
                    float time = clip.length * f / (keys.Length - 1);
                    float correction = .0015f - after[f];
                    report.maximumCorrection = Mathf.Max(report.maximumCorrection, Mathf.Abs(correction));
                    keys[f] = new Keyframe(time, original.Evaluate(time) + correction / conversion);
                }
                for (int i = 0; i < keys.Length; i++)
                {
                    var key = keys[i];
                    key.inTangent = i == 0 ? 0 : (key.value - keys[i-1].value) / (key.time - keys[i-1].time);
                    key.outTangent = i == keys.Length-1 ? 0 : (keys[i+1].value - key.value) / (keys[i+1].time - key.time);
                    keys[i] = key;
                }
                AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(keys)); report.changed = true;
                after = SampleDeath(prefab, clip, report.samples);
            }
            if (repair && after.Any(h => Mathf.Abs(h - .0015f) > .003f))
                throw new InvalidOperationException(name + " death grounding did not converge; inspect the derived height curve.");
            if (report.changed) { EditorUtility.SetDirty(clip); AssetDatabase.SaveAssetIfDirty(clip); }
            report.minimumAfter = after.Min(); report.maximumAfter = after.Max(); report.finalAfter = after.Last(); return report;
        }

        public static float[] SampleDeath(GameObject prefab, AnimationClip clip, int samples)
        {
            var scene = EditorSceneManager.NewPreviewScene(); var graph = default(PlayableGraph);
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>()) behaviour.enabled = false;
                foreach (var collider in root.GetComponentsInChildren<Collider>()) collider.enabled = false;
                var animator = root.GetComponentInChildren<Animator>(); animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.Rebind();
                graph = PlayableGraph.Create("Enemy death ground sampler"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationClipPlayable.Create(graph, clip); playable.SetApplyFootIK(false);
                AnimationPlayableOutput.Create(graph, "death", animator).SetSourcePlayable(playable); graph.Play();
                // 与实际蒙皮矩阵一致，避免BakeMesh把模型缩放再次计入世界坐标。
                var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>().Select(s => new SkinGeometry(s)).ToArray();
                var result = new float[samples];
                for (int f = 0; f < samples; f++)
                {
                    playable.SetTime(clip.length * f / (samples - 1)); graph.Evaluate(.000001f);
                    float minimum = float.PositiveInfinity;
                    foreach (var skin in skins) minimum = Mathf.Min(minimum, skin.Minimum());
                    result[f] = minimum;
                }
                return result;
            }
            finally { if (graph.IsValid()) graph.Destroy(); EditorSceneManager.ClosePreviewScene(scene); }
        }

        sealed class SkinGeometry
        {
            readonly Transform[] bones; readonly Matrix4x4[] bindposes, matrices;
            readonly Vector3[] vertices; readonly BoneWeight[] weights;
            public SkinGeometry(SkinnedMeshRenderer skin)
            {
                bones = skin.bones; bindposes = skin.sharedMesh.bindposes; vertices = skin.sharedMesh.vertices;
                weights = skin.sharedMesh.boneWeights; matrices = new Matrix4x4[bones.Length];
            }
            public float Minimum()
            {
                for (int i = 0; i < matrices.Length; i++) matrices[i] = bones[i].localToWorldMatrix * bindposes[i];
                float minimum = float.PositiveInfinity;
                for (int i = 0; i < vertices.Length; i++)
                {
                    var w = weights[i]; var v = vertices[i];
                    var p = matrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0
                        + matrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1
                        + matrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2
                        + matrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
                    minimum = Mathf.Min(minimum, p.y);
                }
                return minimum;
            }
        }
    }
}
