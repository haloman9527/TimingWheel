#region 注 释

/***
 *
 *  Title:
 *
 *  Description:
 *
 *  Date:
 *  Version:
 *  Writer: 半只龙虾人
 *  Github: https://github.com/haloman9527
 *  Blog: https://www.haloman.net/
 *
 */

#endregion

using System;
using System.Collections.Generic;
using Atom;

namespace Atom
{
    public partial class TimingWheel : IDisposable
    {
        static TimingWheel()
        {
            ObjectPoolManager.RegisterPool(new LinkListNodePool());
        }
        
        /// <summary>
        /// 插槽
        /// </summary>
        private Slot[] slots;

        /// <summary>
        /// 插槽数量
        /// </summary>
        private int slotCount;

        /// <summary>
        /// 时间刻度
        /// </summary>
        private long tickSpan;

        /// <summary>
        /// 时间轮转动一圈的时间
        /// </summary>
        private long wheelSpan;

        /// <summary>
        /// 当前时间轮的开始时间
        /// </summary>
        private long startTime;

        /// <summary>
        /// 当前指针时间
        /// </summary>
        private long currentTime;

        /// <summary>
        /// 当前时间戳
        /// </summary>
        private long currentTimeStamp;

        /// <summary>
        /// 当前指针下标
        /// </summary>
        private int currentIndicator;

        /// <summary>
        /// 正在执行事件
        /// </summary>
        private bool executingTask;

        /// <summary>
        /// 整个分层事件轮共享数据
        /// </summary>
        private TimingWheelSharedInfo sharedInfo;

        /// <summary>
        /// 外轮(刻度更大的时间轮)
        /// </summary>
        private TimingWheel outerWheel;

        /// <summary>
        /// 内轮(刻度更小的时间轮)
        /// </summary>
        private TimingWheel innerWheel;

        /// <summary>
        /// 当前指针时间
        /// </summary>
        public long CurrentTime
        {
            get { return currentTime; }
        }

        /// <summary>
        /// 当前时间戳
        /// </summary>
        public long CurrentTimeStamp
        {
            get { return currentTimeStamp; }
        }

        /// <summary>
        /// 插槽数量
        /// </summary>
        public int SlotCount
        {
            get { return slotCount; }
        }

        /// <summary>
        /// 时间刻度
        /// </summary>
        public long TickSpan
        {
            get { return tickSpan; }
        }

