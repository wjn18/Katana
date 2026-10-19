using UnityEngine;

namespace OCDesign.Combat
{
    /// <summary>
    /// 角色组合入口，仅协调输入、动作和运动。替换 CombatInputSource 可接入 AI、回放或其它设备。
    /// 相机是场景依赖，通过 BindCamera 注入，角色 Prefab 不持有场景对象。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatActorController : MonoBehaviour
    {
        [SerializeField] private CombatInputSource inputSource;
        [SerializeField] private CombatCharacterMotor motor;
        [SerializeField] private CombatAnimationDriver animationDriver;
        [SerializeField] private CombatOrbitCamera orbitCamera;
        private bool initialized;

        public CombatCharacterMotor Motor => motor;
        public CombatAnimationDriver AnimationDriver => animationDriver;

        public void Configure(CombatInputSource input, CombatCharacterMotor characterMotor, CombatAnimationDriver driver)
        { inputSource = input; motor = characterMotor; animationDriver = driver; }

        public void BindCamera(CombatOrbitCamera cameraRig)
        {
            orbitCamera = cameraRig;
            if (orbitCamera) orbitCamera.Configure(transform);
        }

        private void Start()
        {
            if (!inputSource || !motor || !motor.Settings || !animationDriver)
            { Debug.LogError("战斗角色缺少输入、运动、移动配置或动画组件。", this); enabled = false; return; }
            if (!animationDriver.ValidateConfiguration(out string error))
            { Debug.LogError(error, this); enabled = false; return; }
            if (!orbitCamera && Camera.main) orbitCamera = Camera.main.GetComponent<CombatOrbitCamera>();
            if (orbitCamera) orbitCamera.Configure(transform);
            animationDriver.Target.applyRootMotion = false;
            animationDriver.ResetState();
            initialized = true;
        }

        private void Update()
        {
            if (!initialized) return;
            CombatInputFrame input = inputSource.isActiveAndEnabled ? inputSource.ReadFrame() : CombatInputFrame.None;
            if (orbitCamera) orbitCamera.ApplyLook(input.Look);
            animationDriver.TickAttack();
            if (input.PrimaryAttackPressed) animationDriver.TryPrimaryAttack();
            else if (input.HeavyAttackPressed) animationDriver.TryHeavyAttack();
            else if (input.AttackIndex >= 0) animationDriver.TryStartAttack(input.AttackIndex);
            CombatAttackDefinition attack = animationDriver.CurrentAttack;
            motor.SetAttackMotion(attack, animationDriver.CurrentAttackNormalizedTime, animationDriver.AttackSerial);
            Vector3 viewForward = orbitCamera ? orbitCamera.transform.forward : Vector3.forward;
            motor.Tick(input.Move, input.Sprint, viewForward,
                attack && attack.LockMovement, attack && attack.LockRotation, Time.deltaTime);
            animationDriver.SetLocomotion(motor.PlanarSpeed, motor.Settings, Time.deltaTime);
        }

        private void OnDisable()
        {
            if (motor) motor.ResetMotion();
            if (initialized && animationDriver) animationDriver.ResetState();
            if (inputSource) inputSource.SuspendInput();
        }

        private void OnEnable()
        {
            if (initialized && inputSource) inputSource.ResumeInput();
        }
    }
}
