using UnityEngine;

namespace OCDesign.Samurai
{
    /// <summary>
    /// 只负责角色胶囊的移动、重力和转向。不读取输入设备，也不决定攻击或防御状态。
    /// 生命周期由角色入口管理；所有动作位移仍经过同一个 CharacterController。
    /// </summary>
    public sealed class SamuraiCharacterMotor
    {
        readonly Transform root;
        readonly CharacterController controller;
        readonly SamuraiCombatProfile profile;
        Vector3 spawnPosition;
        Quaternion spawnRotation;
        float verticalSpeed;
        readonly RaycastHit[] corpseHits = new RaycastHit[32];

        public CharacterController Controller => controller;
        public Vector3 Forward => root != null ? root.forward : Vector3.forward;

        public SamuraiCharacterMotor(Transform root, CharacterController controller, SamuraiCombatProfile profile)
        {
            this.root = root;
            this.controller = controller;
            this.profile = profile;
            CaptureSpawn();
        }

        public void CaptureSpawn()
        {
            if (root == null) return;
            spawnPosition = root.position;
            spawnRotation = root.rotation;
        }

        public void ResetPose()
        {
            verticalSpeed = 0f;
            if (root == null) return;
            bool wasEnabled = controller != null && controller.enabled;
            if (wasEnabled) controller.enabled = false;
            root.SetPositionAndRotation(spawnPosition, spawnRotation);
            if (wasEnabled) controller.enabled = true;
        }

        public void FaceDirection(Vector3 direction, float deltaTime)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f || profile == null || root == null) return;
            root.rotation = Quaternion.RotateTowards(root.rotation,
                Quaternion.LookRotation(direction), Mathf.Max(0f, profile.turnSpeed * deltaTime));
        }

        /// <summary>动作开始时锁定出招方向；动作途中不会自动追踪目标。</summary>
        public void TurnForAction(Vector3 direction)
        {
            direction.y = 0f;
            if (root != null && direction.sqrMagnitude > 0.001f)
                root.rotation = Quaternion.LookRotation(direction);
        }

        public void Move(Vector3 planarVelocity, float dt)
        {
            if (controller == null || !controller.enabled || profile == null || dt <= 0f) return;
            if (controller.isGrounded && verticalSpeed < 0f) verticalSpeed = -2f;
            verticalSpeed = Mathf.Max(-Mathf.Max(1f, profile.terminalFallSpeed),
                verticalSpeed - profile.gravity * dt);
            CollisionFlags flags = controller.Move((planarVelocity + Vector3.up * verticalSpeed) * dt);
            if ((flags & CollisionFlags.Above) != 0 && verticalSpeed > 0f) verticalSpeed = 0f;
            if ((flags & CollisionFlags.Below) != 0 && verticalSpeed < 0f) verticalSpeed = -2f;
        }

        /// <summary>敌人死亡后保持胶囊关闭，仅沿竖直方向落到环境；不重新阻挡玩家。</summary>
        public void MoveCorpse(float dt)
        {
            if (controller == null || controller.enabled || profile == null || !profile.disableCollisionOnDeath)
            { Move(Vector3.zero, dt); return; }
            if (root == null || dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            dt = Mathf.Min(dt, .1f);
            verticalSpeed = Mathf.Max(-Mathf.Max(1f, profile.terminalFallSpeed),
                Mathf.Min(0f, verticalSpeed) - Mathf.Max(0f, profile.gravity) * dt);
            float fall = -verticalSpeed * dt;
            Vector3 origin = root.position + Vector3.up * .2f;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, corpseHits, .22f + fall,
                profile.obstructionMask, QueryTriggerInteraction.Ignore);
            RaycastHit[] hits = corpseHits;
            if (count == corpseHits.Length)
            { hits = Physics.RaycastAll(origin, Vector3.down, .22f + fall, profile.obstructionMask, QueryTriggerInteraction.Ignore); count = hits.Length; }
            float ground = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider.GetComponentInParent<SamuraiCombatant>() != null || hits[i].normal.y < .45f) continue;
                ground = Mathf.Max(ground, hits[i].point.y);
            }
            float y = root.position.y - fall;
            if (!float.IsNegativeInfinity(ground) && y <= ground + .001f)
            { y = ground + .001f; verticalSpeed = 0f; }
            var position = root.position; position.y = y; root.position = position;
        }

        /// <summary>按动作进度差计算本次步进，避免帧率改变攻击或闪避的总位移。</summary>
        public void MoveAction(SamuraiActionDefinition action, Vector3 actionDirection,
            float previousNormalized, float currentNormalized, float dt)
        {
            if (dt <= 0f) return;
            float distance = action != null ? action.stepDistance
                * (action.StepFraction(currentNormalized) - action.StepFraction(previousNormalized)) : 0f;
            Move(actionDirection * (Mathf.Max(0f, distance) / dt), dt);
        }

        /// <summary>受击等外部水平位移；不额外推进重力，也不绕过CharacterController碰撞。</summary>
        public void DisplacePlanar(Vector3 displacement)
        {
            if (controller == null || !controller.enabled || !controller.gameObject.activeInHierarchy) return;
            displacement.y = 0f;
            if (displacement.sqrMagnitude > 0f) controller.Move(displacement);
        }

        public static Vector3 MoveDirection(SamuraiInputFrame input, Transform root)
        {
            Vector3 direction = input.moveWorld;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f && root != null)
                direction = root.right * input.move.x + root.forward * input.move.y;
            return Vector3.ClampMagnitude(direction, 1f);
        }

        public static Vector3 FacingDirection(Vector3 preferred, Vector3 fallback)
        {
            preferred.y = 0f;
            return preferred.sqrMagnitude > .0001f ? preferred.normalized : fallback;
        }
    }
}
