namespace WorldGen.Rendering
{
    /// <summary>Detects a double-click on the SAME POI within a time window. Pure/time-injected
    /// so it can be self-tested without the Editor clock. RegisterClick returns true exactly on the
    /// second qualifying click; state resets after firing so a 3rd click starts a fresh pair.</summary>
    public class PoiClickTracker
    {
        public float DoubleClickSeconds = 0.3f;
        string lastId;
        float lastTime;
        bool hasLast;

        public bool RegisterClick(string poiId, float time)
        {
            bool isDouble = hasLast && poiId == lastId && (time - lastTime) <= DoubleClickSeconds;
            if (isDouble) { hasLast = false; lastId = null; return true; }
            hasLast = true; lastId = poiId; lastTime = time;
            return false;
        }
    }
}
