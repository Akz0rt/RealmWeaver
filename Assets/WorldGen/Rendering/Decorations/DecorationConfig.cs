using System;
using UnityEngine;

namespace WorldGen.Rendering.Decorations
{
    /// <summary>Параметры расстановки/рендера декораций. Один сериализованный экземпляр на
    /// WorldMapRenderer (правится в Inspector, живой OnValidate); тот же объект передаётся в
    /// DecorationPlacer как вход.</summary>
    [Serializable]
    public class DecorationConfig
    {
        public bool enabled = false;

        [Header("Пороги высоты (EffectiveElevation 0..1)")]
        [Range(0f, 1f)] public float mountainMinElevation = 0.72f; // >= => гора
        [Range(0f, 1f)] public float hillMinElevation = 0.55f;     // [hill,mtn) => холм
        [Tooltip("EffectiveTemperature ниже этого => снежная категория (горы/холмы/хвоя).")]
        public float coldTemperature = 0.32f;

        [Header("Плотность (шаг грида в мировых единицах; меньше = гуще)")]
        public float mountainGridStep = 26f;
        public float hillGridStep = 30f;
        public float pineGridStep = 12f;
        public float autumnGridStep = 12f;
        public float mesaGridStep = 34f;

        [Header("Вероятность постановки в грид-точке [0..1]")]
        [Range(0f, 1f)] public float mountainProbability = 0.55f;
        [Range(0f, 1f)] public float hillProbability = 0.35f;
        [Range(0f, 1f)] public float pineProbability = 0.66f;
        [Range(0f, 1f)] public float autumnProbability = 0.62f;
        [Range(0f, 1f)] public float mesaProbability = 0.18f;

        [Header("Размеры (мировые единицы, высота спрайта)")]
        public float mountainSize = 34f;
        public float hillSize = 16f;
        public float treeSize = 13f;
        public float mesaSize = 14f;
        [Range(0.1f, 3f)] public float globalScale = 1f;
        [Range(0f, 0.6f)] public float sizeJitter = 0.25f; // ± доля к размеру

        [Header("Производительность")]
        public int maxInstances = 6000;

        public float GridStep(DecorationType t) => t switch
        {
            DecorationType.Mountain => mountainGridStep,
            DecorationType.Hill => hillGridStep,
            DecorationType.Pine => pineGridStep,
            DecorationType.AutumnTree => autumnGridStep,
            DecorationType.Mesa => mesaGridStep,
            _ => 20f,
        };

        public float Probability(DecorationType t) => t switch
        {
            DecorationType.Mountain => mountainProbability,
            DecorationType.Hill => hillProbability,
            DecorationType.Pine => pineProbability,
            DecorationType.AutumnTree => autumnProbability,
            DecorationType.Mesa => mesaProbability,
            _ => 0f,
        };

        public float BaseSize(DecorationType t) => t switch
        {
            DecorationType.Mountain => mountainSize,
            DecorationType.Hill => hillSize,
            DecorationType.Pine => treeSize,
            DecorationType.AutumnTree => treeSize,
            DecorationType.Mesa => mesaSize,
            _ => 12f,
        };
    }
}
