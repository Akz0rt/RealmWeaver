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
        /// <summary>Доля движения к цели за кадр — но за кадр при опорной частоте 60 Гц, а не за
        /// вызов метода. Опорная частота нужна затем, что доля «за вызов» без неё зависела бы от
        /// того, на сколько шагов нарезано одно и то же время: на 144 Гц кадров вдвое с лишним
        /// больше, чем на 60 Гц, и та же доля от каждого давала бы множителю догнать цель
        /// заметно быстрее — та же болезнь, от которой уже защищена скорость в <see cref="SpeedOf"/>,
        /// осталась бы в сглаживании. 60 Гц — просто точка отсчёта: число 0.3 продолжает означать
        /// «за кадр», и на самой частоте 60 Гц <see cref="Smooth"/> ведёт себя как раньше.</summary>
        public const float SmoothingPerFrame = 0.3f;

        public static float SpeedOf(float distanceCanvasUnits, float deltaSeconds)
            => deltaSeconds <= 0.00001f ? 0f : distanceCanvasUnits / deltaSeconds;

        public static float MultiplierFor(float speedCanvasUnitsPerSecond)
        {
            float t = Mathf.Clamp01(speedCanvasUnitsPerSecond / FastSpeed);
            return Mathf.Lerp(SlowMultiplier, FastMultiplier, t);
        }

        /// <summary>Сглаживает множитель к цели с долей, приведённой к прошедшему времени, а не к
        /// числу вызовов: та же доля «за вызов» на слабом компьютере (кадры реже) сходилась бы к
        /// цели медленнее по времени, чем на быстром — та же рука, другая картинка, только теперь
        /// в сглаживании, а не в скорости. При неположительном <paramref name="deltaSeconds"/>
        /// (кадр ещё не начался или время не идёт) значение не двигается и не даёт NaN.</summary>
        public static float Smooth(float previous, float target, float deltaSeconds)
        {
            if (deltaSeconds <= 0f) return previous;
            float t = 1f - Mathf.Pow(1f - SmoothingPerFrame, deltaSeconds * 60f);
            return Mathf.Lerp(previous, target, t);
        }

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
