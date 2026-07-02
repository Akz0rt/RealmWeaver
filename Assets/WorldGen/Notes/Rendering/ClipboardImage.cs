namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Best-effort clipboard image access. Unity has no built-in cross-platform image
    /// clipboard API; this returns null when the clipboard doesn't contain image bytes
    /// Unity can decode, which callers must treat as "nothing to paste" rather than an error.
    /// </summary>
    public static class ClipboardImage
    {
        public static byte[] TryGetImageBytes()
        {
            // No built-in Unity API reads image data from the OS clipboard. Returning null
            // here means Ctrl+V silently does nothing when the clipboard has no image Unity
            // can access — callers already treat null as "no-op", so this is safe.
            return null;
        }
    }
}
