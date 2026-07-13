namespace WorldGen.Rendering
{
    /// <summary>Resolves a POI's political-region name from its owner cell, decoupled from concrete
    /// types (delegates) so it's pure and self-testable. Empty string when the POI has no owner cell,
    /// its cell has no region, or the region has no name.</summary>
    public static class PoiRegionLabel
    {
        public static string Resolve(int ownerCellId,
            System.Func<int, int> regionIdByCell, System.Func<int, string> regionNameById)
        {
            if (ownerCellId < 0) return "";
            int regionId = regionIdByCell(ownerCellId);
            if (regionId < 0) return "";
            string name = regionNameById(regionId);
            return string.IsNullOrEmpty(name) ? "" : name;
        }
    }
}
