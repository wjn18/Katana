using System;

namespace OCDesign.Samurai.BehaviorTree
{
    /// <summary>无 Unity 或战斗依赖的反应式节点；每次 Tick 从第一个子节点重评估优先级。</summary>
    public enum NodeStatus { Failure, Success, Running }

    public abstract class Node
    {
        public abstract NodeStatus Tick();
    }

    public sealed class ConditionNode : Node
    {
        private readonly Func<bool> condition;
        public ConditionNode(Func<bool> condition)
        {
            this.condition = condition ?? throw new ArgumentNullException(nameof(condition));
        }
        public override NodeStatus Tick() => condition() ? NodeStatus.Success : NodeStatus.Failure;
    }

    public sealed class ActionNode : Node
    {
        private readonly Func<NodeStatus> action;
        public ActionNode(Func<NodeStatus> action)
        {
            this.action = action ?? throw new ArgumentNullException(nameof(action));
        }
        public override NodeStatus Tick() => action();
    }

    /// <summary>按顺序执行；第一个失败或尚未结束的子节点中止本次评估。</summary>
    public sealed class Sequence : Node
    {
        private readonly Node[] children;
        public Sequence(params Node[] children)
        {
            this.children = children ?? throw new ArgumentNullException(nameof(children));
        }
        public override NodeStatus Tick()
        {
            foreach (var child in children)
            {
                var result = child.Tick();
                if (result != NodeStatus.Success) return result;
            }
            return NodeStatus.Success;
        }
    }

    /// <summary>按优先级选中第一个成功或运行中的子节点；下帧仍允许高优先级分支抢占。</summary>
    public sealed class Selector : Node
    {
        private readonly Node[] children;
        public Selector(params Node[] children)
        {
            this.children = children ?? throw new ArgumentNullException(nameof(children));
        }
        public override NodeStatus Tick()
        {
            foreach (var child in children)
            {
                var result = child.Tick();
                if (result != NodeStatus.Failure) return result;
            }
            return NodeStatus.Failure;
        }
    }
}
