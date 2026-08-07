namespace WorldGen.Notes.Data
{
    /// <summary>Арифметика портрета. Без UnityEngine — сама пересборка картинки делается вызывающим,
    /// а решение «во сколько раз» проверяется офлайн.</summary>
    public static class PortraitOps
    {
        /// <summary>Предел большей стороны. Портрет живёт в постоянном списке навигатора, поэтому
        /// «как есть» (правило картинки в строке) здесь — ложная аналогия: та одна и лежит на
        /// открытой странице.</summary>
        public const int MaxSide = 512;

        public static bool Fit(int width, int height, out int outWidth, out int outHeight)
        {
            outWidth = width;
            outHeight = height;
            if (width <= 0 || height <= 0) return false;

            int longer = width > height ? width : height;
            if (longer <= MaxSide) return false;

            double k = (double)MaxSide / longer;
            outWidth = (int)System.Math.Round(width * k);
            outHeight = (int)System.Math.Round(height * k);
            if (outWidth < 1) outWidth = 1;
            if (outHeight < 1) outHeight = 1;
            return true;
        }
    }
}
