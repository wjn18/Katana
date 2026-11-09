using System;
using System.Collections.Generic;
using UnityEditor;
namespace OCDesign.Samurai.EditorTools
{
    /// <summary>Elite1维护入口：真实Humanoid Avatar及双手锤肌肉动画。</summary>
    public static class Elite1CombatSetup
    {
        public const string PrefabPath="Assets/KatanaDemo/Characters/Elite1/Prefabs/Elite1.prefab";
        public const string ClipFolder="Assets/KatanaDemo/Characters/Elite1/Animations/Humanoid/Combat";
        public const string ControllerPath=ClipFolder+"/Elite1Combat.controller";
        public const string ConfigFolder="Assets/KatanaDemo/SamuraiCombat/Config/Elite1";
        public const string ProfilePath=ConfigFolder+"/Elite1.asset";
        // 旧报告读取结构；Generic烘焙已退出运行接入。
        [Serializable] public sealed class ClipReport
        {
            public string state,path,source;
            public float duration,minBodyY,maxBodyY,maximumGroundLift,interpolationGroundLift,strideSpeed,alignmentYaw;
            public int frames,curves; public bool loop,adaptedGuard; public float[] phaseTimes;
        }
        [Serializable] public sealed class InstallReport
        {
            public string date="2026-10-27";public float tPoseError,visualOffset;
            public bool validTPose,avatarValid;public int mappedBones,bakedTransforms,sceneEnemies;
            public List<ClipReport> clips=new List<ClipReport>();
        }
        [MenuItem("Katana/Samurai/Enemies/Install Elite1 Combat In Current Scene")]
        public static void InstallInCurrentScene()
        {
            Elite1HumanoidSetup.Install();
            Elite1AttackVolumeSetup.InstallAll();
            EnemyAttackRootMotionSetup.InstallElite1();
        }
        public static void RebakeClip(string state)=>Elite1HumanoidSetup.Install();
    }
}
