using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Rendering.Mountains
{
    /// <summary>
    /// Готовые массивы меша — вершины, краски, треугольники — до того, как их отдали Unity.
    ///
    /// Существует ради одного: собирать их можно В ФОНЕ. Vector3 и Color32 — обычные структуры, к
    /// живому движку они не обращаются; на главном потоке остаётся только заливка в Mesh, а это
    /// один вызов на массив. Раньше вся сборка шла на главном потоке, и на большом хребте ДМ видел
    /// заминку в самый неподходящий момент — на отпускании кисти и на отмене.
    ///
    /// Списки переиспользуются между пересчётами (Clear вместо new): за мазок пересчётов десятки, а
    /// список на миллион вершин — это двадцать мегабайт, которые иначе оседают мусором каждый раз.
    /// </summary>
    public sealed class MountainMeshData
    {
        public readonly List<Vector3> Verts = new List<Vector3>();
        public readonly List<Color32> Colors = new List<Color32>();

        /// <summary>Треугольники тел — они пишут глубину и не красят.</summary>
        public readonly List<int> Tris = new List<int>();

        /// <summary>Треугольники туши. Копятся отдельно и приписываются в конец: ВСЕ тела обязаны
        /// лечь раньше любой линии, иначе перекрытие по глубине не сработает — глубина задним
        /// числом уже нарисованное не стирает.</summary>
        public readonly List<int> InkTris = new List<int>();

        /// <summary>Сшивает: сперва все тела, следом вся тушь.</summary>
        public void Seal()
        {
            Tris.AddRange(InkTris);
            InkTris.Clear();
        }

        /// <summary>Сколько чего вышло — чтобы можно было честно сказать ДМ, во что обошёлся рисунок.</summary>
        public int Mountains;
        public int GritMarks;
        public bool GritCapped;

        public void Clear()
        {
            Verts.Clear();
            Colors.Clear();
            Tris.Clear();
            InkTris.Clear();
            Mountains = 0;
            GritMarks = 0;
            GritCapped = false;
        }
    }
}
