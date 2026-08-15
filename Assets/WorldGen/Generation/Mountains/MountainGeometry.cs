using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation.Mountains
{
    /// <summary>
    /// Конец конвейера: мазки → пятно → маска → поле расстояний → оси → ЗВЕНЬЯ → доли → ГОРЫ, готовые
    /// к рисованию. Всё, что выше по течению, уже посчитано в своих файлах; здесь их складывают и
    /// переводят в мировые единицы карты.
    ///
    /// Границу «сетка / мир» держим ровно тут. Оси приходят в координатах сетки (МЕДИАЛЬНАЯ ось
    /// снята с растра, и мерить её в ячейках честнее), а звенья, доли и горы живут уже в
    /// Site-координатах: их отдают рендеру, и лишний пересчёт масштаба в каждом файле — это ровно то
    /// место, где рано или поздно теряется множитель.
    /// </summary>
    public static class MountainGeometry
    {
        /// <summary>Смещение зерна для каждой следующей оси — простое число, как в прототипе.
        /// Осталось от прежней схемы «зерно пятна + номер оси»; сама схема снята (см. AxisSeed).</summary>
        public const uint AxisSeedStep = 7919u;

        /// <summary>Шаг округления положения оси при выводе зерна, в мировых единицах.</summary>
        public const float AxisSeedQuantum = 1f;

        /// <summary>
        /// Зерно разброса оси — из ЕЁ СОБСТВЕННОГО ПОЛОЖЕНИЯ, а не из личности массива.
        ///
        /// Так пришлось сделать после разворота 2026-08-14. Раньше зерно бралось от самого старого
        /// мазка пятна, и это держало уже нарисованное на месте при дорисовке. У массива из клеток
        /// «самого старого» нет вовсе, а любая замена — номер компоненты, наименьший номер клетки —
        /// меняется от каждой правки где угодно и перетасовала бы принятый хребет молча.
        ///
        /// Честная оговорка: полной устойчивости не бывает и не было. Ось пересчитывается вместе с
        /// массой, и удлинение гряды двигает её звенья в любом случае. Что зерно от положения
        /// ГАРАНТИРУЕТ — соседи не мешают друг другу: нарисовал второй массив на другом конце карты,
        /// первый не шелохнулся. Это и проверяется стендом.
        /// </summary>
        public static uint AxisSeed(IReadOnlyList<Vector2> pts)
        {
            if (pts == null || pts.Count == 0) return 0u;
            Vector2 mid = pts[pts.Count / 2];
            int qx = (int)Math.Round(mid.X / AxisSeedQuantum);
            int qy = (int)Math.Round(mid.Y / AxisSeedQuantum);
            return unchecked(MountainBlob.Fnv(qx) * 16777619u ^ MountainBlob.Fnv(qy));
        }

        /// <summary>Весь путь от пятна до гор. Удобно и для рендера, и для проверок: короче него
        /// сквозной проверки не напишешь.</summary>
        public static List<MountainShape> Build(MountainBlob blob, MountainSettings settings)
            => Build(blob, settings, out _, out _);

        public static List<MountainShape> Build(MountainBlob blob, MountainSettings settings,
                                                out MountainMask mask, out List<AxisLink> links)
        {
            mask = null;
            links = new List<AxisLink>();
            if (blob == null || settings == null || blob.Strokes.Count == 0) return new List<MountainShape>();

            float brush = float.PositiveInfinity;
            foreach (var s in blob.Strokes) if (s.Radius < brush) brush = s.Radius;
            foreach (var s in blob.Erasers) if (s.Radius < brush) brush = s.Radius;

            mask = MountainMask.Build(blob, MountainMask.ChooseCell(settings.Radius, brush), settings.IsLand);
            if (mask == null) return new List<MountainShape>();
            return BuildFromMask(mask, settings, out links);
        }

        /// <summary>
        /// Тот же путь от ГОТОВОЙ маски. Приложение ходит сюда: маску ему даёт рельеф (клетки, у
        /// которых высота дотянула до «горы»), а не мазок.
        /// </summary>
        public static List<MountainShape> BuildFromMask(MountainMask mask, MountainSettings settings,
                                                        out List<AxisLink> links)
        {
            links = new List<AxisLink>();
            if (mask == null || settings == null) return new List<MountainShape>();

            var field = DistanceField.Build(mask);
            var axes = AxisBuilder.Build(mask, field, settings.Radius / mask.Cell);
            links = BuildLinks(mask, axes, settings);
            return Build(links, settings);
        }

        /// <summary>
        /// Оси (в координатах сетки) → звенья (в мировых) с розданными ярусами. Отдельно от гор,
        /// потому что звенья нужны и сами по себе — по ним рисуется отладочный показ. (Рельеф из
        /// них НЕ считается: с 2026-08-14 течение обратное — это рельеф задаёт маску.)
        /// </summary>
        public static List<AxisLink> BuildLinks(MountainMask mask, IReadOnlyList<MountainAxis> axes,
                                                MountainSettings settings)
        {
            var links = new List<AxisLink>();
            if (mask == null || axes == null || settings == null) return links;

            for (int i = 0; i < axes.Count; i++)
            {
                var axis = axes[i];
                if (axis == null || axis.Points.Count < 2) continue;

                var pts = new List<Vector2>(axis.Points.Count);
                var wid = new List<float>(axis.Points.Count);
                var dep = new List<float>(axis.Points.Count);
                for (int j = 0; j < axis.Points.Count; j++)
                {
                    pts.Add(mask.GridToWorld(axis.Points[j].X, axis.Points[j].Y));
                    wid.Add(axis.Widths[j] * mask.Cell);
                    dep.Add(axis.Depths[j] * mask.Cell);
                }

                var rng = new Mulberry32(AxisSeed(pts));
                links.AddRange(LinkSplitter.Split(pts, wid, dep, axis.Closed, axis.Tip0, axis.Tip1,
                                                  settings.LinkLength, settings.LengthJitter,
                                                  settings.Anisotropy, rng));
            }

            AssignTiers(links, settings);
            return links;
        }

        /// <summary>
        /// §11 «Ярусы»: чем глубже внутри массы стоит гора, тем она выше. Границы ярусов — те же слои
        /// по 2R, которыми отбираются кольца, поэтому ярус 0 — гряда по краю мазка, ярус 1 — первый
        /// слой внутрь, и так далее.
        ///
        /// Высота нормируется на самый глубокий ярус ЭТОГО пятна, а не на предельный номер: у
        /// узкого мазка, где слой всего один, горы иначе вышли бы поголовно приземистыми — «край»
        /// без «сердцевины», с которой его сравнивают.
        /// </summary>
        public static void AssignTiers(List<AxisLink> links, MountainSettings settings)
        {
            if (links == null || settings == null) return;

            int maxTier = 0;
            int limit = Math.Max(1, MountainSettings.Tiers) - 1;
            foreach (var link in links)
            {
                int tier = (int)Math.Floor(Math.Max(0f, link.MidDepth) / settings.TierBand);
                link.Tier = Math.Min(limit, tier);
                if (link.Tier > maxTier) maxTier = link.Tier;
            }

            float edge = settings.EdgeHeight;
            foreach (var link in links)
                link.TierScale = maxTier > 0 ? edge + (1f - edge) * link.Tier / maxTier : 1f;
        }

        /// <summary>Звенья → горы, уже в порядке рисования.</summary>
        public static List<MountainShape> Build(IReadOnlyList<AxisLink> links, MountainSettings settings)
        {
            var shapes = new List<MountainShape>();
            if (links == null || settings == null) return shapes;

            // Одна выборка кривой подъёма на весь пересчёт: она зависит только от остроты, а Acos и
            // Pow внутри неё стоят дороже всего остального поиска границы вместе взятого.
            var profile = settings.Profile();

            foreach (var link in links)
            {
                var outline = LinkOutline.Build(link, settings.Waist, settings.SampleStep);
                if (outline == null) continue;

                // §14 «вылет за мазок»: растягиваем подошву только туда, где есть сосед.
                float back = link.FreeStart ? 1f : settings.Stretch;
                float forward = link.FreeEnd ? 1f : settings.Stretch;
                var shape = MoundBuilder.Build(outline, link, settings.HeightFactor, settings.Squash,
                                               back, forward, settings.MinSpan, profile, settings.Jag);
                if (shape != null) shapes.Add(shape);
            }

            // Тона по глубине (воздушной перспективы, §10) здесь больше нет: решением ДМ 2026-08-14
            // цвет задаёт ЯРУС — внешнему слою один, среднему другой, внутреннему третий, чтобы
            // читалась форма массы. Долю вдоль шкалы считает MountainTierRamp, краски берёт рендер.
            SortForPainting(shapes);
            return shapes;
        }

        /// <summary>
        /// Порядок маляра. Дальняя гора рисуется раньше, ближняя закрывает её собой; «ближе» — это
        /// НИЖЕ по экрану, то есть меньше Y, поэтому порядок — по убыванию Depth.
        ///
        /// При равных Y разбираем по ярусу (§14 «глубина между слоями»): вложенное кольцо стоит
        /// глубже в массе, значит оно дальше и уходит под своего соседа. Последний ключ — исходный
        /// номер: List.Sort неустойчива, и без него две одинаковые по глубине горы могли бы меняться
        /// местами от запуска к запуску, а рисунок обязан быть повторяемым.
        /// </summary>
        public static void SortForPainting(List<MountainShape> shapes)
        {
            if (shapes == null || shapes.Count < 2) return;

            var order = new int[shapes.Count];
            var copy = new MountainShape[shapes.Count];
            for (int i = 0; i < shapes.Count; i++) { order[i] = i; copy[i] = shapes[i]; }

            Array.Sort(order, (a, b) =>
            {
                int byDepth = copy[b].Depth.CompareTo(copy[a].Depth);
                if (byDepth != 0) return byDepth;
                int byTier = copy[b].Tier.CompareTo(copy[a].Tier);
                if (byTier != 0) return byTier;
                return a.CompareTo(b);
            });

            for (int i = 0; i < order.Length; i++) shapes[i] = copy[order[i]];
        }
    }
}
