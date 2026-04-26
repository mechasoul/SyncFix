using System;

namespace SyncFix.FrameRecorder
{
    /// <summary>
    /// dataholder for a single data value that's tracked per-frame
    /// </summary>
    /// <typeparam name="T">the type of the data value</typeparam>
    internal class FrameRecord<T> : IComparable<FrameRecord<T>>
    {
        public int frame;
        public T value;

        public FrameRecord(int frame, T value)
        {
            this.frame = frame;
            this.value = value;
        }

        public int CompareTo(FrameRecord<T> other)
        {
            return frame.CompareTo(other.frame);
        }

        public override string ToString()
        {
            return $"{frame},{value}";
        }
    }
}