        /// <summary>
        /// 一周时间
        /// </summary>
        public long WheelSpan
        {
            get { return wheelSpan; }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="slotCount"> 插槽数量 </param>
        /// <param name="tickSpan"> 时间刻度 </param>
        /// <param name="startTime"> 开始时间 </param>
        public TimingWheel(int slotCount, long tickSpan, long startTime)
        {
            this.tickSpan = tickSpan;
            this.slotCount = slotCount;
            this.wheelSpan = tickSpan * slotCount;
            this.startTime = startTime;
            this.currentTimeStamp = startTime;
            this.currentTime = startTime;
            this.slots = new Slot[slotCount];
            this.sharedInfo = new TimingWheelSharedInfo();
            for (int i = 0; i < slotCount; i++)
            {
                slots[i] = new Slot();
            }
        }

        private void Tick_Internal()
        {
            if (currentIndicator == 0)
            {
                outerWheel?.Tick_Internal();
            }

            currentTime += tickSpan;
            currentIndicator = (currentIndicator + 1) % slots.Length;

            if (currentIndicator == 0)
            {
                startTime += wheelSpan;
            }

            var currentSlot = slots[currentIndicator];
            var taskCount = currentSlot.tasks.Count;
            var taskNode = currentSlot.tasks.First;

            while (taskNode != null && taskCount-- > 0)
            {
                var task = taskNode.Value;
                var taskInfo = sharedInfo.taskInfos[task];

                if (currentTime >= taskInfo.nextTime)
                {
                    if (innerWheel != null)
                    {
                        RemoveTask_Internal(task);
                        innerWheel.AddTask_Internal(task, taskInfo.nextTime);
                    }
                    else
                    {
                        RemoveTask_Internal(task);

                        try
                        {
                            task.Invoke(this);
                        }
                        catch (Exception e)
                        {
                            throw e;
                        }

                        if (task.LoopTime < 0 || --task.LoopTime > 0)
                        {
                            taskInfo.nextTime += task.LoopInterval;
                            AddTask_Internal(task, taskInfo.nextTime);
                        }
                    }
                }

                taskNode = taskNode.Next;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="task"></param>
        /// <param name="nextTime"> must be bigger than current time </param>
        private void AddTask_Internal(ITimeTask task, long nextTime)
        {
            var delta = nextTime - currentTime;
            if (delta <= 0)
            {
                try
                {
                    task.Invoke(this);
                }
                catch (Exception e)
                {
                    throw e;
                }

                if (task.LoopTime < 0 || --task.LoopTime > 0)
                {
                    nextTime += task.LoopInterval;
                    AddTask_Internal(task, nextTime);
                }
            }
            else if (delta <= wheelSpan)
            {
                if (delta < tickSpan && innerWheel != null)
                {
                    innerWheel.AddTask_Internal(task, nextTime);
                }
                else
                {
                    var offset = nextTime - startTime;
                    var index = ((offset / tickSpan) + (offset % tickSpan > 0 ? 1 : 0)) % slotCount;
                    var slot = slots[index];
                    var taskNode = (LinkedListNode<ITimeTask>)ObjectPoolManager.Spawn(typeof(LinkedListNode<ITimeTask>));
                    taskNode.Value = task;
                    slot.tasks.AddLast(taskNode);

                    var taskInfo = new TimeTaskInfo();
                    taskInfo.nextTime = nextTime;
                    taskInfo.wheel = this;
                    taskInfo.slot = slot;
                    taskInfo.linkListNode = taskNode;
                    sharedInfo.taskInfos[task] = taskInfo;
                }
            }
            else if (delta > wheelSpan)
            {
                outerWheel?.AddTask_Internal(task, nextTime);
            }
        }

        private void RemoveTask_Internal(ITimeTask task)
        {
            var taskInfo = sharedInfo.taskInfos[task];
            sharedInfo.taskInfos.Remove(task);
            taskInfo.slot.tasks.Remove(taskInfo.linkListNode);
            ObjectPoolManager.Recycle(taskInfo.linkListNode);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="parentSlotCounts"> 父轮插槽数量 </param>
        public void BuildParent(params int[] parentSlotCounts)
        {
            if (parentSlotCounts == null)
                return;

            var wheel = this;
            for (int i = 0; i < parentSlotCounts.Length; i++)
            {
                if (wheel.outerWheel != null)
                {
                    wheel = outerWheel;
                }
                else
                {
                    wheel.outerWheel = new TimingWheel(parentSlotCounts[i], wheelSpan, startTime);
                    wheel.outerWheel.innerWheel = this;
                    wheel.outerWheel.sharedInfo = this.sharedInfo;
                    wheel = wheel.outerWheel;
                }
            }
        }

        /// <summary>
        /// 推进时间轮，前进1刻度(<see cref="tickSpan"/>)
        /// </summary>
        public void Tick()
        {
            currentTimeStamp += tickSpan;
            var delta = currentTimeStamp - currentTime;
            while (delta > tickSpan)
            {
                Tick_Internal();
                delta -= tickSpan;
            }
        }

        /// <summary> 推进时间轮 </summary>
        /// <param name="tick"> 拨动指针，前进一段时间 </param>
        public void Tick(long tick)
        {
            currentTimeStamp += tick;
            var delta = currentTimeStamp - currentTime;
            while (delta > tickSpan)
            {
                Tick_Internal();
                delta -= tickSpan;
            }
        }

        public bool ContainsTask(ITimeTask task)
        {
            return sharedInfo.taskInfos.ContainsKey(task);
        }

        /// <summary>
        /// 添加时间任务
        /// </summary>
        /// <param name="task"></param>
        /// <param name="startDelay"></param>
        public void AddTask(ITimeTask task, long startDelay = 0)
        {
            if (sharedInfo.taskInfos.ContainsKey(task))
            {
                return;
            }

            startDelay = Math.Max(startDelay, 0);
            var nextInvokeTime = currentTime + startDelay;

            AddTask_Internal(task, nextInvokeTime);
        }

        public void RemoveTask(ITimeTask task)
        {
            if (!sharedInfo.taskInfos.TryGetValue(task, out var taskInfo))
            {
                return;
            }

            RemoveTask_Internal(task);
        }

        public void ClearTasks()
        {
            foreach (var pair in sharedInfo.taskInfos)
            {
                var taskInfo = pair.Value;
                taskInfo.slot.tasks.Remove(taskInfo.linkListNode);
                ObjectPoolManager.Recycle(taskInfo.linkListNode);
            }

            sharedInfo.taskInfos.Clear();
        }

        public void Dispose()
        {
            ClearTasks();
        }
    }


    public partial class TimingWheel
    {
        public interface ITimeTask
        {
            /// <summary>
            /// 循环次数
            ///     -1: 无限
            ///    0|1: 不循环
            /// </summary>
            int LoopTime { get; set; }

            /// <summary>
            /// 循环间隔
            /// </summary>
            long LoopInterval { get; set; }

            void Invoke(TimingWheel timingWheel);
        }

        public class CustomTask : ITimeTask
        {
            public int LoopTime { get; set; }

            public long LoopInterval { get; set; }

            public Action task;

            public void Invoke(TimingWheel timingWheel)
            {
                task?.Invoke();
            }
        }

        public class DayTask : ITimeTask
        {
            public int LoopTime { get; set; }

            public long LoopInterval
            {
                get => 86400000L;
                set { }
            }

            public Action task;

            public void Invoke(TimingWheel timingWheel)
            {
                task?.Invoke();
            }
        }

        public class WeekTask : ITimeTask
        {
            public int LoopTime { get; set; }

            public long LoopInterval
            {
                get => 604800000L;
                set { }
            }

            public Action task;

            public void Invoke(TimingWheel timingWheel)
            {
                task?.Invoke();
            }
        }

        public class MonthTask : ITimeTask
        {
            public int LoopTime { get; set; }

            public long LoopInterval
            {
                get => new DateTimeOffset(0, 1, 0, 0, 0, 0, TimeSpan.Zero).Millisecond;
                set { }
            }

            public Action task;

            public void Invoke(TimingWheel timingWheel)
            {
                task?.Invoke();
            }
        }

        public class YearTask : ITimeTask
        {
            public int LoopTime { get; set; }

            public long LoopInterval
            {
                get => new DateTimeOffset(1, 0, 0, 0, 0, 0, TimeSpan.Zero).Millisecond;
                set { }
            }

            public Action task;

            public void Invoke(TimingWheel timingWheel)
            {
                task?.Invoke();
            }
        }

        public struct TimeTaskInfo
        {
            /// <summary>
            /// 任务下次执行时间
            /// </summary>
            public long nextTime;

            /// <summary>
            /// 任务所在时间层
            /// </summary>
            public TimingWheel wheel;

            /// <summary>
            /// 任务所在刻度
            /// </summary>
            public Slot slot;

            /// <summary>
            /// 任务所在的链表的位置
            /// </summary>
            public LinkedListNode<ITimeTask> linkListNode;
        }

        /// <summary>
        /// 整个分层事件轮中共享的数据
        /// </summary>
        internal class TimingWheelSharedInfo
        {
            /// <summary>
            /// 任务数据
            /// </summary>
            public Dictionary<ITimeTask, TimeTaskInfo> taskInfos = new Dictionary<ITimeTask, TimeTaskInfo>();
        }

        public class Slot
        {
            public LinkedList<ITimeTask> tasks = new LinkedList<ITimeTask>();
        }

        public class LinkListNodePool : ObjectPoolBase<LinkedListNode<ITimeTask>>
        {
            protected override LinkedListNode<ITimeTask> Create()
            {
                return new LinkedListNode<ITimeTask>(default);
            }
        }
    }
}