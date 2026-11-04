using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OCDesign.Samurai.EditorTools
{
    public static partial class SamuraiDownPoseAdaptation
    {
        public const string ExecutionFolder = "Assets/KatanaDemo/SamuraiCombat/Animations/Execution";
        static readonly string[] ReceiverFiles = { "Nioh3_Receiver_V1_32DBF64D", "Nioh3_Receiver_V2_2D4FFCF0" };
        public static string ImportExecutionSources()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Exit Play before source import.");
            string canonical = ExecutionFolder + "/SourceFBX/Nioh3_Receiver_Canonical.fbx";
            AssetDatabase.ImportAsset(canonical, ImportAssetOptions.ForceSynchronousImport);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(canonical);
            var importer = (ModelImporter)AssetImporter.GetAtPath(canonical);
            var humanNames = new[] { "Hips", "Spine", "Chest", "Neck", "Head", "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes", "RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes", "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand" };
            var ids = new[] { 2,9,10,11,12,3,5,7,23,4,6,8,24,13,15,17,19,14,16,18,20 };
            var hd = importer.humanDescription;
            hd.human = humanNames.Select((n,i) => new HumanBone { humanName=n, boneName="bone_"+ids[i], limit=new HumanLimit { useDefaultValues=true } }).ToArray();
            hd.skeleton = model.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone { name=t.name, position=t.localPosition, rotation=t.localRotation, scale=t.localScale }).ToArray();
            hd.armStretch=hd.legStretch=0; hd.upperArmTwist=hd.lowerArmTwist=hd.upperLegTwist=hd.lowerLegTwist=.5f; hd.hasTranslationDoF=false;
            importer.animationType=ModelImporterAnimationType.Human; importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
            importer.humanDescription=hd; importer.importAnimation=false; importer.optimizeGameObjects=false;
            var so=new SerializedObject(importer); so.FindProperty("m_AutoGenerateAvatarMappingIfUnspecified").boolValue=false; so.ApplyModifiedPropertiesWithoutUndo();
            importer.SaveAndReimport();
            var avatar=AssetDatabase.LoadAllAssetsAtPath(canonical).OfType<Avatar>().First();
            if (!avatar.isValid || !avatar.isHuman) throw new InvalidOperationException("Receiver canonical avatar invalid.");
            foreach (string file in ReceiverFiles)
            {
                string path=ExecutionFolder+"/SourceFBX/"+file+".fbx";
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                importer=(ModelImporter)AssetImporter.GetAtPath(path);
                importer.animationType=ModelImporterAnimationType.Human; importer.avatarSetup=ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar=avatar; importer.humanDescription=avatar.humanDescription;
                so=new SerializedObject(importer);so.FindProperty("m_AutoGenerateAvatarMappingIfUnspecified").boolValue=false;so.ApplyModifiedPropertiesWithoutUndo();
                importer.animationCompression=ModelImporterAnimationCompression.Off; importer.importCameras=false; importer.importLights=false;
                importer.materialImportMode=ModelImporterMaterialImportMode.None; importer.optimizeGameObjects=false;
                var takes=importer.clipAnimations.Length>0?importer.clipAnimations:importer.defaultClipAnimations;
                foreach(var take in takes) { take.lockRootRotation=take.lockRootHeightY=take.lockRootPositionXZ=true; take.keepOriginalOrientation=take.keepOriginalPositionY=take.keepOriginalPositionXZ=true; take.heightFromFeet=false;take.heightOffset=take.rotationOffset=0;take.loopTime=false; }
                importer.clipAnimations=takes; importer.SaveAndReimport();
            }
            return "Imported two receivers using their own 802-bone source reference.";
        }

        public static string BakeExecutionReceivers()
        {
            if(Application.isPlaying) throw new InvalidOperationException("Exit Play before baking.");
            var scene=EditorSceneManager.NewPreviewScene();var temporary=new List<UnityEngine.Object>();
            var details=new List<string>();
            try
            {
                var rig=CreateGetupRig("Enemy1",scene,temporary);Transform[] bones;
                var avatar=BuildTemporaryEnemyAvatar(rig.animator,out bones); temporary.Add(avatar);rig.animator.avatar=avatar;rig.animator.enabled=true;rig.animator.Rebind();
                var skin=new SkinSampler(rig.gameObject,rig.animator);
                for(int index=0;index<2;index++)
                {
                    var imported=AssetDatabase.LoadAllAssetsAtPath(ExecutionFolder+"/SourceFBX/"+ReceiverFiles[index]+".fbx").OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview"));
                    var source=PrepareSource(imported);temporary.Add(source);
                    int steps=Mathf.CeilToInt(source.length*120);var values=Enumerable.Range(0,bones.Length*10).Select(_=>new float[steps+1]).ToArray();var previous=new Quaternion[bones.Length];
                    var hips=rig.animator.GetBoneTransform(HumanBodyBones.Hips);float maxGroundCorrection=0;
                    using(var sampling=new Sampling(rig.animator,source,false))
                    for(int f=0;f<=steps;f++)
                    {
                        sampling.At(source.length*f/steps);
                        // Ground the actual target skin, including the prone torso, without changing rotations.
                        float correction=.004f-skin.Minimum(5);hips.position+=Vector3.up*correction;maxGroundCorrection=Mathf.Max(maxGroundCorrection,Mathf.Abs(correction));
                        for(int b=0;b<bones.Length;b++)
                        {
                            var p=bones[b].localPosition;var q=bones[b].localRotation;var s=bones[b].localScale;
                            if(f>0&&Quaternion.Dot(previous[b],q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);previous[b]=q;
                            var v=new[]{p.x,p.y,p.z,q.x,q.y,q.z,q.w,s.x,s.y,s.z};for(int k=0;k<10;k++)values[b*10+k][f]=v[k];
                        }
                    }
                    var output=new AnimationClip { name="Enemy1_ExecutionV"+(index+1),frameRate=120,legacy=false,wrapMode=WrapMode.ClampForever };
                    temporary.Add(output);
                    for(int b=0;b<bones.Length;b++)for(int k=0;k<10;k++)AnimationUtility.SetEditorCurve(output,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(bones[b],rig.animator.transform),typeof(Transform),TransformProperties[k]),Curve(values[b*10+k],source.length));
                    output.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(output);settings.loopTime=false;settings.loopBlend=false;settings.stopTime=source.length;AnimationUtility.SetAnimationClipSettings(output,settings);
                    SaveExecutionClip(output,ExecutionFolder+"/Enemy1/"+output.name+".anim");
                    details.Add(output.name+": "+source.length+"s, max target grounding "+maxGroundCorrection+"m");
                }
                return string.Join("\n",details);
            }
            finally { foreach(var item in temporary.Where(o=>o!=null).Reverse<UnityEngine.Object>()) UnityEngine.Object.DestroyImmediate(item);EditorSceneManager.ClosePreviewScene(scene); }
        }
        static void SaveExecutionClip(AnimationClip clip,string path)
        {
            var old=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(old==null){old=UnityEngine.Object.Instantiate(clip);old.hideFlags=HideFlags.None;AssetDatabase.CreateAsset(old,path);}
            else EditorUtility.CopySerialized(clip,old);
            EditorUtility.SetDirty(old);AssetDatabase.SaveAssetIfDirty(old);
        }
        public static string BakeExecutionJoins()
        {
            var scene=EditorSceneManager.NewPreviewScene();var temp=new List<UnityEngine.Object>();
            try
            {
                var enemy=CreateGetupRig("Enemy1",scene,temp);
                var bones=enemy.animator.GetComponentsInChildren<Transform>(true).Where(b=>b!=enemy.animator.transform).ToArray();
                var skin=new SkinSampler(enemy.gameObject,enemy.animator);
                for(int i=0;i<2;i++)
                {
                    var receiver=AssetDatabase.LoadAssetAtPath<AnimationClip>(ExecutionFolder+"/Enemy1/Enemy1_ExecutionV"+(i+1)+".anim");
                    float delay=i==0?.36f:.56f;
                    for(int branch=0;branch<2;branch++)
                    {
                        string suffix=branch==0?"Healthy":"LowHealth";
                        var down=AssetDatabase.LoadAssetAtPath<AnimationClip>(Destination("Enemy1",branch==0?"DownHealthy":"DownLowHealth"));
                        var joined=BakeGenericExecutionJoin(enemy.animator,bones,skin,receiver,down,0,delay,false);
                        joined.name="Enemy1_ExecutionV"+(i+1)+suffix;SaveExecutionClip(joined,ExecutionFolder+"/Enemy1/"+joined.name+".anim");temp.Add(joined);
                        if(i==0)
                        {
                            var getup=AssetDatabase.LoadAssetAtPath<AnimationClip>(Destination("Enemy1",branch==0?"GetupHealthy":"GetupLowHealth"));
                            var recovery=BakeGenericExecutionJoin(enemy.animator,bones,skin,getup,receiver,receiver.length,.32f,true);
                            recovery.name="Enemy1_ExecutionGetup"+suffix;SaveExecutionClip(recovery,ExecutionFolder+"/Enemy1/"+recovery.name+".anim");temp.Add(recovery);
                        }
                    }
                }
                var player=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/KatanaDemo/SamuraiCombat/Prefabs/SamuraiCombatPlayer.prefab"),scene);temp.Add(player);
                player.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);foreach(var c in player.GetComponentsInChildren<MonoBehaviour>(true))c.enabled=false;
                var animator=player.GetComponentInChildren<Animator>();animator.runtimeAnimatorController=null;animator.applyRootMotion=false;
                var playerSkin=new SkinSampler(player,animator);string[] names={"Nioh3_Mid_Slash_09600","Nioh3_Mid_ComboX3_DownwardSlash_09702"};
                for(int i=0;i<2;i++)
                {
                    var original=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/KatanaDemo/SamuraiCombat/Animations/Nioh3/Runtime/"+names[i]+".anim");
                    var clip=UnityEngine.Object.Instantiate(original);temp.Add(clip);clip.name=i==0?"Execution09600":"Execution09702";
                    int steps=Mathf.CeilToInt(clip.length*240);float[] heights=new float[steps+1];var curve=AnimationUtility.GetEditorCurve(clip,Binding("RootT.y"));
                    using(var sample=new Sampling(animator,original,false))for(int f=0;f<=steps;f++)
                    {float t=clip.length*f/steps;sample.At(t);heights[f]=curve.Evaluate(t)+(.004f-playerSkin.Minimum(5))/(animator.humanScale*animator.transform.lossyScale.y);}
                    AnimationUtility.SetEditorCurve(clip,Binding("RootT.y"),Curve(heights,clip.length));
                    SaveExecutionClip(clip,ExecutionFolder+"/"+clip.name+".anim");
                }
                return "Four receiver entrances, two R7/R8 recovery joins, two grounded attacker derivatives baked.";
            }
            finally{foreach(var o in temp.Where(o=>o!=null).Reverse<UnityEngine.Object>())UnityEngine.Object.DestroyImmediate(o);EditorSceneManager.ClosePreviewScene(scene);}
        }
        static AnimationClip BakeGenericExecutionJoin(Animator animator,Transform[] bones,SkinSampler skin,AnimationClip motion,AnimationClip from,float fromTime,float blend,bool recovery)
        {
            from.SampleAnimation(animator.gameObject,fromTime);var startP=bones.Select(b=>b.localPosition).ToArray();var startQ=bones.Select(b=>b.localRotation).ToArray();
            motion.SampleAnimation(animator.gameObject,0);var baseP=bones.Select(b=>b.localPosition).ToArray();var baseQ=bones.Select(b=>b.localRotation).ToArray();
            float duration=motion.length+(recovery?0:blend);int steps=Mathf.CeilToInt(duration*120);
            var values=Enumerable.Range(0,bones.Length*10).Select(_=>new float[steps+1]).ToArray();var previous=new Quaternion[bones.Length];
            var hips=bones.First(b=>b.name=="Hips");
            for(int f=0;f<=steps;f++)
            {
                float t=duration*f/steps;motion.SampleAnimation(animator.gameObject,recovery?t:Mathf.Max(0,t-blend));
                float weight=1-Mathf.SmoothStep(0,1,Mathf.Clamp01(t/(recovery?blend:blend*.8f)));
                for(int b=0;b<bones.Length;b++) {bones[b].localPosition+=(startP[b]-baseP[b])*weight;bones[b].localRotation=Quaternion.Slerp(Quaternion.identity,startQ[b]*Quaternion.Inverse(baseQ[b]),weight)*bones[b].localRotation;}
                hips.position+=Vector3.up*(.004f-skin.Minimum(5));
                for(int b=0;b<bones.Length;b++)
                {var p=bones[b].localPosition;var q=bones[b].localRotation;var s=bones[b].localScale;if(f>0&&Quaternion.Dot(previous[b],q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);previous[b]=q;var v=new[]{p.x,p.y,p.z,q.x,q.y,q.z,q.w,s.x,s.y,s.z};for(int k=0;k<10;k++)values[b*10+k][f]=v[k];}
            }
            var output=new AnimationClip {frameRate=120,wrapMode=WrapMode.ClampForever};
            for(int b=0;b<bones.Length;b++)for(int k=0;k<10;k++)AnimationUtility.SetEditorCurve(output,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(bones[b],animator.transform),typeof(Transform),TransformProperties[k]),Curve(values[b*10+k],duration));
            output.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(output);settings.loopTime=false;settings.stopTime=duration;AnimationUtility.SetAnimationClipSettings(output,settings);return output;
        }
        public static string MeasureExecutionGeometry(string outputPath)
        {
            var scene=EditorSceneManager.NewPreviewScene();var temporary=new List<UnityEngine.Object>();var rows=new List<object>();
            try
            {
                var enemy=CreateGetupRig("Enemy1",scene,temporary);var all=enemy.animator.GetComponentsInChildren<Transform>(true);
                var original=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/KatanaDemo/SamuraiCombat/Prefabs/SamuraiCombatPlayer.prefab");
                var player=(GameObject)PrefabUtility.InstantiatePrefab(original,scene);temporary.Add(player);player.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                foreach(var c in player.GetComponentsInChildren<MonoBehaviour>(true))c.enabled=false;
                var actor=player.GetComponent<OCDesign.Samurai.SamuraiCombatant>();var animator=actor.Animator;animator.runtimeAnimatorController=null;animator.applyRootMotion=false;
                var so=new SerializedObject(actor);var bladeBase=(Transform)so.FindProperty("weaponBase").objectReferenceValue;var bladeTip=(Transform)so.FindProperty("weaponTip").objectReferenceValue;
                string[] names={"Nioh3_Mid_Slash_09600","Nioh3_Mid_ComboX3_DownwardSlash_09702"};
                Func<Vector3,float[]> v=p=>new[]{p.x,p.y,p.z};
                for(int index=0;index<2;index++)
                {
                    var attack=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/KatanaDemo/SamuraiCombat/Animations/Nioh3/Runtime/"+names[index]+".anim");
                    var adapted=AssetDatabase.LoadAssetAtPath<AnimationClip>(ExecutionFolder+"/"+(index==0?"Execution09600":"Execution09702")+".anim");if(adapted!=null)attack=adapted;
                    var victim=AssetDatabase.LoadAssetAtPath<AnimationClip>(ExecutionFolder+"/Enemy1/Enemy1_ExecutionV"+(index+1)+".anim");
                    using(var sampling=new Sampling(animator,attack,false))
                    for(int f=0;f<=Mathf.CeilToInt(Mathf.Max(attack.length,victim.length)*60);f++)
                    {
                        float t=f/60f;sampling.At(Mathf.Min(t,attack.length));victim.SampleAnimation(enemy.animator.gameObject,Mathf.Min(t,victim.length));
                        rows.Add(new {pair=index+1,time=t,bladeBase=v(bladeBase.position),bladeTip=v(bladeTip.position),playerHips=v(animator.GetBoneTransform(HumanBodyBones.Hips).position),victim=all.Where(b=>GenericNames.Contains(b.name)).ToDictionary(b=>b.name,b=>v(b.position))});
                    }
                }
                System.IO.File.WriteAllText(outputPath,Newtonsoft.Json.JsonConvert.SerializeObject(rows));return outputPath;
            }
            finally {foreach(var item in temporary.Where(o=>o!=null).Reverse<UnityEngine.Object>())UnityEngine.Object.DestroyImmediate(item);EditorSceneManager.ClosePreviewScene(scene);}
        }
        public static string RenderExecutionPose(int pairIndex,float seconds,string outputPath)
        {
            var scene=EditorSceneManager.NewPreviewScene();var temp=new List<UnityEngine.Object>();RenderTexture rt=null;Texture2D image=null;
            try
            {
                string code=pairIndex==0?"09600":"09702";
                var pair=AssetDatabase.LoadAssetAtPath<OCDesign.Samurai.SamuraiExecutionPair>("Assets/KatanaDemo/SamuraiCombat/Config/Execution/Enemy1_"+code+"_V"+(pairIndex+1)+".asset");
                var enemy=CreateGetupRig("Enemy1",scene,temp);enemy.gameObject.transform.SetPositionAndRotation(pair.receiverOffset,Quaternion.Euler(0,pair.receiverYaw,0));
                pair.receiverClip.SampleAnimation(enemy.animator.gameObject,Mathf.Min(seconds,pair.receiverClip.length));
                var player=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/KatanaDemo/SamuraiCombat/Prefabs/SamuraiCombatPlayer.prefab"),scene);temp.Add(player);
                player.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);foreach(var b in player.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;
                var animator=player.GetComponentInChildren<Animator>();animator.runtimeAnimatorController=null;animator.applyRootMotion=false;
                using(var sample=new Sampling(animator,pair.attackerClip,false))
                {
                    sample.At(Mathf.Min(seconds,pair.attackerClip.length));
                    var go=new GameObject("Execution preview camera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);temp.Add(go);var cam=go.AddComponent<Camera>();
                    cam.scene=scene;cam.transform.position=new Vector3(3.6f,2.3f,3.5f);cam.transform.LookAt(new Vector3(0,.8f,.6f));cam.fieldOfView=35;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.12f,.15f,.18f);
                    var lightGo=new GameObject("Preview light");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo,scene);temp.Add(lightGo);var light=lightGo.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.transform.rotation=Quaternion.Euler(45,-30,0);
                    rt=new RenderTexture(1200,900,24);rt.Create();cam.targetTexture=rt;cam.Render();var previous=RenderTexture.active;RenderTexture.active=rt;image=new Texture2D(1200,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1200,900),0,0);image.Apply();RenderTexture.active=previous;
                    System.IO.File.WriteAllBytes(outputPath,image.EncodeToPNG());cam.targetTexture=null;
                }
                return outputPath;
            }
            finally{if(image!=null)UnityEngine.Object.DestroyImmediate(image);if(rt!=null){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}foreach(var o in temp.Where(o=>o!=null).Reverse<UnityEngine.Object>())UnityEngine.Object.DestroyImmediate(o);EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
