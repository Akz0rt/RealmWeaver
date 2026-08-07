using System;
using Newtonsoft.Json;

namespace WorldGen.PlayerPrep.Data
{
    /// <summary>Откуда взять текст поставляемого справочника. Слой Data не имеет права ни читать
    /// файлы, ни звать Resources, поэтому источник ему ПОДКЛАДЫВАЮТ: Unity — из Resources
    /// (SheetRulesSource), харнесс — прямо с диска (Program.cs). Самопроверка, увидев null,
    /// ПАДАЕТ, а не пропускает себя: иначе главная проверка данных стала бы зелёной от отсутствия.
    /// </summary>
    public static class RulesTextSource
    {
        public static Func<string> Provider;
    }

    public static class RulesLoader
    {
        public static RulesData FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("пустой JSON справочника");
            var data = JsonConvert.DeserializeObject<RulesData>(json);
            if (data == null) throw new ArgumentException("JSON справочника не разобрался в RulesData");
            return data;
        }
    }
}
