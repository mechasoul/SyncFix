namespace SyncFix.FrameRecorder
{
    /// <summary>
    /// an object that records data values per-frame
    /// </summary>
    internal interface IFrameRecorder
    {
        /// <summary>
        /// records the given data value for the given frame
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="frame"></param>
        /// <param name="value"></param>
        void Record<T>(int frame, T value);
        /// <summary>
        /// saves all recorded frame values to disk
        /// </summary>
        void SaveToFile();
        /// <summary>
        /// clears all frame records from the FrameRecorder
        /// </summary>
        void Clear();
    }
}
