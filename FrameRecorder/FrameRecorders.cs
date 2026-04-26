using System.Collections.Generic;

namespace SyncFix.FrameRecorder
{
    /// <summary>
    /// static class for recording frame values. use this to record frame values without having to manage framerecorders manually
    /// </summary>
    internal class FrameRecorders
    {
        private static readonly Dictionary<string, IFrameRecorder> _frameRecorders;

        static FrameRecorders()
        {
            _frameRecorders = new Dictionary<string, IFrameRecorder>();
        }

        /// <summary>
        /// retrieves the FrameRecorder with the given name. note names are unique among FrameRecorders. if a FrameRecorder exists with the same
        /// name but a different type, bad things will happen. instantiates the frame recorder automatically if none exists with that name
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="name"></param>
        /// <returns></returns>
        public static IFrameRecorder GetFrameRecorder<T>(string name)
        {
            if (_frameRecorders.TryGetValue(name, out IFrameRecorder recorder))
            {
                return recorder;
            }
            else
            {
                IFrameRecorder frameRecorder = new FrameRecorder<T>(name);
                _frameRecorders.Add(name, frameRecorder);
                return frameRecorder;
            }
        }

        /// <summary>
        /// record the given data value for the given frame in the given FrameRecorder. will automatically create the FrameRecorder if it doesn't
        /// already exist. can do all recording through this without ever needing to call GetFrameRecorder manually
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="name"></param>
        /// <param name="frame"></param>
        /// <param name="value"></param>
        public static void Record<T>(string name, int frame, T value)
        {
            IFrameRecorder frameRecorder = GetFrameRecorder<T>(name);
            frameRecorder.Record(frame, value);
        }

        /// <summary>
        /// saves all existing FrameRecorders to disk
        /// </summary>
        public static void SaveAll()
        {
            foreach (var frameRecorder in _frameRecorders.Values)
            {
                frameRecorder.SaveToFile();
                frameRecorder.Clear();
            }
        }

        public static void ClearAll()
        {
            foreach (var frameRecorder in _frameRecorders.Values)
            {
                frameRecorder.Clear();
            }
        }
    }
}
