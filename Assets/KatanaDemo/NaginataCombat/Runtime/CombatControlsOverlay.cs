using UnityEngine;

namespace OCDesign.Combat
{
    /// <summary>演示场景的操作提示。删除此组件不会影响任何操控逻辑。</summary>
    public sealed class CombatControlsOverlay : MonoBehaviour
    {
        [SerializeField] private CombatAnimationDriver animationDriver;
        public void Configure(CombatAnimationDriver driver) => animationDriver = driver;

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(18f, 18f, 430f, 170f), GUI.skin.box);
            GUILayout.Label("NAGINATA / THIRD PERSON COMBAT");
            GUILayout.Label("WASD / Arrows: move    Shift: run");
            GUILayout.Label("Mouse: orbit    RMB: aerial heavy attack 03");
            GUILayout.Label("LMB: click to chain 01 > 02 > 04 > 05 > 06");
            GUILayout.Label("1 - 6: single attacks (3: heavy)    Stop clicking: recover");
            GUILayout.Label("Esc: release cursor    Click: resume controls");
            GUILayout.Label(animationDriver && animationDriver.IsAttacking
                ? "Action: " + animationDriver.CurrentAttack.DisplayName : "Action: locomotion");
            if (animationDriver && animationDriver.HasBufferedCombo) GUILayout.Label("Next combo hit queued");
            GUILayout.EndArea();
        }
    }
}
