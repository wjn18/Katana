using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace OCDesign.Samurai.EditorTools
{
    // Isolated preview derivatives only; never assigns combat profiles or saves the user's scene.
    public static partial class SamuraiDownPoseAdaptation
    {
        public const string ElitePreviewFolder="Assets/KatanaDemo/SamuraiCombat/Animations/Execution/Elite1NaturalPreview";
        const float ElitePreviewGroundY=-.015f;
        public static readonly string ElitePreviewOutput=Path.GetFullPath(Path.Combine(Application.dataPath,"../../镇门鬼将/动画资源/elite1-execution-preview-20261102"));
        static Rig ExecutionSceneClone(string profileName,UnityEngine.SceneManagement.Scene scene,List<UnityEngine.Object> temporary)
        {
            var original=UnityEngine.Object.FindObjectsByType<OCDesign.Samurai.SamuraiCombatant>().First(c=>c.Profile!=null&&c.Profile.name==profileName&&!EditorSceneManager.IsPreviewScene(c.gameObject.scene));
            var clone=UnityEngine.Object.Instantiate(original.gameObject);clone.name=profileName+" preview";
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(clone,scene);temporary.Add(clone);
            clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            foreach(var b in clone.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;
            foreach(var c in clone.GetComponentsInChildren<Collider>(true))c.enabled=false;
            var animator=clone.GetComponentInChildren<Animator>(true);animator.runtimeAnimatorController=null;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            return new Rig {gameObject=clone,animator=animator};
        }
        static float[] EVP(Vector3 p)=>new[]{p.x,p.y,p.z};
        static void EVSave(string name,object data)=>File.WriteAllText(Path.Combine(ElitePreviewOutput,"analysis",name),Newtonsoft.Json.JsonConvert.SerializeObject(data,Newtonsoft.Json.Formatting.Indented));
        sealed class EVPose
        {
            public Vector3[] p,s;public Quaternion[] q;
            public EVPose(Transform[] bones){p=bones.Select(b=>b.localPosition).ToArray();q=bones.Select(b=>b.localRotation).ToArray();s=bones.Select(b=>b.localScale).ToArray();}
            public void Apply(Transform[] bones){for(int i=0;i<bones.Length;i++){bones[i].localPosition=p[i];bones[i].localRotation=q[i];bones[i].localScale=s[i];}}
            public static void Blend(Transform[] bones,EVPose a,EVPose b,float t){for(int i=0;i<bones.Length;i++){bones[i].localPosition=Vector3.Lerp(a.p[i],b.p[i],t);bones[i].localRotation=Quaternion.Slerp(a.q[i],b.q[i],t);bones[i].localScale=Vector3.Lerp(a.s[i],b.s[i],t);}}
        }
        sealed class EVGenericSampler
        {
            readonly Transform[] bones;readonly AnimationCurve[][] curves;
            public EVGenericSampler(Animator animator,AnimationClip clip)
            {
                var paths=AnimationUtility.GetCurveBindings(clip).Select(b=>b.path).Distinct().ToArray();bones=paths.Select(p=>p.Length==0?animator.transform:animator.transform.Find(p)).ToArray();
                curves=paths.Select(p=>TransformProperties.Select(n=>AnimationUtility.GetEditorCurve(clip,EditorCurveBinding.FloatCurve(p,typeof(Transform),n))).ToArray()).ToArray();
            }
            public void At(float t)
            {
                for(int i=0;i<bones.Length;i++){if(bones[i]==null)continue;var c=curves[i];bones[i].localPosition=new Vector3(c[0].Evaluate(t),c[1].Evaluate(t),c[2].Evaluate(t));bones[i].localRotation=new Quaternion(c[3].Evaluate(t),c[4].Evaluate(t),c[5].Evaluate(t),c[6].Evaluate(t)).normalized;bones[i].localScale=new Vector3(c[7].Evaluate(t),c[8].Evaluate(t),c[9].Evaluate(t));}
            }
        }
        static Vector3 EVHead(Animator a)
        {
            var h=a.GetBoneTransform(HumanBodyBones.Head);var n=a.GetBoneTransform(HumanBodyBones.Neck);
            return h.position+(h.position-n.position).normalized*.16f;
        }
        static AnimationClip EVBake(string name,Transform[] bones,Animator animator,List<EVPose> frames,float duration,string folder=null)
        {
            var values=Enumerable.Range(0,bones.Length*10).Select(_=>new float[frames.Count]).ToArray();var previous=new Quaternion[bones.Length];
            for(int f=0;f<frames.Count;f++)for(int b=0;b<bones.Length;b++)
            {var p=frames[f].p[b];var q=frames[f].q[b];var s=frames[f].s[b];if(f>0&&Quaternion.Dot(previous[b],q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);previous[b]=q;var v=new[]{p.x,p.y,p.z,q.x,q.y,q.z,q.w,s.x,s.y,s.z};for(int k=0;k<10;k++)values[b*10+k][f]=v[k];}
            var clip=new AnimationClip{name=name,frameRate=60,wrapMode=WrapMode.ClampForever};
            for(int b=0;b<bones.Length;b++)for(int k=0;k<10;k++)AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(bones[b],animator.transform),typeof(Transform),TransformProperties[k]),Curve(values[b*10+k],duration));
            clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=false;settings.stopTime=duration;AnimationUtility.SetAnimationClipSettings(clip,settings);
            SaveExecutionClip(clip,(folder??ElitePreviewFolder)+"/"+name+".anim");return clip;
        }
        public static string BakeEliteExecutionPreview() => BakeEliteNaturalPreview();
        public static string ProbeEliteExecution()
        {
            var scene=EditorSceneManager.NewPreviewScene();var temp=new List<UnityEngine.Object>();var rows=new List<object>();
            try
            {
                var elite=ExecutionSceneClone("Elite1",scene,temp);var player=ExecutionSceneClone("SamuraiPlayer",scene,temp);
                var skin=new SkinSampler(elite.gameObject,elite.animator);var head=elite.animator.GetBoneTransform(HumanBodyBones.Head);var hips=elite.animator.GetBoneTransform(HumanBodyBones.Hips);
                var so=new SerializedObject(player.gameObject.GetComponent<OCDesign.Samurai.SamuraiCombatant>());
                var tip=(Transform)so.FindProperty("weaponTip").objectReferenceValue;var start=(Transform)so.FindProperty("weaponBase").objectReferenceValue;
                for(int index=0;index<2;index++)
                {
                    var attack=AssetDatabase.LoadAssetAtPath<AnimationClip>(ExecutionFolder+"/Execution"+(index==0?"09600":"09702")+".anim");
                    var source=PrepareSource(AssetDatabase.LoadAllAssetsAtPath(ExecutionFolder+"/SourceFBX/"+ReceiverFiles[index]+".fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview")));temp.Add(source);
                    float strike=index==0?26f/60:37f/60,delay=index==0?.36f:.56f;
                    using(var ps=new Sampling(player.animator,attack,false))using(var es=new Sampling(elite.animator,source,false))
                    {
                        ps.At(strike);es.At(strike-delay);hips.position+=Vector3.up*(.005f-skin.Minimum(5));
                        rows.Add(new{index,eliteScale=EVP(elite.animator.transform.lossyScale),playerScale=EVP(player.animator.transform.lossyScale),head=EVP(head.position),hips=EVP(hips.position),chest=EVP(elite.animator.GetBoneTransform(HumanBodyBones.Chest).position),tip=EVP(tip.position),bladeBase=EVP(start.position),headUp=EVP(head.up),headForward=EVP(head.forward)});
                    }
                }
                foreach(string state in new[]{"DownHealthy","DownLowHealth"})
                {
                    var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Destination("Elite1",state));
                    using(var sample=new Sampling(elite.animator,clip,false)){sample.At(clip.length*.5f);rows.Add(new{state,head=EVP(head.position),hips=EVP(hips.position),ground=skin.Minimum(5),knees=new[]{EVP(elite.animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position),EVP(elite.animator.GetBoneTransform(HumanBodyBones.RightLowerLeg).position)}});}
                }
                EVSave("probe.json",rows);return File.ReadAllText(Path.Combine(ElitePreviewOutput,"analysis/probe.json"));
            }
            finally{foreach(var o in temp.Where(o=>o!=null).AsEnumerable().Reverse())UnityEngine.Object.DestroyImmediate(o);EditorSceneManager.ClosePreviewScene(scene);}
        }
        public static string RenderEliteExecutionPreview(string code,float seconds,string outputPath)
        {
            var scene=EditorSceneManager.NewPreviewScene();var temp=new List<UnityEngine.Object>();RenderTexture rt=null;Texture2D texture=null;
            try
            {
                var e=ExecutionSceneClone("Elite1",scene,temp);var p=ExecutionSceneClone("SamuraiPlayer",scene,temp);
                var row=Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(Path.Combine(ElitePreviewOutput,"analysis/adaptation.json"))).First(x=>(string)x["code"]==code);
                var offset=row["offset"].ToObject<float[]>();e.gameObject.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(ElitePreviewFolder+"/Elite1_"+code+"_NaturalReceiver.anim");e.animator.enabled=false;new EVGenericSampler(e.animator,clip).At(seconds);
                float lift=ElitePreviewGroundY-new SkinSampler(e.gameObject,e.animator).Minimum(5);e.gameObject.transform.SetPositionAndRotation(new Vector3(offset[0],lift,offset[2]),Quaternion.Euler(0,(float)row["yaw"],0));
                var attack=AssetDatabase.LoadAssetAtPath<AnimationClip>(ExecutionFolder+"/Execution"+code+".anim");
                using(var sample=new Sampling(p.animator,attack,false))
                {
                    sample.At(Mathf.Min(seconds,attack.length));
                    var camgo=new GameObject("Head preview camera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camgo,scene);temp.Add(camgo);var cam=camgo.AddComponent<Camera>();cam.scene=scene;cam.transform.position=new Vector3(6,3.2f,4);cam.transform.LookAt(new Vector3(0,1.2f,1.5f));cam.fieldOfView=42;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.13f,.16f,.20f);
                    var lightgo=new GameObject("Preview light");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightgo,scene);temp.Add(lightgo);var light=lightgo.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.transform.rotation=Quaternion.Euler(45,-45,0);
                    rt=new RenderTexture(1000,800,24);rt.Create();cam.targetTexture=rt;cam.Render();var previous=RenderTexture.active;RenderTexture.active=rt;texture=new Texture2D(1000,800,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1000,800),0,0);texture.Apply();RenderTexture.active=previous;File.WriteAllBytes(outputPath,texture.EncodeToPNG());cam.targetTexture=null;
                }
                return outputPath;
            }
            finally{if(texture!=null)UnityEngine.Object.DestroyImmediate(texture);if(rt!=null){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}foreach(var o in temp.Where(o=>o!=null).AsEnumerable().Reverse())UnityEngine.Object.DestroyImmediate(o);EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
