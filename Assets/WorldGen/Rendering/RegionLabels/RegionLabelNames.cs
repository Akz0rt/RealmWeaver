using System.Collections.Generic;
using WorldGen.Rendering.MapRaster; // BiomeFamily

namespace WorldGen.Rendering.RegionLabels
{
    /// <summary>Pure, deterministic Russian region-name generator: a biome-family -> noun table
    /// (the SINGLE place biome names live — kept isolated for the planned biome-type rework) plus a
    /// gender-agreeing adjective pool. Picks a unique adjective per zone within a family. No Random.</summary>
    public static class RegionLabelNames
    {
        public enum Gender { Masculine, Feminine, Neuter, Plural }

        readonly struct Noun
        {
            public readonly string Word;
            public readonly Gender Gender;
            public Noun(string word, Gender gender) { Word = word; Gender = gender; }
        }

        readonly struct Adjective
        {
            public readonly string M, F, N, Pl;
            public Adjective(string m, string f, string n, string pl) { M = m; F = f; N = n; Pl = pl; }
            public string For(Gender g) => g switch
            {
                Gender.Masculine => M,
                Gender.Feminine  => F,
                Gender.Neuter    => N,
                _                => Pl,
            };
        }

        // THE isolated biome-name table. A future biome rework edits ONLY this dictionary.
        static readonly Dictionary<BiomeFamily, Noun> Nouns = new Dictionary<BiomeFamily, Noun>
        {
            { BiomeFamily.Forest,     new Noun("Лес",     Gender.Masculine) },
            { BiomeFamily.ForestWarm, new Noun("Дубрава", Gender.Feminine)  },
            { BiomeFamily.Badlands,   new Noun("Пустошь", Gender.Feminine)  },
            { BiomeFamily.Plains,     new Noun("Луга",    Gender.Plural)    },
            { BiomeFamily.Highland,   new Noun("Кряж",    Gender.Masculine) },
            { BiomeFamily.Snow,       new Noun("Снега",   Gender.Plural)    },
            { BiomeFamily.Moor,       new Noun("Топь",    Gender.Feminine)  },
            { BiomeFamily.Tundra,     new Noun("Тундра",  Gender.Feminine)  },
            { BiomeFamily.Sea,        new Noun("Море",    Gender.Neuter)    },
            // Coast, Lake intentionally absent -> NameFor returns null (unnamed).
        };

        static readonly Adjective[] Adjectives =
        {
            new Adjective("Сумрачный",  "Сумрачная",  "Сумрачное",  "Сумрачные"),
            new Adjective("Пепельный",  "Пепельная",  "Пепельное",  "Пепельные"),
            new Adjective("Золотой",    "Золотая",    "Золотое",    "Золотые"),
            new Adjective("Вечный",     "Вечная",     "Вечное",     "Вечные"),
            new Adjective("Северный",   "Северная",   "Северное",   "Северные"),
            new Adjective("Древний",    "Древняя",    "Древнее",    "Древние"),
            new Adjective("Багряный",   "Багряная",   "Багряное",   "Багряные"),
            new Adjective("Туманный",   "Туманная",   "Туманное",   "Туманные"),
            new Adjective("Забытый",    "Забытая",    "Забытое",    "Забытые"),
            new Adjective("Стылый",     "Стылая",     "Стылое",     "Стылые"),
            new Adjective("Мёртвый",    "Мёртвая",    "Мёртвое",    "Мёртвые"),
            new Adjective("Гиблый",     "Гиблая",     "Гиблое",     "Гиблые"),
            new Adjective("Тихий",      "Тихая",      "Тихое",      "Тихие"),
            new Adjective("Дикий",      "Дикая",      "Дикое",      "Дикие"),
            new Adjective("Хладный",    "Хладная",    "Хладное",    "Хладные"),
            new Adjective("Ветреный",   "Ветреная",   "Ветреное",   "Ветреные"),
            new Adjective("Полуночный", "Полуночная", "Полуночное", "Полуночные"),
            new Adjective("Седой",      "Седая",      "Седое",      "Седые"),
            new Adjective("Угрюмый",    "Угрюмая",    "Угрюмое",    "Угрюмые"),
            new Adjective("Мглистый",   "Мглистая",   "Мглистое",   "Мглистые"),
            new Adjective("Кровавый",   "Кровавая",   "Кровавое",   "Кровавые"),
            new Adjective("Терновый",   "Терновая",   "Терновое",   "Терновые"),
            new Adjective("Вороний",    "Воронья",    "Воронье",    "Вороньи"),
            new Adjective("Волчий",     "Волчья",     "Волчье",     "Волчьи"),
        };

        /// <summary>Deterministic FNV-1a-style mix of seed+zoneKey (no Random, stable across runs).</summary>
        static int Hash(int seed, int zoneKey)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)seed) * 16777619u;
                h = (h ^ (uint)zoneKey) * 16777619u;
                return (int)h;
            }
        }

        // Isolated continent syllable pools (invented fantasy names; unrelated to the biome noun table
        // and to the planned biome-type rework — edit freely).
        static readonly string[] ContinentOnsets =
        { "Вэл","Каэр","Тарн","Морн","Драг","Эль","Вор","Нар","Ске","Тир","Улл","Фэн","Гэл","Хад","Рун","Аск" };
        static readonly string[] ContinentCodas =
        { "дрим","вейл","морн","гард","холд","рун","тар","нор","вен","дал","мир","рат","гейт","ланд" };

        /// <summary>Deterministic invented (fantasy proper-noun) landmass name, e.g. "Вэлдрим", "Каэрхолд".
        /// Two decorrelated draws from the FNV hash so onset and coda vary independently. Shares the
        /// caller's seed, so the reroll salt (baked into seed) varies continent names too.</summary>
        public static string ContinentName(int seed, int key)
        {
            int a = (int)((uint)Hash(seed, key) % (uint)ContinentOnsets.Length);
            int b = (int)((uint)Hash(seed, unchecked(key * 31 + 0x2545F491)) % (uint)ContinentCodas.Length);
            return ContinentOnsets[a] + ContinentCodas[b];
        }

        /// <summary>Composed name for a zone, or null if the family is unnamed (Coast/Lake).
        /// Picks the hashed adjective index, linear-probing forward for one not yet used by THIS
        /// family on THIS map (mutates usedAdjIndices). If all are used (>pool zones of one family),
        /// reuses the last probed index.</summary>
        public static string NameFor(BiomeFamily family, int seed, int zoneKey, HashSet<int> usedAdjIndices)
        {
            if (!Nouns.TryGetValue(family, out var noun)) return null;
            int n = Adjectives.Length;
            int start = (int)((uint)Hash(seed, zoneKey) % (uint)n);
            int chosen = start;
            for (int probe = 0; probe < n; probe++)
            {
                int cand = (start + probe) % n;
                chosen = cand;
                if (usedAdjIndices.Add(cand)) break; // found an unused adjective for this family
            }
            return Adjectives[chosen].For(noun.Gender) + " " + noun.Word;
        }
    }
}
