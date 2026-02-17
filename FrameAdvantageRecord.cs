namespace BlazeSyncFix
{
    public class FrameAdvantageRecord
    {
        public readonly int frame;
        public readonly float localAdvantage;
        public readonly float remoteAdvantage;

        public FrameAdvantageRecord(int frame, float localAdvantage, float remoteAdvantage)
        {
            this.frame = frame;
            this.localAdvantage = localAdvantage;
            this.remoteAdvantage = remoteAdvantage;
        }
    }
}
