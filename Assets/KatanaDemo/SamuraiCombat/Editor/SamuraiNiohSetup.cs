using System;
using System.IO;
using System.Linq;
using OCDesign.Samurai;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OCDesign.EditorTools
{
    /// <summary>定向接入已选动作目录（原19条及六连补充09202）。再次安装保留已调数值与架势顺序。</summary>
    public static class SamuraiNiohSetup
    {
        const string Root = SamuraiCombatSetup.Root;
        const string Anim = Root + "/Animations/Nioh3";
        const string CanonicalRig = Anim + "/SourceFBX/Nioh3_Canonical_TPose.fbx";
        const string TargetModel = "Assets/KatanaDemo/Characters/Douli/Models/Humanoid/Douli_Humanoid.fbx";
        const string PlayerAvatar = Anim + "/SamuraiNiohAvatar.asset";
        const string Config = Root + "/Config/Nioh3";
        [Serializable] public class Entry { public string code, name, label; public bool martialArt; public Vector2[] windows; public float link; }
        [Serializable] public class Catalog { public Entry[] entries; }
        public static Entry[] Entries => JsonUtility.FromJson<Catalog>(File.ReadAllText(Root + "/Editor/Nioh3Selected19.json")).entries;
        static readonly string[] HumanNames={"Hips","Spine","Chest","Neck","Head","LeftUpperLeg","LeftLowerLeg","LeftFoot","LeftToes","RightUpperLeg","RightLowerLeg","RightFoot","RightToes","LeftShoulder","LeftUpperArm","LeftLowerArm","LeftHand","RightShoulder","RightUpperArm","RightLowerArm","RightHand"};
        static readonly int[] BoneIds={2,9,10,11,12,3,5,7,23,4,6,8,24,13,15,17,19,14,16,18,20};

        public static void ConfigureCanonicalAvatar()
        {
            RequireEdit();
            var model=Required<GameObject>(CanonicalRig);
            var importer=(ModelImporter)AssetImporter.GetAtPath(CanonicalRig);
            importer.animationType=ModelImporterAnimationType.Human;
            importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
            var hd=importer.humanDescription;
            hd.human=HumanNames.Select((n,i)=>new HumanBone{humanName=n,boneName="bone_"+BoneIds[i],limit=new HumanLimit{useDefaultValues=true}}).ToArray();
            hd.skeleton=model.GetComponentsInChildren<Transform>(true).Select(t=>new SkeletonBone{name=t.name,position=t.localPosition,rotation=t.localRotation,scale=t.localScale}).ToArray();
            hd.armStretch=hd.legStretch=0;hd.upperArmTwist=hd.lowerArmTwist=hd.upperLegTwist=hd.lowerLegTwist=.5f;hd.hasTranslationDoF=false;
            importer.humanDescription=hd;
            importer.importAnimation=false;
            DisableAutomaticMapping(importer);
            importer.SaveAndReimport();
            ValidateMapping(AssetDatabase.LoadAllAssetsAtPath(CanonicalRig).OfType<Avatar>().FirstOrDefault());
        }
        static void DisableAutomaticMapping(ModelImporter importer)
        {
            var serialized=new SerializedObject(importer);
            serialized.FindProperty("m_AutoGenerateAvatarMappingIfUnspecified").boolValue=false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        static void ValidateMapping(Avatar avatar)
        {
            if(!avatar || !avatar.isValid || !avatar.isHuman)throw new InvalidOperationException("共享T Pose Avatar无效。");
            var actual=avatar.humanDescription.human.ToDictionary(h=>h.humanName,h=>h.boneName);
            for(int i=0;i<HumanNames.Length;i++)
                if(!actual.TryGetValue(HumanNames[i],out string bone) || bone!="bone_"+BoneIds[i])
                    throw new InvalidOperationException("实际Avatar骨骼映射错误："+HumanNames[i]+"。请先ConfigureCanonicalAvatar。");
            if(actual.Count!=HumanNames.Length)throw new InvalidOperationException("Avatar含有未授权自动映射骨骼。");
        }

        public static void ConfigurePlayerAvatar()
        {
            RequireEdit();
            // 玩家专用配置；不修改其他角色共享的模型/Avatar。关闭自动前臂扭转分配，
            // 避免超过180度时的半角分支翻转，保留手掌姿态并贴近原生前臂轴向。
            var model=Required<GameObject>(TargetModel);
            var original=AssetDatabase.LoadAllAssetsAtPath(TargetModel).OfType<Avatar>().FirstOrDefault();
            if(!original || !original.isHuman || !original.isValid)throw new InvalidOperationException("玩家参考Avatar无效。");
            var preview=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            GameObject builder=null;Avatar rebuilt=null;
            try
            {
                builder=UnityEngine.Object.Instantiate(model);builder.name=model.name;
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(builder,preview);
                var hd=original.humanDescription;hd.lowerArmTwist=0f;
                var nodes=builder.GetComponentsInChildren<Transform>(true).GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
                foreach(var s in hd.skeleton)
                    if(nodes.TryGetValue(s.name,out var t)){t.localPosition=s.position;t.localRotation=s.rotation;t.localScale=s.scale;}
                foreach(var h in hd.human)
                    if(!nodes.ContainsKey(h.boneName))throw new InvalidOperationException("缺少玩家参考骨骼："+h.boneName);
                rebuilt=AvatarBuilder.BuildHumanAvatar(builder,hd);rebuilt.name="SamuraiNiohAvatar";
                if(!rebuilt.isValid || !rebuilt.isHuman)throw new InvalidOperationException("玩家专用Avatar构建失败。");
                Folder(Anim);
                var avatar=AssetDatabase.LoadAssetAtPath<Avatar>(PlayerAvatar);
                if(!avatar){avatar=rebuilt;AssetDatabase.CreateAsset(avatar,PlayerAvatar);rebuilt=null;}
                else{EditorUtility.CopySerialized(rebuilt,avatar);EditorUtility.SetDirty(avatar);AssetDatabase.SaveAssetIfDirty(avatar);}
                var contents=PrefabUtility.LoadPrefabContents(SamuraiCombatSetup.PlayerPath);
                try
                {
                    var animator=contents.GetComponentInChildren<Animator>(true);
                    if(!animator)throw new InvalidOperationException("玩家缺少Animator。");
                    if(animator.avatar!=avatar){animator.avatar=avatar;PrefabUtility.SaveAsPrefabAsset(contents,SamuraiCombatSetup.PlayerPath);}
                }
                finally{PrefabUtility.UnloadPrefabContents(contents);}
            }
            finally
            {
                if(rebuilt)UnityEngine.Object.DestroyImmediate(rebuilt);
                if(builder)UnityEngine.Object.DestroyImmediate(builder);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        static void RequireEdit()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出Play模式。");
        }
        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            Folder(Path.GetDirectoryName(path).Replace('\\','/'));
            AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'), Path.GetFileName(path));
        }
        static T Required<T>(string path) where T : UnityEngine.Object
        {
            var asset=AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset) throw new InvalidOperationException("缺少资源："+path);
            return asset;
        }

        public static string ImportOne(int index)
        {
            RequireEdit();
            Entry e=Entries[index];
            string path=Anim+"/SourceFBX/"+e.name+".fbx";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer=(ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType=ModelImporterAnimationType.Human;
            // 动作FBX的rest来自各自首帧，不能用来逐个创建Avatar。
            var avatar=AssetDatabase.LoadAllAssetsAtPath(CanonicalRig).OfType<Avatar>().FirstOrDefault();
            if (!avatar || !avatar.isValid || !avatar.isHuman)
                throw new InvalidOperationException("缺少有效的共享T Pose Avatar："+CanonicalRig);
            ValidateMapping(avatar);
            importer.avatarSetup=ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar=avatar;
            importer.humanDescription=avatar.humanDescription;
            DisableAutomaticMapping(importer);
            importer.animationCompression=ModelImporterAnimationCompression.Off;
            importer.importCameras=false;importer.importLights=false;
            importer.materialImportMode=ModelImporterMaterialImportMode.None;
            // 玩家容器由战斗系统驱动。转体和跳跃必须烘焙进姿态，
            // 否则关闭ApplyRootMotion时会丢失旋转，并按各首帧改变高度基准。
            var takes=importer.clipAnimations;
            if(takes.Length==0)takes=importer.defaultClipAnimations;
            foreach(var take in takes)
            {
                take.lockRootRotation=take.lockRootHeightY=take.lockRootPositionXZ=true;
                take.keepOriginalOrientation=take.keepOriginalPositionY=take.keepOriginalPositionXZ=true;
                take.heightFromFeet=false;take.heightOffset=take.rotationOffset=0;take.loopTime=false;
            }
            importer.clipAnimations=takes;
            importer.SaveAndReimport();
            var source=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c=>!c.name.StartsWith("__preview"));
            if (!avatar || !avatar.isValid || !avatar.isHuman || !source || !source.humanMotion)
                throw new InvalidOperationException("Humanoid导入失败："+e.code);
            Folder(Anim+"/Runtime");
            string runtimePath=Anim+"/Runtime/"+e.name+".anim";
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(runtimePath);
            // 每次由当前导入结果重建派生片段，同时保留已有GUID和招式调参。
            var rebuilt=UnityEngine.Object.Instantiate(source);rebuilt.name=e.name;
            try
            {
                // 只去除水平根平移；容器位移继续由现有战斗碰撞系统负责，保留升空与转体。
                foreach (var b in AnimationUtility.GetCurveBindings(rebuilt))
                    if(b.type==typeof(Animator) && (b.propertyName=="RootT.x" || b.propertyName=="RootT.z"))
                    {
                        var c=AnimationUtility.GetEditorCurve(rebuilt,b);var keys=c.keys;
                        for(int i=0;i<keys.Length;i++) { keys[i].value=0;keys[i].inTangent=keys[i].outTangent=0; }
                        c.keys=keys;AnimationUtility.SetEditorCurve(rebuilt,b,c);
                    }
                var settings=AnimationUtility.GetAnimationClipSettings(rebuilt);settings.loopTime=false;
                AnimationUtility.SetAnimationClipSettings(rebuilt,settings);rebuilt.frameRate=60;
                if (!clip) { clip=rebuilt;AssetDatabase.CreateAsset(clip,runtimePath);rebuilt=null; }
                else { EditorUtility.CopySerialized(rebuilt,clip);EditorUtility.SetDirty(clip);AssetDatabase.SaveAssetIfDirty(clip); }
            }
            finally { if(rebuilt)UnityEngine.Object.DestroyImmediate(rebuilt); }
            Folder(Config);
            string actionPath=Config+"/"+e.name+".asset";
            var action=AssetDatabase.LoadAssetAtPath<SamuraiActionDefinition>(actionPath);
            if(!action)
            {
                action=ScriptableObject.CreateInstance<SamuraiActionDefinition>();action.name=e.name;
                action.displayName=e.label;action.isMartialArt=e.martialArt;
                action.damageWindows=e.windows;
                action.damageStart=e.windows[0].x;action.damageEnd=e.windows[e.windows.Length-1].y;
                // 下列伤害/耗精/判定均为本项目初值，并非原游戏战斗参数。
                action.damage=(e.martialArt?36f:22f)/e.windows.Length;
                action.staminaDamage=(e.code=="07600"?42f:e.martialArt?36f:25f)/e.windows.Length;
                action.staminaCost=e.martialArt?22f:14f;
                action.hitSource=e.code=="07600"?SamuraiHitSource.LeftFoot:SamuraiHitSource.Weapon;
                action.hitRadius=e.code=="07600"?.14f:0f;
                action.comboInputStart=.08f;action.comboInputEnd=.85f;action.comboLink=e.link;
                action.cancelStart=Mathf.Max(action.damageEnd,.55f);
                action.stepDistance=e.code=="07600"?.08f:.22f;
                action.stepStart=.06f;action.stepEnd=Mathf.Max(.08f,action.damageEnd);
                SetRemainingAttackStepDefaults(e.code,action);
                SetHighArtApproachDefaults(e.code,action);
                action.invulnerableStart=action.invulnerableEnd=0;
                SamuraiAttackKnockbackSetup.InitializeNew(action);
                AssetDatabase.CreateAsset(action,actionPath);
            }
            action.clip=clip;action.stateName=e.name;
            SamuraiToughnessSetup.InitializeAction(action);
            EditorUtility.SetDirty(action);AssetDatabase.SaveAssetIfDirty(action);
            return e.code+": Humanoid valid, "+clip.length.ToString("F3")+"s";
        }

        [MenuItem("Katana/Samurai/Player/Install Selected Nioh3 Actions")]
        public static void Install()
        {
            RequireEdit();
            ConfigurePlayerAvatar();
            var entries=Entries;
            var actions=entries.ToDictionary(e=>e.code,e=>Required<SamuraiActionDefinition>(Config+"/"+e.name+".asset"));
            var player=Required<SamuraiCombatProfile>(Root+"/Config/SamuraiPlayer.asset");
            var controller=Required<AnimatorController>(Root+"/Animations/SamuraiCombat.controller");
            var machine=controller.layers[0].stateMachine;
            foreach(var e in entries)
            {
                var action=actions[e.code];var state=machine.states.FirstOrDefault(s=>s.state.name==e.name).state;
                if(!state)state=machine.AddState(e.name,new Vector3(280*(machine.states.Length%5),90*(machine.states.Length/5)));
                state.motion=action.clip;state.speed=1;state.iKOnFeet=false;EditorUtility.SetDirty(state);
            }
            EditorUtility.SetDirty(machine);EditorUtility.SetDirty(controller);AssetDatabase.SaveAssetIfDirty(controller);
            var high=CreateStance("High","高架势",new[]{new[]{"05180"}},new[]{"05080","07600","09300","16000"},actions);
            RestoreHighNormalChain(high,player);
            var mid=CreateStance("Mid","中架势",new[]{SamuraiMidSixComboSetup.Codes},new[]{"05160","09210"},actions);
            if(player.stances==null || player.stances.Length==0)
            { player.stances=new[]{high,mid};player.defaultStanceIndex=1;EditorUtility.SetDirty(player);AssetDatabase.SaveAssetIfDirty(player); }
            var contents=PrefabUtility.LoadPrefabContents(SamuraiCombatSetup.PlayerPath);
            try
            {
                var ribbon=contents.GetComponent<SamuraiBladeRibbon>();bool changed=false;
                if(ribbon)foreach(var action in actions.Values)
                    if(action.hitSource==SamuraiHitSource.Weapon)
                        changed|=SamuraiBladeVfxSetup.EnsureAttackProfile(ribbon,action,.10f,1f,1f);
                if(changed)PrefabUtility.SaveAsPrefabAsset(contents,SamuraiCombatSetup.PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }
        static SamuraiStanceDefinition CreateStance(string id,string label,string[][] chains,string[] skills,System.Collections.Generic.Dictionary<string,SamuraiActionDefinition> actions)
        {
            string path=Config+"/Nioh3_Stance_"+id+".asset";
            var stance=AssetDatabase.LoadAssetAtPath<SamuraiStanceDefinition>(path);
            if(stance)return stance;
            stance=ScriptableObject.CreateInstance<SamuraiStanceDefinition>();stance.displayName=label;
            stance.attackChains=chains.Select(c=>c.SequenceEqual(SamuraiMidSixComboSetup.Codes)
                ? SamuraiMidSixComboSetup.CreateChain(actions)
                : new SamuraiAttackChain{displayName=string.Join(" → ",c.Select(code=>actions[code].DisplayName)),attacks=c.Select(code=>actions[code]).ToArray()}).ToArray();
            stance.martialArts=skills.Select(code=>actions[code]).ToArray();
            AssetDatabase.CreateAsset(stance,path);return stance;
        }

        // Explicit migration: movement only, without reimporting animations or
        // overwriting the existing damage, hit windows, stamina, or Avatar.
        public static void RestoreHighComboAndConfigureApproach()
        {
            RequireEdit();
            var player=Required<SamuraiCombatProfile>(Root+"/Config/SamuraiPlayer.asset");
            var high=Required<SamuraiStanceDefinition>(Config+"/Nioh3_Stance_High.asset");
            RestoreHighNormalChain(high,player);
            foreach(var e in Entries)
            {
                if(e.code!="05080" && e.code!="07600" && e.code!="09300" && e.code!="16000")continue;
                var action=Required<SamuraiActionDefinition>(Config+"/"+e.name+".asset");
                SetHighArtApproachDefaults(e.code,action);
                EditorUtility.SetDirty(action);AssetDatabase.SaveAssetIfDirty(action);
            }
        }

        static void RestoreHighNormalChain(SamuraiStanceDefinition high,SamuraiCombatProfile player)
        {
            if(SamuraiNativeHighSetup.ApplyPreparedAttacks(player, high))return;
            if(player.combo==null || player.combo.Length!=4 || player.combo.Any(a=>!a))
                throw new InvalidOperationException("原高架势四连资源不完整，停止修改。");
            var chains=high.attackChains ?? new SamuraiAttackChain[0];
            if(chains.Length>0 && chains[0]!=null && chains[0].attacks!=null && chains[0].attacks.SequenceEqual(player.combo))return;
            // Retain the rising slash and any other configured alternate groups.
            high.attackChains=new[]{new SamuraiAttackChain{displayName="普通攻击 1 → 2 → 3 → 4",attacks=player.combo.ToArray()}}
                .Concat(chains.Where(c=>c==null || c.attacks==null || !c.attacks.SequenceEqual(player.combo))).ToArray();
            EditorUtility.SetDirty(high);AssetDatabase.SaveAssetIfDirty(high);
        }

        // Project-authored approach steps for the remaining in-place clips. Most
        // travel happens before the first strike; existing assets retain their tuning.
        static void SetRemainingAttackStepDefaults(string code,SamuraiActionDefinition action)
        {
            switch(code)
            {
                case "05160": action.stepDistance=1.25f;action.stepStart=.04f;action.stepEnd=.24f;break;
                case "05180": action.stepDistance=.9f;action.stepStart=.02f;action.stepEnd=.20f;break;
                case "09210": action.stepDistance=1.2f;action.stepStart=.08f;action.stepEnd=.31f;break;
                case "09400": action.stepDistance=1.2f;action.stepStart=.04f;action.stepEnd=.26f;break;
                case "09600": action.stepDistance=.9f;action.stepStart=.04f;action.stepEnd=.20f;break;
                case "09602": action.stepDistance=1.05f;action.stepStart=.06f;action.stepEnd=.23f;break;
                case "16200": action.stepDistance=.85f;action.stepStart=.03f;action.stepEnd=.23f;break;
                case "09200": action.stepDistance=.9f;action.stepStart=.03f;action.stepEnd=.25f;break;
                case "09201": action.stepDistance=.7f;action.stepStart=.01f;action.stepEnd=.12f;break;
                case "09202": action.stepDistance=.85f;action.stepStart=.02f;action.stepEnd=.22f;break;
                case "09203": action.stepDistance=.75f;action.stepStart=.01f;action.stepEnd=.16f;break;
                case "09204": action.stepDistance=.9f;action.stepStart=.04f;action.stepEnd=.27f;break;
                case "09205": action.stepDistance=.85f;action.stepStart=.03f;action.stepEnd=.21f;break;
            }
            SamuraiMidSixComboSetup.ConfigureCloseContact(code, action);
        }

        static void SetHighArtApproachDefaults(string code,SamuraiActionDefinition action)
        {
            switch(code)
            {
                case "05080": action.stepDistance=2.5f;ConfigureWhirlwindMoveWindow(action);break;
                case "07600": action.stepDistance=.9f;action.stepStart=.04f;action.stepEnd=.24f;break;
                case "09300": action.stepDistance=2f;action.stepStart=.02f;action.stepEnd=.19f;break;
                case "16000": action.stepDistance=2.2f;action.stepStart=.17f;action.stepEnd=.40f;break;
                default:return;
            }
            action.approachEnabled=true;
            action.approachStopGap=code=="07600"?.2f:.4f;
            action.approachSlowSpeed=.25f;
        }

        internal static void ConfigureWhirlwindMoveWindow(SamuraiActionDefinition action)
        {
            // 131 frame intervals at 60 fps: wind-up stays still; body spin spans frames 33–72.
            action.stepStart = 33f / 131f;
            action.stepEnd = 72f / 131f;
            action.pushEnemies = true;
            // Fill only the inner forward arc, clipped by each independent damage window.
            action.closeRangeReach = .65f;
            action.closeRangeHalfAngle = 75f;
            action.closeRangeStart = 33f / 131f;
            action.closeRangeEnd = 72f / 131f;
        }
    }
}
