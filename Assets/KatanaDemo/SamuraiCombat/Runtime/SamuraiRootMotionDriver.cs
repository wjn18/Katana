using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>挂在Animator同对象，只转交动画根增量；角色胶囊、动作资格与碰撞仍由战斗入口负责。</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Animator))]
    public sealed class SamuraiRootMotionDriver : MonoBehaviour
    {
        [SerializeField] SamuraiCombatant actor;
        Animator animator;

        public void Configure(SamuraiCombatant owner)
        {
            actor = owner;
            animator = GetComponent<Animator>();
            if (!animator.applyRootMotion) animator.applyRootMotion = true;
        }
        void Awake() { animator = GetComponent<Animator>(); }
        void OnAnimatorMove()
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (actor != null) actor.ApplyAnimationRootMotion(animator);
        }
    }
}
