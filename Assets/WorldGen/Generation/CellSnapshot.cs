namespace WorldGen.Generation
{
    /// <summary>
    /// Снимок изменяемых (через override) полей клетки - используется для Undo: сохраняется
    /// один раз при первом затрагивании клетки в течение одного "мазка кистью", и применяется
    /// обратно при отмене (Ctrl+Z), восстанавливая клетку к состоянию до начала мазка.
    ///
    /// Намеренно НЕ включает Polygon/NeighborIds/Site и другие геометрические поля - они никогда
    /// не меняются через override-инструменты, только климат/ландшафт/биом.
    /// </summary>
    public struct CellSnapshot
    {
        public float Height;
        public float Temperature;
        public float Humidity;
        public float? ElevationOverride;
        public float? TemperatureOverride;
        public float? MoistureOverride;
        public WaterOverrideType WaterOverride;
        public Biome Biome;

        public static CellSnapshot Capture(VoronoiCell cell)
        {
            return new CellSnapshot
            {
                Height = cell.Height,
                Temperature = cell.Temperature,
                Humidity = cell.Humidity,
                ElevationOverride = cell.ElevationOverride,
                TemperatureOverride = cell.TemperatureOverride,
                MoistureOverride = cell.MoistureOverride,
                WaterOverride = cell.WaterOverride,
                Biome = cell.Biome
            };
        }

        /// <summary>
        /// Восстанавливает override-поля и итоговый Biome обратно в клетку. Height/Temperature/Humidity
        /// в снапшоте НЕ применяются обратно - это computed-baseline значения от генерации, они
        /// никогда не меняются override-инструментами и потому не нуждаются в откате; хранятся
        /// здесь только для возможной будущей диагностики/сравнения.
        /// </summary>
        public void RestoreOverridesTo(VoronoiCell cell)
        {
            cell.ElevationOverride = ElevationOverride;
            cell.TemperatureOverride = TemperatureOverride;
            cell.MoistureOverride = MoistureOverride;
            cell.WaterOverride = WaterOverride;
            cell.Biome = Biome;
        }
    }
}
