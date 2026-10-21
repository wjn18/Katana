using System;
using System.IO;
using System.Linq;
using OCDesign.Samurai;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OCDesign.EditorTools
{
    /// <summary>Independent ARPG Samurai module. Never rewrites source characters or animation package.</summary>
    public static class SamuraiCombatSetup
    {
        public const string Root = "Assets/KatanaDemo/SamuraiCombat";
        public const string ScenePath = Root + "/Scenes/SamuraiCombatDemo.unity";
        public const string PlayerPath = Root + "/Prefabs/SamuraiCombatPlayer.prefab";
        public const string EnemyPath = Root + "/Prefabs/SamuraiTrainingOpponent.prefab";
        public const string WeaponPath = Root + "/Prefabs/SamuraiUchigatana.prefab";
        const string HumanoidSource = "Assets/KatanaDemo/NaginataCombat/Prefabs/NaginataCombatPlayer.prefab";
        const string SwordSource = "Assets/KatanaDemo/UchigatanaCombat/Prefabs/UchigatanaCombatPlayer.prefab";
        static readonly string[] DodgeNames = {"Forward","ForwardRight","Right","BackwardRight","Backward","BackwardLeft","Left","ForwardLeft"};

        [MenuItem("OC Design/Samurai/Create Independent Combat Demo")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            for (int i=0;i<SceneManager.sceneCount;i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save your current scene before building.");
            foreach (string p in new[]{"Animations","Config","Prefabs","Scenes","Materials"}) EnsureFolder(Root+"/"+p);
            var profile=BuildProfile();
            var enemyProfile=LoadOrCreate<SamuraiCombatProfile>(Root+"/Config/TrainingOpponent.asset");
            EditorUtility.CopySerialized(profile,enemyProfile);
            enemyProfile.name="TrainingOpponent";enemyProfile.maxHealth=220;enemyProfile.maxStamina=85;
            enemyProfile.walkSpeed=1.85f;enemyProfile.runSpeed=2.72f;
            // 玩家起手跨步更远；训练敌人继续使用独立的短步配置与同一动画状态。
            var enemyOpening=LoadOrCreate<SamuraiActionDefinition>(Root+"/Config/TrainingCombo1.asset");
            EditorUtility.CopySerialized(profile.combo[0],enemyOpening);
            enemyOpening.name="TrainingCombo1";enemyOpening.stepDistance=.30f;
            enemyProfile.combo=(SamuraiActionDefinition[])profile.combo.Clone();
            enemyProfile.combo[0]=enemyOpening;
            EditorUtility.SetDirty(enemyOpening);AssetDatabase.SaveAssetIfDirty(enemyOpening);
            EditorUtility.SetDirty(enemyProfile);AssetDatabase.SaveAssetIfDirty(enemyProfile);
            var controller=BuildController(profile);
            BuildWeapon();
            BuildActor(PlayerPath,"SamuraiCombatPlayer",profile,controller,0);
            BuildActor(EnemyPath,"SamuraiTrainingOpponent",enemyProfile,controller,1);
            SamuraiSprintCombatSetup.InstallIfPrepared();
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath)) BuildScene();
            EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
            Selection.activeObject=AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        }

        static void EnsureFolder(string path)
        {
            if(AssetDatabase.IsValidFolder(path))return;
            string parent=path.Substring(0,path.LastIndexOf('/'));EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent,path.Substring(path.LastIndexOf('/')+1));
        }
        static T LoadOrCreate<T>(string path) where T:ScriptableObject
        {
            var item=AssetDatabase.LoadAssetAtPath<T>(path);if(item)return item;
            item=ScriptableObject.CreateInstance<T>();AssetDatabase.CreateAsset(item,path);return item;
        }
        static AnimationClip Clip(string n)
        {
            var c=AssetDatabase.LoadAssetAtPath<AnimationClip>(Root+"/Animations/Samurai_"+n+".anim");
            if(!c || !c.humanMotion)throw new InvalidOperationException("Missing Blender-prepared humanoid clip: "+n);
            return c;
        }
        static SamuraiActionDefinition Action(string name,float damage,float cost,float start,float end,float link,float cancel,float distance)
        {
            var a=LoadOrCreate<SamuraiActionDefinition>(Root+"/Config/"+name+".asset");
            a.clip=Clip(name);a.stateName=name;a.damage=damage;a.staminaDamage=damage*1.45f;a.staminaCost=cost;
            a.damageStart=start;a.damageEnd=end;a.comboInputStart=.06f;a.comboInputEnd=link+.08f;
            a.comboLink=link;a.cancelStart=cancel;a.stepDistance=distance;a.stepStart=.04f;a.stepEnd=Mathf.Max(.15f,end);
            a.unblockable=name=="Charged";
            a.closeRangeReach=name=="Charged"?.8f:0f;
            a.closeRangeHalfAngle=55f;a.closeRangeStart=start;a.closeRangeEnd=end;
            EditorUtility.SetDirty(a);AssetDatabase.SaveAssetIfDirty(a);return a;
        }
        static SamuraiCombatProfile BuildProfile()
        {
            var p=LoadOrCreate<SamuraiCombatProfile>(Root+"/Config/SamuraiPlayer.asset");
            p.idle=Clip("Idle");p.walk=Clip("Walk");p.run=Clip("Run");p.guard=Clip("Guard");p.guardHit=Clip("GuardHit");
            p.guardBreak=Clip("GuardBreak");p.hit=Clip("Hit");p.death=Clip("Death");p.charge=Clip("Charge");
            p.chargeSeconds=1.5f;p.transitionSeconds=.055f;p.guardWalkSpeed=0f;
            p.walkSpeed=3f;p.runSpeed=5f;
            ConfigureDefenseClips(p);
            p.combo=new[]{
                Action("Combo1",20,12,.12f,.34f,.367347f,.40f,1f),
                Action("Combo2",24,14,.17f,.46f,.50f,.53f,.38f),
                Action("Combo3",27,16,.11f,.39f,.423077f,.46f,.42f),
                Action("Combo4",35,19,.12f,.48f,.76f,.56f,.50f)};
            p.heavy=Action("Heavy",40,26,.16f,.43f,.75f,.56f,.55f);
            p.charged=Action("Charged",65,38,.10f,.42f,.75f,.53f,.90f);
            p.dodges=new SamuraiActionDefinition[8];
            for(int i=0;i<8;i++)
            {
                var d=Action("Dodge"+DodgeNames[i],0,22,1,1,1,.72f,2.3f);
                d.stepStart=.03f;d.stepEnd=.59f;d.invulnerableStart=.08f;d.invulnerableEnd=.55f;
                EditorUtility.SetDirty(d);AssetDatabase.SaveAssetIfDirty(d);p.dodges[i]=d;
            }
            EditorUtility.SetDirty(p);AssetDatabase.SaveAssetIfDirty(p);return p;
        }
        public static void ConfigureDefenseClips(SamuraiCombatProfile p)
        {
            p.parry=Clip("Parry");
            p.directionalWalk=DodgeNames.Select((d,i)=>i==0?p.walk:Clip("Walk"+d)).ToArray();
            p.directionalRun=DodgeNames.Select((d,i)=>i==0?p.run:Clip("Run"+d)).ToArray();
            p.parryActiveSeconds=.20f;p.parryCooldownSeconds=.7f;
            p.parryStunSeconds=1.1f;p.parrySuccessRecoverySeconds=.28f;
        }
        static AnimatorController BuildController(SamuraiCombatProfile p)
        {
            string path=Root+"/Animations/SamuraiCombat.controller";
            var c=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if(!c)c=AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm=c.layers[0].stateMachine;
            string[] names={"Idle","Walk","Run","Guard","GuardHit","GuardBreak","Hit","Death","Charge","Parry"};
            AnimationClip[] clips={p.idle,p.walk,p.run,p.guard,p.guardHit,p.guardBreak,p.hit,p.death,p.charge,p.parry};
            for(int i=0;i<names.Length;i++)SetState(sm,names[i],clips[i],i);
            for(int i=1;i<DodgeNames.Length;i++)
            {
                SetState(sm,"Walk"+DodgeNames[i],p.directionalWalk[i],sm.states.Length);
                SetState(sm,"Run"+DodgeNames[i],p.directionalRun[i],sm.states.Length);
            }
            foreach(var a in p.combo.Concat(new[]{p.heavy,p.charged}).Concat(p.dodges))SetState(sm,a.stateName,a.clip,sm.states.Length);
            sm.defaultState=sm.states.First(s=>s.state.name=="Idle").state;
            EditorUtility.SetDirty(c);AssetDatabase.SaveAssetIfDirty(c);return c;
        }
        static void SetState(AnimatorStateMachine sm,string name,AnimationClip clip,int index)
        {
            var state=sm.states.FirstOrDefault(s=>s.state.name==name).state;
            if(!state)state=sm.AddState(name,new Vector3(240*(index%5),90*(index/5)));
            state.motion=clip;state.speed=1;state.iKOnFeet=false;state.writeDefaultValues=true;
        }
        static void BuildWeapon()
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(SwordSource);
            var grip=source.GetComponentsInChildren<Transform>(true).First(t=>t.name=="UchigatanaGrip");
            var go=UnityEngine.Object.Instantiate(grip.GetChild(0).gameObject);go.name="SamuraiUchigatana";
            go.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);go.transform.localScale=Vector3.one;
            PrefabUtility.SaveAsPrefabAsset(go,WeaponPath);UnityEngine.Object.DestroyImmediate(go);
        }
        static void BuildActor(string path,string name,SamuraiCombatProfile profile,RuntimeAnimatorController controller,int team)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(HumanoidSource);
            var root=new GameObject(name);
            try
            {
                var visual=UnityEngine.Object.Instantiate(source.GetComponentInChildren<Animator>().gameObject,root.transform);
                visual.name="SamuraiVisual";visual.transform.localPosition=new Vector3(0,.017f,0);visual.transform.localRotation=Quaternion.identity;
                foreach(var c in visual.GetComponentsInChildren<MonoBehaviour>(true))UnityEngine.Object.DestroyImmediate(c);
                foreach(var t in visual.GetComponentsInChildren<Transform>(true))
                    if(t && t.name=="NaginataGrip")UnityEngine.Object.DestroyImmediate(t.gameObject);
                var animator=visual.GetComponent<Animator>();animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;
                animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.updateMode=AnimatorUpdateMode.Normal;
                var grip=new GameObject("SamuraiGrip").transform;grip.SetParent(animator.GetBoneTransform(HumanBodyBones.RightHand),false);
                var sourceGrip=AssetDatabase.LoadAssetAtPath<GameObject>(SwordSource).GetComponentsInChildren<Transform>(true).First(t=>t.name=="UchigatanaGrip");
                grip.localPosition=sourceGrip.localPosition;grip.localRotation=sourceGrip.localRotation;grip.localScale=sourceGrip.localScale;
                var weapon=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(WeaponPath));
                weapon.transform.SetParent(grip,false);
                var b=new GameObject("BladeBase").transform;b.SetParent(grip,false);b.localPosition=new Vector3(0,.15f,0);
                var tip=new GameObject("BladeTip").transform;tip.SetParent(grip,false);tip.localPosition=new Vector3(.078f,.64f,0);
                var motor=root.AddComponent<CharacterController>();motor.height=1.5f;motor.center=new Vector3(0,.75f,0);motor.radius=.26f;
                motor.skinWidth=.025f;motor.stepOffset=.25f;motor.slopeLimit=50;motor.minMoveDistance=0;
                root.AddComponent<WeaponTrailGizmos>().Configure(b,tip,profile.bladeRadius);
                var actor=root.AddComponent<SamuraiCombatant>();actor.Configure(animator,motor,profile,b,tip,team);
                if(team==0)
                {
                    var targetLock=root.AddComponent<SamuraiTargetLock>();targetLock.Configure(actor,null);
                    root.AddComponent<SamuraiPlayerInput>().Configure(actor,null,null,null,targetLock);
                }
                else root.AddComponent<SamuraiTrainingEnemy>().Configure(actor,null);
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        static Material Mat(string name,Color color)
        {
            var path=Root+"/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m)return m;
            m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",.16f);
            AssetDatabase.CreateAsset(m,path);return m;
        }
        static GameObject Block(string name,Vector3 position,Vector3 scale,Material material,bool collision=true)
        {
            var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.position=position;g.transform.localScale=scale;
            g.GetComponent<Renderer>().sharedMaterial=material;if(!collision)UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());return g;
        }
        static void BuildScene()
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
            var floor=Mat("TrainingStone",new Color(.17f,.22f,.25f));var lines=Mat("ArenaMarkings",new Color(.39f,.48f,.47f));var wall=Mat("BoundaryStone",new Color(.29f,.32f,.34f));
            Block("Training Floor",new Vector3(0,-.15f,0),new Vector3(24,.3f,24),floor);
            for(int i=-10;i<=10;i+=2){Block("Grid X",new Vector3(i,.003f,0),new Vector3(.018f,.004f,22),lines,false);Block("Grid Z",new Vector3(0,.003f,i),new Vector3(22,.004f,.018f),lines,false);}
            Block("North Wall",new Vector3(0,.7f,12),new Vector3(24,1.4f,.4f),wall);
            Block("South Wall",new Vector3(0,.7f,-12),new Vector3(24,1.4f,.4f),wall);
            Block("West Wall",new Vector3(-12,.7f,0),new Vector3(.4f,1.4f,24),wall);
            Block("East Wall",new Vector3(12,.7f,0),new Vector3(.4f,1.4f,24),wall);
            Block("Collision Practice Pillar",new Vector3(-3,.9f,2),new Vector3(.7f,1.8f,.7f),wall);
            var p=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath));p.transform.position=new Vector3(0,.04f,-2);
            var e=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPath));e.transform.position=new Vector3(0,.04f,2);e.transform.rotation=Quaternion.Euler(0,180,0);
            var camera=new GameObject("Main Camera").AddComponent<Camera>();camera.tag="MainCamera";camera.fieldOfView=48;camera.nearClipPlane=.08f;camera.farClipPlane=100;
            camera.backgroundColor=new Color(.10f,.14f,.18f);camera.clearFlags=CameraClearFlags.SolidColor;camera.gameObject.AddComponent<AudioListener>();
            var orbit=camera.gameObject.AddComponent<SamuraiOrbitCamera>();orbit.distance=4.4f;orbit.initialPitch=24;
            var input=p.GetComponent<SamuraiPlayerInput>();var enemy=e.GetComponent<SamuraiTrainingEnemy>();
            input.Configure(p.GetComponent<SamuraiCombatant>(),camera.transform,orbit,enemy);
            enemy.Configure(e.GetComponent<SamuraiCombatant>(),p.GetComponent<SamuraiCombatant>(),input);
            orbit.Configure(p.transform,input);
            var ui=new GameObject("Combat HUD").AddComponent<SamuraiCombatHUD>();ui.Configure(p.GetComponent<SamuraiCombatant>(),enemy,input,camera);
            var light=new GameObject("Sun").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.shadows=LightShadows.Soft;light.transform.rotation=Quaternion.Euler(48,-35,0);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.55f,.61f,.67f);
            // ResetView initializes gameplay pivot. Set an edit-mode framing as well.
            camera.transform.position=p.transform.position+new Vector3(0,2.8f,-4.5f);camera.transform.LookAt(p.transform.position+Vector3.up);
            PrefabUtility.RecordPrefabInstancePropertyModifications(input);PrefabUtility.RecordPrefabInstancePropertyModifications(enemy);
            EditorSceneManager.SaveScene(scene,ScenePath);
        }
    }
}
