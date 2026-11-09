using System;
using OCDesign.Combat;
using UnityEditor;
using UnityEngine;

namespace OCDesign.EditorTools
{
    public static class HeavyLandingVfxSetup
    {
        [MenuItem("Katana/Naginata/Install Heavy Landing Impact")]
        public static void Install()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit模式装配落地特效。");
            var shader=Shader.Find("KatanaDemo/VFX/GroundImpact");
            if(!shader||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("落地Shader尚未编译成功。");
            string oldPath=NaginataCombatSetup.Root+"/VFX/HeavyLandingRing.mat";
            string fracturePath=NaginataCombatSetup.Root+"/VFX/HeavyLandingFracture.mat";
            bool migrate=AssetDatabase.LoadAssetAtPath<Material>(oldPath)&&!AssetDatabase.LoadAssetAtPath<Material>(fracturePath);
            if(migrate)
            {
                string error=AssetDatabase.MoveAsset(oldPath,fracturePath);
                if(!string.IsNullOrEmpty(error))throw new InvalidOperationException(error);
            }
            var fracture=Material("HeavyLandingFracture",shader,2f,new Color(.02f,.018f,.014f,1f));
            if(migrate)
            {
                fracture.name="HeavyLandingFracture";fracture.SetFloat("_Mode",2f);fracture.SetColor("_Tint",new Color(.02f,.018f,.014f,1f));
                EditorUtility.SetDirty(fracture);AssetDatabase.SaveAssetIfDirty(fracture);
            }
            var dust=Material("HeavyLandingDust",shader,1f,new Color(.64f,.55f,.43f,.72f));
            var go=PrefabUtility.LoadPrefabContents(NaginataCombatSetup.PrefabPath);
            try
            {
                var definition=AssetDatabase.LoadAssetAtPath<CombatAttackDefinition>(NaginataCombatSetup.Root+"/Config/NaginataHeavyAttack03Fix.asset");
                var driver=go.GetComponent<CombatAnimationDriver>();var motor=go.GetComponent<CombatCharacterMotor>();var capsule=go.GetComponent<CharacterController>();
                if(!definition||!driver||!motor||!capsule)throw new InvalidOperationException("缺少当前重击或运动组件。");
                var effect=go.GetComponent<CombatLandingImpact>();if(!effect)effect=go.AddComponent<CombatLandingImpact>();
                effect.Configure(driver,motor,capsule,definition,fracture,dust);
                PrefabUtility.SaveAsPrefabAsset(go,NaginataCombatSetup.PrefabPath);
            }
            finally{PrefabUtility.UnloadPrefabContents(go);}
        }
        private static Material Material(string name,Shader shader,float mode,Color tint)
        {
            string path=NaginataCombatSetup.Root+"/VFX/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(shader){name=name};material.SetFloat("_Mode",mode);material.SetColor("_Tint",tint);AssetDatabase.CreateAsset(material,path);}
            AssetDatabase.SaveAssetIfDirty(material);return material;
        }
    }
}
