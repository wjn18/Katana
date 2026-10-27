using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OCDesign.Samurai;
using UnityEditor;
using UnityEngine;

namespace OCDesign.EditorTools
{
    /// <summary>攻击动作的项目初始击退参数；运行时始终读取动作配置，0仍可用于明确关闭。</summary>
    public static class SamuraiAttackKnockbackSetup
    {
        const string Config="Assets/KatanaDemo/SamuraiCombat/Config/";
        public static string ReportFolder=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../斗笠剑士/动画资源/samurai-combat/analysis/attack-knockback-20261027"));
        public static Vector2 Defaults(SamuraiActionDefinition action)
        {
            switch(action.name){
                case "Nioh3_High_Attack1_03100":case "Nioh3_Mid_Attack1_09200":return new Vector2(.15f,.12f);
                case "Nioh3_High_Attack2_03110":return new Vector2(.2f,.14f);
                case "Nioh3_High_Attack3_03120":return new Vector2(.25f,.16f);
                case "Nioh3_High_Attack4_03210":case "Nioh3_High_Strong_03300":return new Vector2(.6f,.22f);
                case "Nioh3_Mid_SpinAttack_09201":return new Vector2(.18f,.14f);
                case "Nioh3_Mid_Attack3_09202":return new Vector2(.2f,.16f);
                case "Nioh3_Mid_Attack2_09203":return new Vector2(.22f,.16f);
                case "Nioh3_Mid_SpinAttack2_09204":return new Vector2(.25f,.18f);
                case "Nioh3_Mid_SpinAttack3_09205":return new Vector2(.4f,.22f);
                case "Nioh3_Mid_Slash_09600":return new Vector2(.3f,.18f);
                case "Nioh3_Mid_StrongAttack2_09601":return new Vector2(.35f,.18f);
                case "Nioh3_Mid_HorizontalSlash_09602":return new Vector2(.4f,.2f);
                case "Nioh3_Mid_StrongAttack4_09603":return new Vector2(.6f,.22f);
                case "Nioh3_High_Kick_07600":return new Vector2(.7f,.22f);
                case "Nioh3_High_RisingDragon_16000":return new Vector2(.6f,.24f);
                case "Nioh3_High_Combo_09300":case "Nioh3_Mid_SpinningIai_05160":return new Vector2(.25f,.18f);
                case "Nioh3_High_WhirlwindSlash_05080":return new Vector2(.35f,.18f);
                case "Nioh3_Mid_Sweep_09210":case "Nioh3_FrostMoon_High_16000":return new Vector2(.45f,.2f);
                case "Nioh3_FrostMoon_Mid_16100":return new Vector2(.35f,.18f);
                default:return new Vector2(action.isMartialArt?.35f:.2f,.18f);
            }
        }
        // 只在动作首次创建时调用。正常重新安装不会覆盖用户后来设置的0或其他数值。
        public static void InitializeNew(SamuraiActionDefinition action){var value=Defaults(action);action.knockbackDistance=value.x;action.knockbackSeconds=value.y;}
        public static SamuraiActionDefinition[] PlayerActions()
        {
            var profile=AssetDatabase.LoadAssetAtPath<SamuraiCombatProfile>(Config+"SamuraiPlayer.asset");var result=new HashSet<SamuraiActionDefinition>();
            var assets=new List<UnityEngine.Object>{profile};assets.AddRange(profile.stances.Where(s=>s!=null));
            foreach(var asset in assets){var p=new SerializedObject(asset).GetIterator();while(p.Next(true))if(p.propertyType==SerializedPropertyType.ObjectReference&&p.objectReferenceValue is SamuraiActionDefinition action)result.Add(action);}
            return result.OrderBy(a=>a.name,StringComparer.Ordinal).ToArray();
        }
        [MenuItem("Tools/OC Design/Samurai Combat/Fill Missing Player Action Knockback")]
        public static void FillMissingDefaults()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Exit Play before changing action assets.");
            var changed=new List<object>();
            foreach(var action in PlayerActions()){
                string path=AssetDatabase.GetAssetPath(action);if(!path.StartsWith(Config+"Nioh3/",StringComparison.Ordinal)||action.knockbackDistance!=0f)continue;
                InitializeNew(action);EditorUtility.SetDirty(action);AssetDatabase.SaveAssetIfDirty(action);
                changed.Add(new{path,action.knockbackDistance,action.knockbackSeconds});
            }
            Directory.CreateDirectory(ReportFolder);string report=Path.Combine(ReportFolder,"补齐动作配置.json");if(changed.Count>0||!File.Exists(report))File.WriteAllText(report,Newtonsoft.Json.JsonConvert.SerializeObject(new{date="2026-10-27",changedCount=changed.Count,projectTuningDefaults=true,actions=changed},Newtonsoft.Json.Formatting.Indented));
            Debug.Log("Filled knockback settings on "+changed.Count+" player action assets. Runtime knockback logic unchanged.");
        }
    }
}
