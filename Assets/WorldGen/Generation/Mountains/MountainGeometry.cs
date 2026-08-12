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
        /// <summary>Смещение зерна для каждой следующей оси — простое число, как в прототипе.</summary>
        public const uint AxisSeedStep = 7919u;

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

            var field = DistanceField.Build(mask);
            var axes = AxisBuilder.Build(mask, field, settings.Radius / mask.Cell);
            links = BuildLinks(mask, axes, blob.Seed, settings);
            return Build(links, settings);
        }

        /// <summary>
        /// Оси (в координатах сетки) → звенья (в мировых) с розданными ярусами. Отдельно от гор,
        /// потому что звенья нужны и сами по себе: по ним рисуется отладочный показ и считается
        /// производный рельеф (задача 8).
        /// </summary>
        public static List<AxisLink> BuildLinks(MountainMask mask, IReadOnlyList<MountainAxis> axes,
                                                uint seed, MountainSettings settings)
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

                // Зерно у каждой оси своё, но выведено из зерна ПЯТНА: дорисовка мазка не
                // перетасовывает уже нарисованное, потому что зерно пятна берётся от старшего мазка.
                var rng = new Mulberry32(unchecked(seed + (uint)i * AxisSeedStep));
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
            int limit = Math.Max(1, settings.Tiers) - 1;
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

            foreach (var link in links)
            {
                var outline = LinkOutline.Build(link, settings.Waist, settings.SampleStep);
                if (outline == null) continue;

                // §14 «вылет за мазок»: растягиваем подошву только туда, где есть сосед.
                float back = link.FreeStart ? 1f : settings.Stretch;
                float forward = link.FreeEnd ? 1f : settings.Stretch;
                var shape = MoundBuilder.Build(outline, link, settings.HeightFactor, settings.Squash,
                                               back, forward, settings.MinSpan);
                if (shape != null) shapes.Add(shape);
            }

            AssignTone(shapes, settings.ToneSpan);
            SortForPainting(shapes);
            return shapes;
        }

        /// <summary>
        /// Воздушная перспектива: дальние горы светлее ближних. Тон считается по глубине подошвы, и
        /// у соседей он поэтому почти одинаков — заливки сливаются без швов, а по всему массиву
        /// набирается плавный переход.
        ///
        /// Размах по умолчанию (span ≤ 0) — фактический, от самой дальней горы ЭТОГО массива до
        /// самой ближней, как в прототипе. Заданный размах в мировых единицах — другой уговор: тон
        /// набирается на первых span единицах вглубь, дальше всё одинаково тёмное. Что выбрать,
        /// решает ДМ на чекпоинте; по умолчанию стоит то, на что он смотрел в прототипе.
        /// </summary>
        public static void AssignTone(List<MountainShape> shapes, float span = 0f)
        {
            if (shapes == null || shapes.Count == 0) return;

            float far = float.NegativeInfinity, near = float.PositiveInfinity;
            foreach (var shape in shapes)
            {
                if (shape.Depth > far) far = shape.Depth;
                if (shape.Depth < near) near = shape.Depth;
            }

            float scale = span > 0f ? span : far - near;
            if (scale <= 0f)
            {
                foreach (var shape in shapes) shape.Tone = 0f;
                return;
            }
            foreach (var shape in shapes)
                shape.Tone = Math.Min(1f, Math.Max(0f, (far - shape.Depth) / scale));
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
