using UnityEngine;
using WorldGen.PlayerPrep.Data;

namespace WorldGen.PlayerPrep.Rendering
{
    /// <summary>Отдаёт слою Data текст поставляемого справочника. Слой Data не имеет права звать
    /// Resources сам — иначе он перестал бы быть чистым и харнесс не смог бы его компилировать.</summary>
    public static class SheetRulesSource
    {
        public const string ResourcePath = "Rules/srd-5.2-ru";
        static RulesData cached;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            RulesTextSource.Provider = () =>
            {
                var asset = Resources.Load<TextAsset>(ResourcePath);
                if (asset == null)
                    throw new System.IO.FileNotFoundException(
                        $"справочник не найден: Assets/Resources/{ResourcePath}.json");
                return asset.text;
            };
        }

        public static RulesData Rules
        {
            get
            {
                if (cached == null)
                {
                    if (RulesTextSource.Provider == null) Install();
                    cached = RulesLoader.FromJson(RulesTextSource.Provider());
                }
                return cached;
            }
        }
    }
}
