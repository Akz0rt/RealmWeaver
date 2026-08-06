using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Notes.Data
{
    /// <summary>Подделка под нажим пера: медленно — толще, быстро — тоньше.
    ///
    /// СКОРОСТЬ МЕРИТСЯ В ЕДИНИЦАХ ДОСКИ В СЕКУНДУ — в той же системе, что базовый диаметр из трёх
    /// кнопок (BrushOps.DiameterInCanvasUnits), и это не педантизм. Мерь мы её в долях ширины
    /// рисунка, на растянутом вчетверо рисунке то же самое движение руки давало бы вчетверо меньшую
    /// скорость и перо выходило бы заметно толще: характер пера менялся бы от размера рисунка,
    /// хотя базовая толщина от него намеренно не зависит. Перевод в доли делается один раз, там же,
    /// где и для базового диаметра.
    ///
    /// И В СЕКУНДАХ, А НЕ ЗА КАДР: иначе на слабом компьютере рисунок выходит толще, чем на
    /// быстром. Та же рука, другая картинка.</summary>
    public static class StrokeWidthOps
    {
        public const float SlowMultiplier = 1.3f;
        public const float FastMultiplier = 0.7f;
        /// <summary>Единиц доски в секунду, при которых перо считается «быстрым».</summary>
        public const float FastSpeed = 400f;
        /// <summary>Доля движения к цели за кадр. Множитель сглаживается по времени, иначе каждый
        /// рывок мыши даёт свою кочку.</summary>
        public const float SmoothingPerFrame = 0.3f;

        public static float SpeedOf(float distanceCanvasUnits, float deltaSeconds)
            => deltaSeconds <= 0.00001f ? 0f : distanceCanvasUnits / deltaSeconds;

        public static float MultiplierFor(float speedCanvasUnitsPerSecond)
        {
            float t = Mathf.Clamp01(speedCanvasUnitsPerSecond / FastSpeed);
            return Mathf.Lerp(SlowMultiplier, FastMultiplier, t);
        }

        public static float Smooth(float previous, float target)
            => Mathf.Lerp(previous, target, SmoothingPerFrame);

        /// <summary>У первой точки скорости ещё нет — она берёт толщину второй. Иначе каждый мазок
        /// начинается с кляксы в базовую толщину. Мазок из одной точки (тычок кистью) остаётся как
        /// есть: заимствовать не у кого, и это законный рисунок, а не ошибка.</summary>
        public static void FixFirstWidth(List<StrokePoint> points)
        {
            if (points == null || points.Count < 2) return;
            var first = points[0];
            first.W = points[1].W;
            points[0] = first;
        }
    }
}
