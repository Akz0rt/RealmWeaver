Shader "WorldGen/MapTerrain"
{
    Properties
    {
        _CellIdTex ("Cell Id", 2D) = "black" {}
        _AttrTex ("Attributes", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _CellIdTex;
            sampler2D _AttrTex;
            float _AttrWidth;
            float _CellRows;
            float4 _Palette[16];   // индекс = BiomeFamily (0..10), плоский цвет семейства
            float2 _MapSize;
            float _Mode;

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }

            // тексел атрибутов клетки cid: slot 0 = A (family,elev,temp,water), slot 1 = B (region)
            float4 attr(int cid, int slot)
            {
                int x = cid % (int)_AttrWidth;
                int y = cid / (int)_AttrWidth + slot * (int)_CellRows;
                float2 uv = float2((x + 0.5) / _AttrWidth, (y + 0.5) / (_CellRows * 2.0));
                return tex2Dlod(_AttrTex, float4(uv, 0, 0));
            }

            fixed4 frag (v2f i) : SV_Target
            {
                int cid = (int)(tex2Dlod(_CellIdTex, float4(i.uv, 0, 0)).r + 0.5);
                if (cid < 0) return fixed4(0, 0, 0, 1);
                float4 a = attr(cid, 0);
                int family = (int)(a.r + 0.5);
                return fixed4(_Palette[family].rgb, 1);
            }
            ENDCG
        }
    }
}
