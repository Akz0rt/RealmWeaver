// Слой нарисованных гор. Единственная задача шейдера — не мешать алгоритму маляра.
//
// Горы перекрывают друг друга: ближняя закрывает дальнюю, и порядок задаётся ПОРЯДКОМ ТРЕУГОЛЬНИКОВ
// в меше (дальние поданы первыми). Поэтому:
//   ZWrite Off  — гора не оставляет глубины, иначе следующая за ней не нарисовалась бы поверх;
//   ZTest Always — и не сверяется с чужой глубиной: слой лежит НАД картой по замыслу, а не по Y;
//   Cull Off    — обход треугольников не выверен намеренно, фигура плоская и видна с одной стороны;
//   Queue       — прозрачная, чтобы очередь шла после земли и после лент берега/границ/рек.
// Цвет несут вершины: тон по глубине считает MountainMeshBuilder, шейдер его только пропускает.
//
// ВАЖНО: шейдер обязан лежать в Project Settings → Graphics → Always Included Shaders, иначе сборка
// его вырежет (Shader.Find в билде вернёт null) — на этом проект уже обжигался, см. память проекта.
Shader "WorldGen/MountainPaint"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+20" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
}
