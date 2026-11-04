using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OCDesign.Samurai.EditorTools
{
    public static class SamuraiExecutionSetup
    {
        const string Root="Assets/KatanaDemo/SamuraiCombat";
        const string Anim=Root+"/Animations/Execution";
        [MenuItem("OC Design/Samurai Combat/Install Enemy1 Paired Executions")]
        public static void Install()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Exit Play before installation.");
            EnsureFolder(Root+"/Config/Execution");EnsureFolder(Root+"/Presentation/Execution");
            var player=AssetDatabase.LoadAssetAtPath<SamuraiCombatProfile>(Root+"/Config/SamuraiPlayer.asset");
            var enemy=AssetDatabase.LoadAssetAtPath<SamuraiCombatProfile>(Root+"/Config/Enemy1/Enemy1.asset");
            var pc=AssetDatabase.LoadAssetAtPath<AnimatorController>(Root+"/Animations/SamuraiCombat.controller");
            var ec=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/KatanaDemo/Characters/Enemy1/Animations/Generic/Combat/Enemy1Combat.controller");
            var material=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Presentation/Execution/ExecutionBlood.mat");
            if(material==null){material=new Material(Shader.Find("OC Design/Execution Blood"));material.name="ExecutionBlood";AssetDatabase.CreateAsset(material,Root+"/Presentation/Execution/ExecutionBlood.mat");}
            SamuraiExecutionPair[] pairs=new SamuraiExecutionPair[2];
            for(int i=0;i<2;i++)
            {
                string code=i==0?"09600":"09702",state="Execution"+code;
                string path=Root+"/Config/Execution/Enemy1_"+code+"_V"+(i+1)+".asset";
                var pair=AssetDatabase.LoadAssetAtPath<SamuraiExecutionPair>(path);bool created=pair==null;
                if(created){pair=ScriptableObject.CreateInstance<SamuraiExecutionPair>();pair.name="Enemy1_"+code+"_V"+(i+1);}
                if(created)
                {
                    pair.attackerClip=Required(Anim+"/"+state+".anim");pair.attackerState=state;
                    pair.receiverClip=Required(Anim+"/Enemy1/Enemy1_ExecutionV"+(i+1)+"Healthy.anim");pair.receiverState="ExecutionV"+(i+1)+"Healthy";
                    pair.receiverLowHealthClip=Required(Anim+"/Enemy1/Enemy1_ExecutionV"+(i+1)+"LowHealth.anim");pair.receiverLowHealthState="ExecutionV"+(i+1)+"LowHealth";
                    pair.receiverDelay=i==0?.36f:.56f;pair.strikeSeconds=i==0?26f/60f:37f/60f;
                    pair.receiverOffset=i==0?new Vector3(-.03f,0,1.19f):new Vector3(.06f,0,1.20f);
                    pair.receiverYaw=180;pair.alignSeconds=.18f;
                    pair.getupHealthy=Required(Anim+"/Enemy1/Enemy1_ExecutionGetupHealthy.anim");pair.getupLowHealth=Required(Anim+"/Enemy1/Enemy1_ExecutionGetupLowHealth.anim");
                    pair.bloodMaterial=material;AssetDatabase.CreateAsset(pair,path);
                }
                AddState(pc,pair.attackerState,pair.attackerClip);
                AddState(ec,pair.receiverState,pair.receiverClip);AddState(ec,pair.receiverLowHealthState,pair.receiverLowHealthClip);
                AddState(ec,pair.getupHealthyState,pair.getupHealthy);AddState(ec,pair.getupLowHealthState,pair.getupLowHealth);pairs[i]=pair;
            }
            // Version only initializes defaults once; future installs preserve explicit off/zero and user tuning.
            if(enemy.executionPairConfigurationVersion<1)
            {
                enemy.executionDamageMode=SamuraiExecutionDamageMode.FixedAmount;enemy.executionFixedDamage=45;
                enemy.executionSurvivingPair=pairs[0];enemy.executionLethalPair=pairs[1];enemy.executionPairConfigurationVersion=1;
                EditorUtility.SetDirty(enemy);AssetDatabase.SaveAssetIfDirty(enemy);
            }
            if(player.executionPairConfigurationVersion<1)
            {player.executionEnabled=true;player.executionPairConfigurationVersion=1;EditorUtility.SetDirty(player);AssetDatabase.SaveAssetIfDirty(player);}
            AssetDatabase.SaveAssetIfDirty(pc);AssetDatabase.SaveAssetIfDirty(ec);AssetDatabase.SaveAssetIfDirty(material);
            Debug.Log("Enemy1 paired executions installed; fixed 45 damage. Other receivers require their own pair assets.");
        }
        static AnimationClip Required(string path){var c=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);if(c==null)throw new InvalidOperationException("Missing "+path);return c;}
        static void AddState(AnimatorController controller,string name,AnimationClip clip)
        {
            var machine=controller.layers[0].stateMachine;var existing=machine.states.FirstOrDefault(s=>s.state.name==name).state;
            if(existing!=null)return;var state=machine.AddState(name);state.motion=clip;state.speed=1;state.iKOnFeet=false;state.writeDefaultValues=true;EditorUtility.SetDirty(controller);
        }
        static void EnsureFolder(string path)
        {if(AssetDatabase.IsValidFolder(path))return;int i=path.LastIndexOf('/');EnsureFolder(path.Substring(0,i));AssetDatabase.CreateFolder(path.Substring(0,i),path.Substring(i+1));}
    }
}
