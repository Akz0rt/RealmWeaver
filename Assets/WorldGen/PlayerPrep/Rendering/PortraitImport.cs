using SFB;
using WorldGen.Rendering;

namespace WorldGen.PlayerPrep.Rendering
{
    /// <summary>Выбор портрета персонажа. 512 по большой стороне — уже установленное в проекте
    /// правило для превью-картинок (см. PoiEditorScreen.OnPickPreviewImageClicked,
    /// DungeonInspectorPanel.PickPreviewImage): вторая своя реализация того же правила разошлась бы
    /// с первой при первой же правке лимита, поэтому переиспользуем ImageImport.LoadAndShrink, а не
    /// пишем уменьшение заново. ImagePicker здесь не подходит — он отдаёт готовые байты, а
    /// LoadAndShrink нужен ПУТЬ к файлу, поэтому диалог открываем напрямую, той же манерой, что и
    /// два образца выше.</summary>
    public static class PortraitImport
    {
        const int MaxSide = 512;

        static readonly ExtensionFilter[] Filters =
        {
            new ExtensionFilter("Images", "png", "jpg", "jpeg", "gif"),
        };

        public static byte[] PickAndDownscale()
        {
            var paths = StandaloneFileBrowser.OpenFilePanel("Выбрать портрет", "", Filters, false);
            if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(paths[0])) return null;
            return ImageImport.LoadAndShrink(paths[0], MaxSide);
        }
    }
}
