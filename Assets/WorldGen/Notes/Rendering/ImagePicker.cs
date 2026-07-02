using SFB;

namespace WorldGen.Notes.Rendering
{
    /// <summary>
    /// Opens a native file-open dialog (via StandaloneFileBrowser) filtered to common
    /// image formats and returns the selected file's raw bytes, or null if the user
    /// cancelled.
    /// </summary>
    public static class ImagePicker
    {
        static readonly ExtensionFilter[] Filters =
        {
            new ExtensionFilter("Images", "png", "jpg", "jpeg", "gif"),
        };

        public static byte[] OpenFileDialog()
        {
            var paths = StandaloneFileBrowser.OpenFilePanel("Выбрать изображение", "", Filters, false);
            if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return null;
            return System.IO.File.ReadAllBytes(paths[0]);
        }
    }
}
