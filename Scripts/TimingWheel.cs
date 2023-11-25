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
 *  Github: https://github.com/HalfLobsterMan
 *  Blog: https://www.crosshair.top/
 *
 */

#endregion

using System;
using System.Collections.Generic;

namespace CZToolKit.TimingWheel
{
    public class TimingWheel
    {
        public interface ITimeTask
        {
            /// <summary>
            /// 下次执行的时间
            /// </summary>
            long NextTime { get; set; }

            /// <summary>
            /// 循环次数
            ///     -1: 无限
            ///    0|1: 不循环
            /// </summary>
            int LoopTime { get; set; }

            /// <summary>
            /// 循环间隔
            /// </summary>
            long LoopInterval { get; }

            Action Task { get; }
        }

        public class Slot
        {
            public LinkedList<ITimeTask> tasks = new LinkedList<ITimeTask>();
        }

        public class LinkListNodePool : BaseObjectPool<LinkedListNode<ITimeTask>>
        {
            protected override LinkedListNode<ITimeTask> Create()
            {
                return new LinkedListNode<ITimeTask>(default);
            }
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
        /// 父轮(刻度更大的时间轮)
        /// </summary>
        private TimingWheel parentWheel;

        /// <summary>
        /// 子轮(刻度更小的时间轮)
        /// </summary>
        private TimingWheel childWheel;

        /// <summary>
        /// 链表节点对象池
        /// </summary>
        private LinkListNodePool taskLinkListNodePool;

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
            for (int i = 0; i < slotCount; i++)
            {
                slots[i] = new Slot();
            }

            taskLinkListNodePool = new LinkListNodePool();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="parentSlotCount"> 父轮插槽数量 </param>
        /// <returns></returns>
        public TimingWheel BuildParent(int parentSlotCount)
        {
            if (parentWheel != null)
                return parentWheel;

            parentWheel = new TimingWheel(parentSlotCount, wheelSpan, startTime);
            parentWheel.childWheel = this;
            return parentWheel;
        }

        /// <summary>
        /// 推进时间轮，前进1刻度(<see cref="tickSpan"/>)
        /// </summary>
        public void Tick()
        {
            Step(currentTimeStamp + tickSpan);
        }

        /// <summary> 推进时间轮 </summary>
        /// <param name="tick"> 拨动指针，前进一段时间 </param>
        public void Tick(long tick)
        {
            Step(currentTimeStamp + tick);
        }

        /// <summary>
        /// 推进时间轮
        /// </summary>
        /// <param name="timestamp"> 前进到该时间戳 </param>
        public void Step(long timestamp)
        {
            if (timestamp <= currentTimeStamp)
                return;
            currentTimeStamp = timestamp;
            while (currentTimeStamp - currentTime >= tickSpan)
            {
                currentTime += tickSpan;
                currentIndicator = (currentIndicator + 1) % slots.Length;
                if (currentIndicator == 0)
                {
                    parentWheel.Step(startTime + wheelSpan);
                    startTime += wheelSpan;
                }

                var currentSlot = slots[currentIndicator];
                var taskNode = currentSlot.tasks.First;

                executingTask = true;
                while (taskNode != null)
                {
                    var task = taskNode.Value;

                    if (childWheel != null)
                    {
                        childWheel.AddTask(task);
                    }
                    else
                    {
                        if (currentTime - task.NextTime < tickSpan)
                        {
                            task.Task?.Invoke();
                        }

                        if (task.LoopTime < 0 || --task.LoopTime > 0)
                        {
                            task.NextTime += task.LoopInterval;
                            AddTask(task);
                        }
                    }

                    var tempTaskNode = taskNode;
                    taskNode = taskNode.Next;
                    currentSlot.tasks.Remove(tempTaskNode);
                    taskLinkListNodePool.Release(tempTaskNode);
                }

                executingTask = false;
            }
        }

        /// <summary>
        /// 添加时间任务
        /// </summary>
        /// <param name="task"></param>
        public void AddTask(ITimeTask task)
        {
            if (task.NextTime == currentTime)
            {
                var slot = slots[currentIndicator];
                if (executingTask)
                {
                    var taskNode = taskLinkListNodePool.Acquire();
                    taskNode.Value = task;
                    slot.tasks.AddLast(taskNode);
                }
                else
                {
                    task.Task?.Invoke();
                    if (task.LoopTime < 0 || --task.LoopTime > 0)
                    {
                        task.NextTime += task.LoopInterval;
                        AddTask(task);
                    }
                }
            }
            else if (currentTime + wheelSpan > task.NextTime)
            {
                var step = task.NextTime - currentTime;
                var index = ((step / tickSpan) + (step % tickSpan == 0 ? 0 : 1) + currentIndicator) % slotCount;
                var slot = slots[index];
                var taskNode = taskLinkListNodePool.Acquire();
                taskNode.Value = task;
                slot.tasks.AddLast(taskNode);
            }
            else if (parentWheel != null)
                parentWheel.AddTask(task);
        }
    }
}