using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using SyncFix.Utils;
using LLBML.Utils;
using Multiplayer;

namespace SyncFix.FrameRecorder
{
    /// <summary>
    /// records data values per-frame. stores them in a list and saves as a csv, by converting frame records to text
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class FrameRecorder<T> : IFrameRecorder
    {
        /// <summary>
        /// name of the recorder. used in the saved file's name
        /// </summary>
        public readonly string name;
        public List<FrameRecord<T>> records;
        /// <summary>
        /// defines how frame records are converted to text to be saved
        /// </summary>
        private Func<FrameRecord<T>, string> toStringFunction = record => record.ToString();

        public Func<FrameRecord<T>, string> ToStringFunc
        {
            get { return toStringFunction; }
            set { toStringFunction = value; }
        }


        internal FrameRecorder(string name)
        {
            this.name = name;
            this.records = new List<FrameRecord<T>>();
        }
        
        //i don't remember why i did the generics this way. probably something about trying to have a collection of IFrameRecorder
        /// <summary>
        /// records the given data value for the given frame. throws exception if the given data type isn't castable to the type of the FrameRecorder
        /// </summary>
        /// <typeparam name="U"></typeparam>
        /// <param name="frame"></param>
        /// <param name="value"></param>
        /// <exception cref="InvalidCastException"></exception>
        public void Record<U>(int frame, U value)
        {
            if (value is not T castValue) throw new InvalidCastException($"tried to record a {typeof(U)} in a FrameRecorder<{typeof(T)}>");

            records.Add(new FrameRecord<T>(frame, castValue));
        }

        public void SaveToFile()
        {
            if (records.Count == 0) return;
            records.Sort();
            StringBuilder sb = new StringBuilder();
            foreach (FrameRecord<T> record in records)
            {
                sb.Append(ToStringFunc(record));
                sb.AppendLine();
            }
            string path = Utility.CombinePaths(PathUtils.GetCurrentGameDebugPath(), $"{name}.csv");
            Directory.CreateDirectory(Directory.GetParent(path).FullName);
            File.AppendAllText(path, sb.ToString());
        }

        public void Clear()
        {
            records.Clear();
        }
    }
}
