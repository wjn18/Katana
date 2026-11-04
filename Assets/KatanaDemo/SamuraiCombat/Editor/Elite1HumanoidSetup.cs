using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace OCDesign.Samurai.EditorTools
{
    /// <summary>只维护Avatar、肌肉片段、Animator引用和步频；不调整武器或战斗参数。</summary>
    public static class Elite1HumanoidSetup
    {
        public const string ModelPath="Assets/KatanaDemo/Characters/Elite1/Models/Elite1.fbx";
        const string SourceFolder="Assets/2Handed_Hammer_Set/Animations/Humanoid/Inplace/";
        const string LegacyController="Assets/KatanaDemo/Characters/Elite1/Animations/Generic/Combat/Elite1Combat.controller";
        static readonly string[] Human={"Hips","Spine","Chest","UpperChest","Neck","Head","LeftShoulder","LeftUpperArm","LeftLowerArm","LeftHand","RightShoulder","RightUpperArm","RightLowerArm","RightHand","LeftUpperLeg","LeftLowerLeg","LeftFoot","LeftToes","RightUpperLeg","RightLowerLeg","RightFoot","RightToes"};
        static readonly string[] Names={"Hips","Spine","Spine1","Spine2","Neck","Head","LeftShoulder","LeftArm","LeftForeArm","LeftHand","RightShoulder","RightArm","RightForeArm","RightHand","LeftUpLeg","LeftLeg","LeftFoot","LeftToeBase","RightUpLeg","RightLeg","RightFoot","RightToeBase"};
        public static readonly string[] Walk={"Walk","WalkForwardRight","WalkRight","WalkBackwardRight","WalkBackward","WalkBackwardLeft","WalkLeft","WalkForwardLeft"};
        public static readonly string[] Run={"Run","RunForwardRight","RunRight","RunBackwardRight","RunBackward","RunBackwardLeft","RunLeft","RunForwardLeft"};
        static readonly string[] Directions={"front","frontR45","right","backR45","back","backL45","left","frontL45"};
        [Serializable] public sealed class Spec
        {
            public string state,source; public bool loop; public float hold=-1;
            public Spec(string s,string c,bool l=false,float h=-1){state=s;source=c;loop=l;hold=h;}
        }
        public static List<Spec> Specs()
        {
            var s=new List<Spec>{new Spec("Idle","idle",true),new Spec("Heavy","attack01"),new Spec("Sweep","attack02"),new Spec("SweepReverse","attack04"),new Spec("Strike","attack06"),new Spec("Combo01","combo01"),new Spec("Combo02","combo02"),new Spec("Combo03","Combo03"),new Spec("Combo04","Combo04"),new Spec("Guard","defense",true),new Spec("GuardHit","hit_body_front"),new Spec("GuardHit2","hit_body_left"),new Spec("GuardHit3","hit_body_right"),new Spec("GuardBreak","down_start"),new Spec("Hit","hit_body_front"),new Spec("Death","dead01"),new Spec("Deflected","hit_head_front"),new Spec("Charge","attack01",true,.12f)};
            for(int i=0;i<8;i++){s.Add(new Spec(Walk[i],"move_walk_"+Directions[i],true));s.Add(new Spec(Run[i],"move_run_"+Directions[i],true));}return s;
        }
        public static string ReportFolder => Path.GetFullPath(Path.Combine(Application.dataPath,"../../镇门鬼将/动画资源/elite1-combat/analysis/humanoid-20261027"));
        public static void Install()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Exit Play before installing Elite1 Humanoid.");
            if(AssetDatabase.LoadAssetAtPath<GameObject>(Elite1CombatSetup.PrefabPath)==null)throw new InvalidOperationException("Elite1 prefab is missing. Restore the existing prefab before maintenance.");
            Directory.CreateDirectory(ReportFolder); EnsureFolder(Elite1CombatSetup.ClipFolder);
            var report=new Elite1CombatSetup.InstallReport(); var avatar=ConfigureAvatar(report);
            var clips=CopyClips(report); GroundRootHeight(clips,report);var controller=ConfigureController(clips);
            ReplaceProfileClips(clips); ConfigurePrefab(avatar,controller,clips,report);
            SamuraiEnemyReadabilitySetup.GroundDeath("Elite1",true);
            Elite1ParrySetup.Install();
            var maintainedProfile=AssetDatabase.LoadAssetAtPath<SamuraiCombatProfile>(Elite1CombatSetup.ProfilePath);
            if(maintainedProfile!=null&&maintainedProfile.elitePoseContinuityVersion>0)SamuraiElitePoseContinuitySetup.Install();
            File.WriteAllText(Path.Combine(ReportFolder,"接入构建.json"),JsonUtility.ToJson(report,true));
            Debug.Log("Elite1 Humanoid installed: valid human Avatar, "+clips.Count+" muscle clips. Combat values and weapon assets retained.");
        }
        public static Avatar ConfigureAvatar(Elite1CombatSetup.InstallReport report)
        {
            var scene=EditorSceneManager.NewPreviewScene(); GameObject model=null;HumanDescription description;
            try{
                model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath),scene);
                PrefabUtility.UnpackPrefabInstance(model,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                model.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);model.transform.localScale=Vector3.one;
                Elite1MotionBaker.Reference(model,model.GetComponentInChildren<SkinnedMeshRenderer>(),report);
                var selected=new HashSet<Transform>(model.GetComponentInChildren<SkinnedMeshRenderer>().bones);selected.Add(model.transform);
                foreach(var t in model.GetComponentsInChildren<Transform>())if(t.name=="Armature")selected.Add(t);
                description=new HumanDescription{
                    human=Human.Select((h,i)=>new HumanBone{humanName=HumanTrait.BoneName[(int)(HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),h)],boneName=Names[i],limit=new HumanLimit{useDefaultValues=true}}).ToArray(),
                    skeleton=model.GetComponentsInChildren<Transform>().Where(selected.Contains).Select(t=>new SkeletonBone{name=t.name,position=t.localPosition,rotation=t.localRotation,scale=t.localScale}).ToArray(),
                    upperArmTwist=.5f,lowerArmTwist=.5f,upperLegTwist=.5f,lowerLegTwist=.5f,armStretch=0,legStretch=0,feetSpacing=0};
                var test=AvatarBuilder.BuildHumanAvatar(model,description);bool valid=test.isValid&&test.isHuman;UnityEngine.Object.DestroyImmediate(test);
                if(!valid)throw new InvalidOperationException("Elite1 Humanoid Avatar validation failed.");
            }finally{if(model!=null)UnityEngine.Object.DestroyImmediate(model);EditorSceneManager.ClosePreviewScene(scene);}
            var importer=(ModelImporter)AssetImporter.GetAtPath(ModelPath);
            if(importer.animationType!=ModelImporterAnimationType.Human||importer.avatarSetup!=ModelImporterAvatarSetup.CreateFromThisModel||!Equivalent(importer.humanDescription,description)||importer.importAnimation||importer.optimizeGameObjects){
                importer.animationType=ModelImporterAnimationType.Human;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;importer.sourceAvatar=null;importer.humanDescription=description;importer.importAnimation=false;importer.optimizeGameObjects=false;importer.SaveAndReimport();
            }
            var avatar=AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().First(a=>a.isHuman&&a.isValid);report.avatarValid=true;
            File.WriteAllText(Path.Combine(ReportFolder,"HumanDescription.json"),JsonUtility.ToJson(description,true));return avatar;
        }
        static bool Equivalent(HumanDescription a,HumanDescription b)
        {
            if(a.human==null||a.skeleton==null||a.human.Length!=b.human.Length||a.skeleton.Length!=b.skeleton.Length)return false;
            for(int i=0;i<a.human.Length;i++)if(a.human[i].humanName!=b.human[i].humanName||a.human[i].boneName!=b.human[i].boneName||!a.human[i].limit.useDefaultValues)return false;
            for(int i=0;i<a.skeleton.Length;i++){var x=a.skeleton[i];var y=b.skeleton[i];if(x.name!=y.name||(x.position-y.position).sqrMagnitude>1e-12f||Quaternion.Angle(x.rotation,y.rotation)>.01f||(x.scale-y.scale).sqrMagnitude>1e-12f)return false;}
            return Mathf.Abs(a.armStretch-b.armStretch)<1e-6f&&Mathf.Abs(a.legStretch-b.legStretch)<1e-6f&&Mathf.Abs(a.upperArmTwist-b.upperArmTwist)<1e-6f&&Mathf.Abs(a.lowerArmTwist-b.lowerArmTwist)<1e-6f&&Mathf.Abs(a.upperLegTwist-b.upperLegTwist)<1e-6f&&Mathf.Abs(a.lowerLegTwist-b.lowerLegTwist)<1e-6f;
        }
        static Dictionary<string,AnimationClip> CopyClips(Elite1CombatSetup.InstallReport report)
        {
            var result=new Dictionary<string,AnimationClip>();
            foreach(var spec in Specs()){
                string sourcePath=SourceFolder+spec.source+"_inplace.fbx";
                var source=AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__"));
                if(!source.humanMotion)throw new InvalidOperationException(sourcePath+" is not Humanoid.");
                var copy=UnityEngine.Object.Instantiate(source);copy.name="Elite1_"+spec.state;copy.legacy=false;
                if(spec.hold>=0)foreach(var binding in AnimationUtility.GetCurveBindings(copy)){
                    float value=AnimationUtility.GetEditorCurve(source,binding).Evaluate(source.length*spec.hold);
                    AnimationUtility.SetEditorCurve(copy,binding,AnimationCurve.Constant(0,.5f,value));
                }
                var settings=AnimationUtility.GetAnimationClipSettings(copy);settings.loopTime=spec.loop;settings.loopBlend=false;settings.startTime=0;settings.stopTime=spec.hold>=0?.5f:source.length;AnimationUtility.SetAnimationClipSettings(copy,settings);
                string path=Elite1CombatSetup.ClipFolder+"/"+copy.name+".anim";var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if(existing==null){AssetDatabase.CreateAsset(copy,path);existing=copy;}else{EditorUtility.CopySerialized(copy,existing);UnityEngine.Object.DestroyImmediate(copy);}
                EditorUtility.SetDirty(existing);AssetDatabase.SaveAssetIfDirty(existing);result.Add(spec.state,existing);
                report.clips.Add(new Elite1CombatSetup.ClipReport{state=spec.state,path=path,source=sourcePath,duration=existing.length,frames=Mathf.RoundToInt(existing.length*source.frameRate)+1,curves=AnimationUtility.GetCurveBindings(existing).Length,loop=spec.loop});
            }return result;
        }
        static AnimatorController ConfigureController(Dictionary<string,AnimationClip> clips)
        {
            string path=Elite1CombatSetup.ControllerPath;
            if(AssetDatabase.LoadAssetAtPath<AnimatorController>(path)==null&&AssetDatabase.LoadAssetAtPath<AnimatorController>(LegacyController)!=null){string error=AssetDatabase.MoveAsset(LegacyController,path);if(!string.IsNullOrEmpty(error))throw new IOException(error);}
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(path)??AnimatorController.CreateAnimatorControllerAtPath(path);
            if(!controller.parameters.Any(p=>p.name=="LocomotionRate"))controller.AddParameter(new AnimatorControllerParameter{name="LocomotionRate",type=AnimatorControllerParameterType.Float,defaultFloat=1});
            var machine=controller.layers[0].stateMachine;
            var profile=AssetDatabase.LoadAssetAtPath<SamuraiCombatProfile>(Elite1CombatSetup.ProfilePath);
            foreach(var clip in clips)
            {
                // Preserve the installed settled death or any later authored Death binding.
                var existing=machine.states.FirstOrDefault(s=>s.state.name==clip.Key).state;
                if(clip.Key=="Death"&&profile!=null&&profile.elitePoseContinuityVersion>0&&existing!=null&&existing.motion!=null)continue;
                SetState(machine,clip.Key,clip.Value);
            }
            SetState(machine,"Guard_B",clips["Guard"]);for(int i=1;i<=3;i++)SetState(machine,"GuardBHit"+i,clips[i==1?"GuardHit":"GuardHit"+i]);
            machine.defaultState=machine.states.First(s=>s.state.name=="Idle").state;EditorUtility.SetDirty(controller);AssetDatabase.SaveAssetIfDirty(controller);return controller;
        }
        static void SetState(AnimatorStateMachine machine,string name,AnimationClip clip)
        {
            var state=machine.states.FirstOrDefault(s=>s.state.name==name).state??machine.AddState(name);
            state.motion=clip;state.speed=1;state.writeDefaultValues=true;state.iKOnFeet=name!="Death";state.speedParameterActive=name.StartsWith("Walk")||name.StartsWith("Run");state.speedParameter="LocomotionRate";
        }
        static void ReplaceProfileClips(Dictionary<string,AnimationClip> clips)
        {
            foreach(var path in Directory.GetFiles(Elite1CombatSetup.ConfigFolder,"*.asset")){
                var asset=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path.Replace('\\','/'));if(asset==null)continue;
                var serialized=new SerializedObject(asset);var p=serialized.GetIterator();bool changed=false;
                while(p.Next(true))if(p.propertyType==SerializedPropertyType.ObjectReference){var old=p.objectReferenceValue as AnimationClip;if(old==null||!old.name.StartsWith("Elite1_"))continue;AnimationClip clip;if(clips.TryGetValue(old.name.Substring(7),out clip)&&old!=clip){p.objectReferenceValue=clip;changed=true;}}
                if(changed){serialized.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssetIfDirty(asset);}
            }
        }
        static void ConfigurePrefab(Avatar avatar,AnimatorController controller,Dictionary<string,AnimationClip> clips,Elite1CombatSetup.InstallReport report)
        {
            var root=PrefabUtility.LoadPrefabContents(Elite1CombatSetup.PrefabPath);var graph=default(PlayableGraph);
            try{
                if(SamuraiEnemyReadabilitySetup.InitializeBrain(root.GetComponent<SamuraiTrainingEnemy>())) EditorUtility.SetDirty(root);
                var a=root.GetComponentInChildren<Animator>();a.avatar=avatar;a.runtimeAnimatorController=null;a.applyRootMotion=false;a.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                a.Rebind();var origin=a.transform.localPosition;origin.y=0;a.transform.localPosition=origin;
                graph=PlayableGraph.Create("Elite1 Humanoid calibration");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);var playable=AnimationClipPlayable.Create(graph,clips["Idle"]);playable.SetApplyFootIK(true);AnimationPlayableOutput.Create(graph,"Elite1",a).SetSourcePlayable(playable);graph.Play();playable.SetTime(0);graph.Evaluate(.000001f);
                // RootT.y已按实际蒙皮脚底校准，视觉根保持零高度，避免重复叠加。
                var skin=root.GetComponentInChildren<SkinnedMeshRenderer>();report.visualOffset=0;
                var gait=root.GetComponent<Elite1LocomotionPlayback>();if(gait==null)throw new InvalidOperationException("Elite1 gait component is missing.");
                gait.walkReferenceSpeeds=Walk.Select(n=>MeasureStride(a,clips[n])).ToArray();gait.runReferenceSpeeds=Run.Select(n=>MeasureStride(a,clips[n])).ToArray();
                for(int i=0;i<8;i++){report.clips.First(c=>c.state==Walk[i]).strideSpeed=gait.walkReferenceSpeeds[i];report.clips.First(c=>c.state==Run[i]).strideSpeed=gait.runReferenceSpeeds[i];}
                playable.SetTime(0);graph.Evaluate(.000001f);
                var bones=skin.bones;var positions=bones.Select(b=>b.localPosition).ToArray();var rotations=bones.Select(b=>b.localRotation).ToArray();var scales=bones.Select(b=>b.localScale).ToArray();
                graph.Destroy();a.runtimeAnimatorController=controller;
                for(int i=0;i<bones.Length;i++){bones[i].localPosition=positions[i];bones[i].localRotation=rotations[i];bones[i].localScale=scales[i];}
                EnemyAttackRootMotionSetup.ConfigureInstalledDriver(root,"Elite1");
                PrefabUtility.SaveAsPrefabAsset(root,Elite1CombatSetup.PrefabPath);
            }finally{if(graph.IsValid())graph.Destroy();PrefabUtility.UnloadPrefabContents(root);}
        }
        sealed class PoseSkin
        {
            readonly Transform[] bones;readonly Matrix4x4[] bind;readonly Vector3[] vertices;readonly BoneWeight[] weights;readonly int[] feet;
            public PoseSkin(SkinnedMeshRenderer skin){bones=skin.bones;bind=skin.sharedMesh.bindposes;vertices=skin.sharedMesh.vertices;weights=skin.sharedMesh.boneWeights;feet=Enumerable.Range(0,vertices.Length).Where(i=>bones[weights[i].boneIndex0].name.IndexOf("Foot",StringComparison.OrdinalIgnoreCase)>=0||bones[weights[i].boneIndex0].name.IndexOf("Toe",StringComparison.OrdinalIgnoreCase)>=0).ToArray();if(feet.Length==0)throw new InvalidOperationException("No foot skin vertices.");}
            public float Minimum(bool fullBody=false){var matrices=bones.Select((b,i)=>b.localToWorldMatrix*bind[i]).ToArray();float min=float.PositiveInfinity;foreach(int i in fullBody?Enumerable.Range(0,vertices.Length):feet){var w=weights[i];var v=vertices[i];var p=matrices[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0+matrices[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1+matrices[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2+matrices[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;min=Mathf.Min(min,p.y);}return min;}
        }
        static void GroundRootHeight(Dictionary<string,AnimationClip> clips,Elite1CombatSetup.InstallReport report)
        {
            var scene=EditorSceneManager.NewPreviewScene();GameObject target=null,source=null;var sourceGraph=default(PlayableGraph);var targetGraph=default(PlayableGraph);
            try{
                target=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Elite1CombatSetup.PrefabPath),scene);foreach(var b in target.GetComponents<MonoBehaviour>())b.enabled=false;
                var a=target.GetComponentInChildren<Animator>();a.avatar=AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().First(v=>v.isHuman&&v.isValid);a.runtimeAnimatorController=null;a.transform.localPosition=Vector3.zero;a.Rebind();
                source=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/2Handed_Hammer_Set/Animations/Humanoid/00_T-pose.FBX"),scene);source.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);source.transform.localScale=Vector3.one;
                var sa=source.GetComponentInChildren<Animator>();sa.runtimeAnimatorController=null;sa.applyRootMotion=false;sa.cullingMode=AnimatorCullingMode.AlwaysAnimate;sa.Rebind();
                var targetSkin=new PoseSkin(target.GetComponentInChildren<SkinnedMeshRenderer>());var sourceSkin=new PoseSkin(source.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.name.Contains("BASEMESH")));
                sourceGraph=PlayableGraph.Create("Human ground reference");sourceGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);var sourcePlayable=AnimationClipPlayable.Create(sourceGraph,clips["Idle"]);sourcePlayable.SetApplyFootIK(true);AnimationPlayableOutput.Create(sourceGraph,"source",sa).SetSourcePlayable(sourcePlayable);sourceGraph.Play();sourcePlayable.SetTime(0);sourceGraph.Evaluate(.000001f);float referenceFloor=sourceSkin.Minimum();sourceGraph.Destroy();
                foreach(var spec in report.clips){
                    var clip=clips[spec.state];var native=AssetDatabase.LoadAllAssetsAtPath(spec.source).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__"));bool fullBody=spec.state=="Death"||spec.state=="GuardBreak";
                    sourceGraph=PlayableGraph.Create("Human ground source");sourceGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);sourcePlayable=AnimationClipPlayable.Create(sourceGraph,clip);sourcePlayable.SetApplyFootIK(spec.state!="Death");AnimationPlayableOutput.Create(sourceGraph,"source",sa).SetSourcePlayable(sourcePlayable);sourceGraph.Play();
                    targetGraph=PlayableGraph.Create("Human ground target");targetGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);var targetPlayable=AnimationClipPlayable.Create(targetGraph,clip);targetPlayable.SetApplyFootIK(spec.state!="Death");AnimationPlayableOutput.Create(targetGraph,"target",a).SetSourcePlayable(targetPlayable);targetGraph.Play();
                    var binding=AnimationUtility.GetCurveBindings(clip).First(b=>b.propertyName=="RootT.y");var nativeY=AnimationUtility.GetEditorCurve(clip,binding);int steps=Mathf.CeilToInt(clip.length*240);var keys=new Keyframe[steps+1];var desiredHeights=new float[steps+1];float maximum=0;
                    for(int f=0;f<=steps;f++){
                        float time=clip.length*f/steps;sourcePlayable.SetTime(time);targetPlayable.SetTime(time);sourceGraph.Evaluate(.000001f);targetGraph.Evaluate(.000001f);
                        // 原包离地幅度按身高比例保留；仅平移身体高度，不改肌肉、脚部或手臂旋转。
                        float targetHeight=a.humanScale*a.transform.lossyScale.y,sourceHeight=sa.humanScale*sa.transform.lossyScale.y;
                        float desired=.0015f+Mathf.Max(0,sourceSkin.Minimum(fullBody)-referenceFloor)*targetHeight/sourceHeight;
                        desiredHeights[f]=desired;
                        float correction=desired-targetSkin.Minimum(fullBody);maximum=Mathf.Max(maximum,Mathf.Abs(correction));keys[f]=new Keyframe(time,nativeY.Evaluate(time)+correction/targetHeight);
                    }
                    sourceGraph.Destroy();targetGraph.Destroy();
                    SetHeightCurve(clip,binding,keys);
                    // Humanoid脚部IK会在大幅转身时重新求解，重新求值补齐其残余下探。
                    for(int iteration=0;iteration<3;iteration++){
                        targetGraph=PlayableGraph.Create("Human ground residual");targetGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);targetPlayable=AnimationClipPlayable.Create(targetGraph,clip);targetPlayable.SetApplyFootIK(spec.state!="Death");AnimationPlayableOutput.Create(targetGraph,"target",a).SetSourcePlayable(targetPlayable);targetGraph.Play();
                        var refined=(Keyframe[])keys.Clone();float residual=0;
                        for(int f=0;f<=steps;f++){targetPlayable.SetTime(keys[f].time);targetGraph.Evaluate(.000001f);float lift=Mathf.Max(0,desiredHeights[f]-targetSkin.Minimum(fullBody));refined[f].value+=lift/(a.humanScale*a.transform.lossyScale.y);residual=Mathf.Max(residual,lift);}
                        targetGraph.Destroy();keys=refined;SetHeightCurve(clip,binding,keys);maximum+=residual;if(residual<.0001f)break;
                    }
                    EditorUtility.SetDirty(clip);AssetDatabase.SaveAssetIfDirty(clip);spec.maximumGroundLift=maximum;
                }
            }finally{if(sourceGraph.IsValid())sourceGraph.Destroy();if(targetGraph.IsValid())targetGraph.Destroy();if(source!=null)UnityEngine.Object.DestroyImmediate(source);if(target!=null)UnityEngine.Object.DestroyImmediate(target);EditorSceneManager.ClosePreviewScene(scene);}
        }
        static void SetHeightCurve(AnimationClip clip,EditorCurveBinding binding,Keyframe[] keys)
        {
            for(int i=0;i<keys.Length;i++){float before=i>0?(keys[i].value-keys[i-1].value)/(keys[i].time-keys[i-1].time):0;float after=i+1<keys.Length?(keys[i+1].value-keys[i].value)/(keys[i+1].time-keys[i].time):0;keys[i].inTangent=before;keys[i].outTangent=after;}
            AnimationUtility.SetEditorCurve(clip,binding,new AnimationCurve(keys));
        }
        public static float MeasureStride(Animator a,AnimationClip clip)
        {
            var graph=PlayableGraph.Create("Elite1 Humanoid stride");
            try{
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);var p=AnimationClipPlayable.Create(graph,clip);p.SetApplyFootIK(true);AnimationPlayableOutput.Create(graph,"stride",a).SetSourcePlayable(p);graph.Play();
                int steps=Mathf.CeilToInt(clip.length*120);var values=new Vector3[2][]{new Vector3[steps+1],new Vector3[steps+1]};var feet=new[]{a.GetBoneTransform(HumanBodyBones.LeftFoot),a.GetBoneTransform(HumanBodyBones.RightFoot)};
                for(int f=0;f<=steps;f++){p.SetTime(clip.length*f/steps);graph.Evaluate(.000001f);for(int k=0;k<2;k++)values[k][f]=feet[k].position;}
                var speeds=new List<float>();for(int k=0;k<2;k++){float low=values[k].Min(v=>v.y);for(int f=1;f<=steps;f++)if(values[k][f].y<low+.06f&&values[k][f-1].y<low+.06f){var d=values[k][f]-values[k][f-1];d.y=0;if(d.magnitude>.00001f)speeds.Add(d.magnitude*steps/clip.length);}}
                speeds.Sort();return speeds.Count>0?Mathf.Max(.1f,speeds[speeds.Count/2]):1;
            }finally{graph.Destroy();}
        }
        static void EnsureFolder(string path){if(AssetDatabase.IsValidFolder(path))return;string parent=path.Substring(0,path.LastIndexOf('/'));EnsureFolder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));}
    }
}
