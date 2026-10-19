using UnityEngine;

namespace OCDesign.Combat
{
    /// <summary>控制器依赖的输入边界，不要求调用方了解键盘、鼠标或 Input System。</summary>
    public abstract class CombatInputSource : MonoBehaviour
    {
        /// <summary>由控制器在 Update 中读取当前帧；一次性操作不会排队留到后续帧。</summary>
        public abstract CombatInputFrame ReadFrame();

        /// <summary>复位或中断控制时清理暂存输入；具体输入源可自行补充处理。</summary>
        public virtual void ResetInput() { }

        /// <summary>控制器停止消费输入时调用，输入源应释放设备控制权并清理操作。</summary>
        public virtual void SuspendInput() => ResetInput();

        /// <summary>控制器重新开始工作时调用；不会恢复挂起前的一次性操作。</summary>
        public virtual void ResumeInput() => ResetInput();
    }
}
