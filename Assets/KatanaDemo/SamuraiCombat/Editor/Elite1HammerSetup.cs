using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OCDesign.Samurai.EditorTools
{
    /// <summary>使用双手锤包原始白模；仅改武器装配，不改Humanoid骨骼或动画。</summary>
    public static class Elite1HammerSetup
    {
        public const string SourceRig="Assets/2Handed_Hammer_Set/Animations/Humanoid/00_T-pose.FBX";
        public const string MaterialPath="Assets/KatanaDemo/Characters/Elite1/Materials/Elite1_HammerWhite.mat";

        [MenuItem("Tools/OC Design/Samurai Combat/Use Hammer Package Whitebox On Elite1")]
        public static void Install()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Exit Play before changing the Elite1 weapon.");
            var scene=EditorSceneManager.NewPreviewScene();GameObject reference=null,source=null,root=null;
            try{
                root=PrefabUtility.LoadPrefabContents(Elite1CombatSetup.PrefabPath);
                var animator=root.GetComponentInChildren<Animator>();
                reference=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Elite1HumanoidSetup.ModelPath),scene);
                PrefabUtility.UnpackPrefabInstance(reference,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                reference.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);reference.transform.localScale=Vector3.one;
                Elite1MotionBaker.Reference(reference,reference.GetComponentInChildren<SkinnedMeshRenderer>(),new Elite1CombatSetup.InstallReport());
                reference.transform.localScale=Vector3.one*animator.transform.lossyScale.x;
                source=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SourceRig),scene);
                source.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);source.transform.localScale=Vector3.one;
                var sa=source.GetComponentInChildren<Animator>();sa.runtimeAnimatorController=null;sa.Rebind();
                var da=reference.GetComponentInChildren<Animator>();
                var refHand=reference.GetComponentsInChildren<Transform>().First(t=>t.name=="LeftHand");
                var sourceHand=sa.GetBoneTransform(HumanBodyBones.LeftHand);
                var original=source.GetComponentsInChildren<MeshFilter>().First(f=>f.name=="hammerbull_wp001");
                float scale=(da.humanScale*reference.transform.lossyScale.y)/(sa.humanScale*source.transform.lossyScale.y);
                Vector3 localOffset=refHand.InverseTransformVector((original.transform.position-sourceHand.position)*scale);
                Quaternion localRotation=Quaternion.Inverse(refHand.rotation)*original.transform.rotation;
                var hand=animator.GetBoneTransform(HumanBodyBones.LeftHand);
                var grip=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="EliteHammerGrip"||t.name=="EliteCleaverGrip");
                if(grip==null){grip=new GameObject("EliteHammerGrip").transform;grip.SetParent(hand,false);}
                grip.name="EliteHammerGrip";grip.SetParent(hand,false);grip.localPosition=localOffset;grip.localRotation=localRotation;grip.localScale=Vector3.one*(scale/hand.lossyScale.x);
                var old=grip.Find("EliteCleaver");if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
                var visual=Child(grip,"EliteHammer");var filter=visual.GetComponent<MeshFilter>();if(filter==null)filter=visual.gameObject.AddComponent<MeshFilter>();filter.sharedMesh=original.sharedMesh;
                var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                if(material==null){material=new Material(original.GetComponent<MeshRenderer>().sharedMaterial.shader){name="Elite1_HammerWhite"};material.SetColor("_BaseColor",Color.white);material.SetFloat("_Metallic",0);material.SetFloat("_Smoothness",.2f);AssetDatabase.CreateAsset(material,MaterialPath);}
                var renderer=visual.GetComponent<MeshRenderer>();if(renderer==null)renderer=visual.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterials=Enumerable.Repeat(material,original.sharedMesh.subMeshCount).ToArray();
                // 沿原始锤头横向覆盖实际打击面，沿用现有武器采样端点的引用。
                var vertices=original.sharedMesh.vertices;var bounds=original.sharedMesh.bounds;var headVertices=vertices.Where(v=>v.z>=bounds.max.z-bounds.size.z*.2f).ToArray();
                var head=new Bounds(headVertices[0],Vector3.zero);foreach(var v in headVertices)head.Encapsulate(v);
                Child(grip,"BladeBase").localPosition=new Vector3(head.min.x,head.center.y,head.center.z);
                Child(grip,"BladeTip").localPosition=new Vector3(head.max.x,head.center.y,head.center.z);
                PrefabUtility.SaveAsPrefabAsset(root,Elite1CombatSetup.PrefabPath);
                var reportFolder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../镇门鬼将/动画资源/elite1-combat/analysis/hammer-knockback-20261027"));Directory.CreateDirectory(reportFolder);
                File.WriteAllText(Path.Combine(reportFolder,"锤子装配.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{source=SourceRig,mesh=original.sharedMesh.name,triangles=original.sharedMesh.triangles.Length/3,uniformWorldScale=scale,fullLengthMeters=bounds.size.z*scale,gripPosition=grip.localPosition.ToString("F6"),gripRotation=grip.localRotation.ToString("F6"),material=MaterialPath,humanoidPreserved=animator.avatar!=null&&animator.avatar.isHuman},Newtonsoft.Json.Formatting.Indented));
            }finally{if(root!=null)PrefabUtility.UnloadPrefabContents(root);if(reference!=null)UnityEngine.Object.DestroyImmediate(reference);if(source!=null)UnityEngine.Object.DestroyImmediate(source);EditorSceneManager.ClosePreviewScene(scene);}
        }
        static Transform Child(Transform parent,string name){var t=parent.Find(name);if(t==null){t=new GameObject(name).transform;t.SetParent(parent,false);}t.localPosition=Vector3.zero;t.localRotation=Quaternion.identity;t.localScale=Vector3.one;return t;}
    }
}
