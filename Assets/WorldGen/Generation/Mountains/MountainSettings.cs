using System;

namespace WorldGen.Generation.Mountains
{
    /// <summary>
    /// Все числа алгоритма в одном месте, в мировых единицах карты. Значения по умолчанию — те, что
    /// подобраны в прототипе `docs/mountain-brush-step1.html` (радиус горы там 44 px, и всё остальное
    /// выражено через него), поэтому здесь они записаны В ДОЛЯХ РАДИУСА, а не в пикселях: тогда
    /// мелкие и крупные горы выглядят одинаково.
    ///
    /// Настройки живут отдельно от геометрии по той же причине, по какой отдельно живёт MountainShape:
    /// в задаче 6 их будет крутить ДМ ползунками, а в задаче 10 подобранное станет новыми дефолтами.
    /// </summary>
    public sealed class MountainSettings
    {
        /// <summary>R — радиус горы. Задаёт и шаг слоёв 2R при отборе колец, и предельную полуширину
        /// массы, и всё остальное ниже.</summary>
        public float Radius = 10f;

        /// <summary>T = LinkFactor·R — целевая длина звена (§8).</summary>
        public float LinkFactor = 1.6f;

        /// <summary>Разброс длин звеньев: вес звена берётся из 1 ± LengthJitter. Шесть процентов —
        /// достаточно, чтобы гряда не выглядела штампованной, и мало, чтобы звенья не разъезжались.</summary>
        public float LengthJitter = 0.06f;

        /// <summary>a — угол взгляда на землю, одним числом. В метрике §8 вертикаль «дороже» в a раз
        /// (позвонки на вертикальных участках ложатся чаще), а подошва горы во столько же раз
        /// сплющена по вертикали. Это одно и то же соглашение о взгляде, поэтому и число одно.</summary>
        public float Anisotropy = 1.6f;

        /// <summary>t — талия на позвонке: доля полной полуширины на стыке звеньев (§9).</summary>
        public float Waist = 0.55f;

        /// <summary>h — множитель высоты: H = h·w, где w — полуширина в середине звена.</summary>
        public float HeightFactor = 2.2f;

        /// <summary>k — растяжение подошвы вдоль оси (§10). Больше — выше перевалы, плотнее массив.</summary>
        public float Stretch = 1.4f;

        /// <summary>Сколько ярусов различаем (§11). Ярус 0 — гряда по краю массива.</summary>
        public int Tiers = 3;

        /// <summary>Высота горы нулевого яруса как доля высоты самого глубокого (§11).</summary>
        public float EdgeHeight = 0.55f;

        public float LinkLength => Math.Max(1e-4f, Radius * LinkFactor);

        /// <summary>Сжатие подошвы по вертикали — обратное анизотропии.</summary>
        public float Squash => 1f / Math.Max(1f, Anisotropy);

        /// <summary>Шаг выборки силуэта доли. В прототипе это «каждые 3 px», то есть R/15.</summary>
        public float SampleStep => Math.Max(1e-4f, Radius / 15f);

        /// <summary>Ширина подошвы, ниже которой горы нет.</summary>
        public float MinSpan => Radius * 0.1f;

        /// <summary>Толщина яруса — тот же слой 2R, которым отбираются кольца (§4).</summary>
        public float TierBand => Math.Max(1e-4f, 2f * Radius);
    }

    /// <summary>
    /// mulberry32 — тот же генератор, что в прототипе, до последнего бита. Не «какой-нибудь
    /// случайный»: от него зависит, совпадёт ли посчитанное у ДМ с посчитанным на стенде, и не
    /// перетасуется ли уже нарисованный хребет между запусками.
    /// </summary>
    public sealed class Mulberry32
    {
        uint state;

        public Mulberry32(uint seed) { state = seed; }

        public float Next()
        {
            unchecked
            {
                state += 0x6D2B79F5u;
                uint t = state;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return (t ^ (t >> 14)) / 4294967296f;
            }
        }
    }
}
