using InternalAPI;

namespace UberClient.Helper
{
    public static class Utility
    {
        public static Stage getStageFromStatus(string status)
        {
            return status switch
            {
                "processing" => Stage.Pending,
                "accepted" => Stage.Accepted,
                "no drivers available" => Stage.Cancelled,
                "completed" => Stage.Completed,
                _ => Stage.Unknown,
            };
        }
    }
}
