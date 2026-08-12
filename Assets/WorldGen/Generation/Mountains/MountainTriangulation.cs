using System;
using System.Collections.Generic;
using System.Numerics;

namespace WorldGen.Generation.Mountains
{
    /// <summary>
    /// Тело горы, разложенное на треугольники. Живёт в чистом слое, а не в сборке меша, по одной
    /// причине: это математика, и её обязан проверять стенд. Рендер только перекладывает индексы.
    ///
    /// Фигура — это две цепи между одними и теми же концами: гребень сверху и ближняя дуга подошвы
    /// снизу. Такую полосу сшивают, идя по обеим цепям сразу и всякий раз продвигая ту, что отстала.
    /// Весь вопрос в том, ЧЕМ мерить отставание.
    ///
    /// Раньше мерили горизонтальной координатой — и это было ошибкой. У звена, которое одновременно
    /// изогнуто и идёт близко к вертикали, дуга подошвы заворачивается назад по X (на кольце такими
    /// выходят девять гор из пятнадцати), после чего «продвинуть отставшего по X» кладёт налезающие
    /// друг на друга треугольники. При сплошной заливке этого не видно, но любая штриховка, боковой
    /// свет или прозрачность сразу проступят пятнами двойной плотности.
    ///
    /// Теперь мерим ДОЛЕЙ ПРОЙДЕННОЙ ДЛИНЫ вдоль своей цепи. Она растёт всегда, как бы цепь ни
    /// виляла, поэтому полоса выходит правильной при любой форме подошвы. Точки при этом не
    /// пересчитываются: в дело идут ровно те, что построил MoundBuilder.
    /// </summary>
    public static class MountainTriangulation
    {
        /// <summary>
        /// Треугольники тела горы. Индексы указывают в объединённый список «сначала все точки
        /// гребня, следом все точки подошвы» — рендер ровно в таком порядке их и складывает.
        /// Обход у всех треугольников одинаковый, хотя фигура и рисуется без отсечения задних
        /// граней: одинаковый обход — единственный способ заметить вывернутый треугольник.
        /// </summary>
        public static int[] Fill(MountainShape shape)
        {
            if (shape == null) return Array.Empty<int>();
            var crest = shape.Crest;
            var front = shape.Front;
            if (crest == null || front == null || crest.Count < 2 || front.Count < 2)
                return Array.Empty<int>();

            float[] alongCrest = Progress(crest);
            float[] alongFront = Progress(front);

            var tris = new List<int>((crest.Count + front.Count) * 3);
            int frontStart = crest.Count;
            int i = 0, j = 0;
            while (i < crest.Count - 1 || j < front.Count - 1)
            {
                bool advanceCrest = j >= front.Count - 1 ||
                                    (i < crest.Count - 1 && alongCrest[i + 1] <= alongFront[j + 1]);
                if (advanceCrest)
                {
                    tris.Add(i); tris.Add(frontStart + j); tris.Add(i + 1);
                    i++;
                }
                else
                {
                    tris.Add(i); tris.Add(frontStart + j); tris.Add(frontStart + j + 1);
                    j++;
                }
            }
            return tris.ToArray();
        }

        /// <summary>Доля пройденной длины в каждой точке цепи: от 0 в начале до 1 в конце.</summary>
        static float[] Progress(List<Vector2> chain)
        {
            var walked = new float[chain.Count];
            for (int i = 1; i < chain.Count; i++)
                walked[i] = walked[i - 1] + Vector2.Distance(chain[i - 1], chain[i]);

            float total = walked[walked.Length - 1];
            if (total <= 0f) return walked;
            for (int i = 0; i < walked.Length; i++) walked[i] /= total;
            return walked;
        }
    }
}
