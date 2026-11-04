using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;

namespace OCDesign.Samurai.EditorTools
{
    /// <summary>Paused-Play isolated actors, 1km from the user's actors. Never saves a scene or source asset.</summary>
    public static class SamuraiExecutionValidation
    {
        static GameObject root;
        public static SamuraiCombatant Player,Enemy,Elite;
        public static SamuraiCombatCameraFeedback Feedback;
        public static Camera View;
        static readonly Vector3 Origin=new Vector3(1000,0,1000);
        static T Field<T>(object o,string field)=>(T)o.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);
        static SamuraiActionController Actions(SamuraiCombatant actor)=>Field<SamuraiActionController>(actor,"actions");
        static SamuraiVitals Vitals(SamuraiCombatant actor)=>Field<SamuraiVitals>(actor,"vitals");
        public static string Setup()
        {
            if(!Application.isPlaying||!EditorApplication.isPaused)throw new InvalidOperationException("Pause Play first.");
            if(root!=null)UnityEngine.Object.DestroyImmediate(root);
            root=new GameObject("Execution validation temporary");
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Validation floor";floor.transform.SetParent(root.transform);floor.transform.position=Origin+Vector3.down*.5f;floor.transform.localScale=new Vector3(20,1,20);
            Player=Clone("Assets/KatanaDemo/SamuraiCombat/Prefabs/SamuraiCombatPlayer.prefab","Execution test player");
            Enemy=Clone("Assets/KatanaDemo/Characters/Enemy1/Prefabs/Enemy1.prefab","Execution test Enemy1");
            Elite=Clone("Assets/KatanaDemo/Characters/Elite1/Prefabs/Elite1.prefab","Execution test Elite1");Elite.transform.position=Origin+Vector3.right*7;
            var camera=new GameObject("Execution validation camera");camera.transform.SetParent(root.transform);View=camera.AddComponent<Camera>();View.enabled=false;View.fieldOfView=35;View.clearFlags=CameraClearFlags.SolidColor;View.backgroundColor=new Color(.12f,.15f,.18f);
            var orbit=camera.AddComponent<SamuraiOrbitCamera>();orbit.Configure(Player.transform);orbit.enabled=true;
            Feedback=camera.AddComponent<SamuraiCombatCameraFeedback>();Feedback.Configure(orbit,Player);
            Prepare(120,true);return "Validation actors created in paused Play; original scene actors unchanged.";
        }
        static SamuraiCombatant Clone(string path,string name)
        {
            var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path),root.transform);go.name=name;
            foreach(var b in go.GetComponentsInChildren<MonoBehaviour>(true))if(!(b is SamuraiCombatant))b.enabled=false;
            var actor=go.GetComponent<SamuraiCombatant>();actor.ResetCombat();return actor;
        }
        public static string Prepare(float health,bool healthy)
        {
            Player.ResetCombat();Enemy.ResetCombat();Feedback.ClearFeedback();
            Player.Motor.enabled=false;Enemy.Motor.enabled=false;
            Player.transform.SetPositionAndRotation(Origin+Vector3.up*.005f,Quaternion.identity);
            Enemy.transform.SetPositionAndRotation(Origin+new Vector3(0,.005f,1.10f),Quaternion.Euler(0,180,0));
            Player.Motor.enabled=Enemy.Motor.enabled=true;Player.Movement.CaptureSpawn();Enemy.Movement.CaptureSpawn();
            Vitals(Enemy).Damage(healthy?0:120);Actions(Enemy).ReactToToughnessBreak();
            if(Enemy.Health>health)Vitals(Enemy).Damage(Enemy.Health-health);
            Player.Animator.Update(0);Enemy.Animator.Update(0);Physics.SyncTransforms();
            return Enemy.StateName+" HP="+Enemy.Health+" eligible="+Player.CanExecuteTarget(Enemy);
        }
        public static void Step(float dt)
        {
            Player.Tick(default(SamuraiInputFrame),dt);Enemy.Tick(default(SamuraiInputFrame),dt);
            if(Player.isActiveAndEnabled&&!Player.IsExecuting)Player.Animator.Update(dt);
            if(Enemy.isActiveAndEnabled&&!Enemy.IsBeingExecuted)Enemy.Animator.Update(dt);
            Physics.SyncTransforms();Feedback.SampleNow(dt,dt);
        }
        public static string RunMatrix(string outputPath)
        {
            var results=new List<object>();int assertions=0;var failures=new List<string>();
            Action<bool,string> check=(ok,name)=>{assertions++;if(!ok)failures.Add(name);};
            foreach(int hz in new[]{15,30,120})foreach(bool healthy in new[]{true,false})foreach(bool lethal in new[]{false,true})
            {
                float health=lethal?45:(healthy?120:60);Prepare(health,healthy);int damageEvents=0,deaths=0;
                Action<SamuraiDamageFeedback> damaged=_=>damageEvents++;Action died=()=>deaths++;Enemy.DamageReceived+=damaged;Enemy.Died+=died;
                int bursts=SamuraiExecutionBlood.BurstCount,shakes=Feedback.ExecutionShakeCount;
                string label=hz+"Hz "+healthy+" lethal="+lethal;
                bool started=Player.TryExecute(Enemy);check(started,label+" starts");var selected=Player.ActiveExecutionPair;
                if(!started){Enemy.DamageReceived-=damaged;Enemy.Died-=died;results.Add(new {label,started});continue;}
                check(selected==(lethal?Enemy.Profile.executionLethalPair:Enemy.Profile.executionSurvivingPair),label+" pair");
                string recovery=null;bool killedInPair=false;float firstDamage=-1;
                for(int f=0;f<hz*8;f++)
                {
                    Step(1f/hz);
                    if(damageEvents>0&&firstDamage<0)firstDamage=(f+1f)/hz;
                    if(Enemy.IsBeingExecuted&&Enemy.IsDead)killedInPair=true;
                    if(Actions(Enemy).IsGettingUp)recovery=Enemy.StateName;
                    if(f==0)check(Enemy.ReceiveHit(10,0,Player.transform.position,Player)==SamuraiHitResult.Ignored,label+" protected pair");
                }
                check(Mathf.Abs(Enemy.Health-Mathf.Max(0,health-45))<.001f,label+" exact damage");check(damageEvents==1,label+" single damage");
                check(deaths==(lethal?1:0),label+" death count");check(SamuraiExecutionBlood.BurstCount-bursts==1,label+" one blood burst");check(Feedback.ExecutionShakeCount-shakes==1,label+" one camera impulse");
                check(!Player.IsExecuting&&!Enemy.IsBeingExecuted,label+" pair released");check(Player.Animator.speed==1&&Enemy.Animator.speed==1,label+" speed restored");
                check(firstDamage>=selected.alignSeconds+selected.strikeSeconds-.001f&&firstDamage<=selected.alignSeconds+selected.strikeSeconds+1.1f/hz,label+" contact clock");
                if(lethal){check(killedInPair,label+" death keeps V2");check(Enemy.IsDead&&!Enemy.Motor.enabled,label+" corpse");check(recovery==null,label+" no lethal getup");}
                else {check(recovery==(healthy?"ExecutionGetupHealthy":"ExecutionGetupLowHealth"),label+" cached R7/R8");check(Enemy.CanAct,label+" recovered");}
                Enemy.DamageReceived-=damaged;Enemy.Died-=died;
                results.Add(new {label,started,firstDamage,health=Enemy.Health,state=Enemy.StateName,recovery,damageEvents,deaths});
            }
            Prepare(120,true);Actions(Elite).ReactToToughnessBreak();check(!Player.CanExecuteTarget(Elite)&&Elite.HasExecutionReceiver,"Elite separate receiver still respects range");
            foreach(bool after in new[]{false,true})
            {Prepare(120,true);Player.TryExecute(Enemy);for(int i=0;i<(after?80:10);i++)Step(.01f);float health=Enemy.Health;Player.ResetCombat();check(!Enemy.IsBeingExecuted&&Enemy.Animator.speed==1,"attacker reset releases receiver "+after);check(Enemy.Health==health,"reset no extra damage "+after);}
            Prepare(120,true);Player.TryExecute(Enemy);Enemy.gameObject.SetActive(false);Step(.01f);check(!Player.IsExecuting&&Player.Animator.speed==1,"receiver disable releases attacker");Enemy.gameObject.SetActive(true);
            Prepare(120,true);var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.SetParent(root.transform);wall.transform.position=Origin+new Vector3(0,1,.65f);wall.transform.localScale=new Vector3(3,2,.15f);Physics.SyncTransforms();check(!Player.TryExecute(Enemy),"wall blocks pair");UnityEngine.Object.DestroyImmediate(wall);
            Prepare(120,true);Player.TryExecute(Enemy);float elapsed=Player.ExecutionElapsed,oldHealth=Enemy.Health;for(int i=0;i<20;i++)Step(0);check(Player.ExecutionElapsed==elapsed&&Enemy.Health==oldHealth,"zero delta freezes pair");Player.ResetCombat();
            var report=new {assertions,passed=failures.Count==0,failures,results};var json=Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented);System.IO.File.WriteAllText(outputPath,json);return json;
        }
        public static string Render(float visualBloodTime,string path)
        {
            foreach(var effect in UnityEngine.Object.FindObjectsByType<SamuraiExecutionBlood>(FindObjectsSortMode.None))effect.SendMessage("Draw",visualBloodTime);
            View.transform.position=Origin+new Vector3(3.6f,2.3f,3.5f);View.transform.LookAt(Origin+new Vector3(0,.8f,.6f));
            var rt=new RenderTexture(1200,900,24);rt.Create();var old=RenderTexture.active;View.targetTexture=rt;View.Render();RenderTexture.active=rt;
            var image=new Texture2D(1200,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1200,900),0,0);image.Apply();System.IO.File.WriteAllBytes(path,image.EncodeToPNG());RenderTexture.active=old;View.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);return path;
        }
    }
}
