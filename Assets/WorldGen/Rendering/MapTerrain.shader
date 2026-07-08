Shader "WorldGen/MapTerrain"
{
    Properties
    {
        _CellIdTex ("Cell Id", 2D) = "black" {}
        _AttrTex ("Attributes", 2D) = "black" {}
        _LabelTex ("Region Labels", 2D) = "black" {}
        _LandDistTex ("Land->Water Dist", 2D) = "black" {}
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

            float4 _OutlineColor;
            float2 _CellIdTexel;   // (1/texW, 1/texH) - шаг соседа для обводки, независим от зума

            float4 _BiomeLineColor;
            float _BiomeLineStrength;

            sampler2D _LabelTex;   // R=familyLabel, G=bandLabel (255 = нет метки), B=сглаженная маска суша/вода
            float2 _LabelTexel;

            float _ElevBands;
            float _BandContrast;
            float _ReliefStrength;
            float _ReliefStep;     // шаг градиента высоты в UV (~размер клетки, чтобы сосед был другой клеткой)
            float _LightAzimuth;
            float _ReliefAmbient;
            float _ColdLight;
            float4 _LightColor;

            float4 _TintCool;
            float4 _TintWarm;
            float4 _SeaShallow;
            float4 _SeaDeep;
            float4 _LakeShallow;
            float4 _LakeDeep;
            float _Darkness;
            float _GrainAmount;
            float _GrainScale;
            float _TintStrength;   // сила региональной тонировки по температуре (0 = чистый цвет семейства)

            sampler2D _CoastTex;   // RFloat: дистанция до берега в пикселях (0 на суше)
            float _WaterDepthRange; // px, за сколько от берега вода становится "глубокой"
            float _GlowWidth;       // px ширины ореола берега (сторона воды)
            float4 _GlowColor;

            sampler2D _LandDistTex; // RFloat: дистанция до ближайшей воды в пикселях (0 на воде, растёт вглубь суши)
            float _BeachWidth;      // px ширины мягкого песчаного перехода на суше
            float _BeachHardness;   // резкость перехода песок→биом (степень в pow), больше = резче/уже
            float _BeachStrength;   // 0..1 сила подмешивания цвета песка
            float4 _BeachColor;

            float _ShowBiome;   // 1 = цвет семейства, 0 = нейтральная база (слой "Биом/климат")
            float _ShowRelief;  // 1 = ступени высоты + hillshade, 0 = плоско (слой "Рельеф")

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

            // Метка области в пикселе: R=family, G=band; 255 = нет метки (откат к attribute).
            int2 labelAt(float2 uv)
            {
                float2 l = tex2Dlod(_LabelTex, float4(uv, 0, 0)).rg * 255.0;
                return int2((int)(l.x + 0.5), (int)(l.y + 0.5));
            }

            // Сглаженная маска суша/вода в пикселе (B-канал, LINEAR-текстура: суша=255→1.0, вода=0→0.0).
            bool landAt(float2 uv)
            {
                return tex2Dlod(_LabelTex, float4(uv, 0, 0)).b > 0.5;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash21(i), b = hash21(i + float2(1,0));
                float c = hash21(i + float2(0,1)), d = hash21(i + float2(1,1));
                return lerp(lerp(a,b,u.x), lerp(c,d,u.x), u.y);
            }
            float fbm(float2 p)
            {
                float s = 0, amp = 0.5, freq = 1;
                for (int k = 0; k < 4; k++) { s += amp * vnoise(p * freq); freq *= 2; amp *= 0.5; }
                return s;
            }
            int cellAt(float2 uv)
            {
                return (int)(tex2Dlod(_CellIdTex, float4(uv, 0, 0)).r + 0.5);
            }

            // тип воды соседней клетки по смещению duv (0=суша,1=океан,2=озеро)
            int waterAt(float2 uv)
            {
                int cid = cellAt(uv);
                if (cid < 0) return 1; // за картой считаем водой
                return (int)(attr(cid, 0).a + 0.5);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 wuv = i.uv;
                int cid = cellAt(wuv);
                if (cid < 0) return fixed4(0, 0, 0, 1);

                float4 a = attr(cid, 0);
                int family = (int)(a.r + 0.5);
                int water = (int)(a.a + 0.5);   // 0=суша, 1=океан, 2=озеро
                float elev = a.g;
                float temp = a.b;
                float3 col;

                bool renderWater = !landAt(i.uv); // сглаженный берег (baked-маска), а не гранёная per-cell attr.a

                if (renderWater)
                {
                    // Вода: плавная глубина по полю дистанции берега (мелко у берега → глубоко вдали).
                    // wt: если сглаженная маска говорит "вода", а per-cell attr - суша (тонкая полоса
                    // расхождения на границе сглаживания), по умолчанию считаем океаном.
                    int wt = (water > 0) ? water : 1;
                    float cd = tex2Dlod(_CoastTex, float4(wuv, 0, 0)).r;
                    float depth = saturate(cd / max(1.0, _WaterDepthRange));
                    float3 shallow = (wt == 2) ? _LakeShallow.rgb : _SeaShallow.rgb;
                    float3 deep    = (wt == 2) ? _LakeDeep.rgb    : _SeaDeep.rgb;
                    col = lerp(shallow, deep, depth);
                    col += (fbm(wuv * 60.0) - 0.5) * 0.04;

                    // широкий светлый ореол у берега (сторона воды)
                    float glow = saturate(1.0 - cd / max(1.0, _GlowWidth));
                    col = lerp(col, _GlowColor.rgb, glow * 0.5);
                }
                else
                {
                    // слой "Биом": цвет семейства (сглаженная метка области) или нейтральная база (пергамент)
                    int2 lab = labelAt(i.uv);
                    int famL = (lab.x == 255) ? family : lab.x;   // откат к attribute на клиньях
                    col = (_ShowBiome > 0.5) ? _Palette[famL].rgb : float3(0.82, 0.78, 0.65);

                    // слой "Рельеф": ступень высоты (выше = светлее по дискретным полосам)
                    if (_ShowRelief > 0.5)
                    {
                        int bands = max(2, (int)_ElevBands);
                        int band = (lab.y == 255) ? clamp((int)(elev * bands), 0, bands - 1) : lab.y;
                        float bt = band / max(1.0, (float)(bands - 1));
                        col *= 1.0 + (bt - 0.5) * (_BandContrast / 100.0);
                    }

                    // региональная тонировка по температуре (слабая) - всегда
                    float wn = saturate((temp - 0.28) / 0.42);
                    col = lerp(col, lerp(_TintCool.rgb, _TintWarm.rgb, wn), _TintStrength);

                    // мягкий песок у берега: сила спадает вглубь суши по дистанции до воды
                    float landDist = tex2Dlod(_LandDistTex, float4(i.uv, 0, 0)).r;
                    float beach = pow(saturate(1.0 - landDist / max(1.0, _BeachWidth)), _BeachHardness);
                    col = lerp(col, _BeachColor.rgb, beach * _BeachStrength);

                    // слой "Рельеф": затенение из градиента высоты + холодный лунный подсвет
                    if (_ShowRelief > 0.5)
                    {
                        float s = _ReliefStep;
                        float eL = attr(cellAt(wuv - float2(s, 0)), 0).g;
                        float eR = attr(cellAt(wuv + float2(s, 0)), 0).g;
                        float eD = attr(cellAt(wuv - float2(0, s)), 0).g;
                        float eU = attr(cellAt(wuv + float2(0, s)), 0).g;
                        float gx = (eL - eR) * 0.5, gy = (eD - eU) * 0.5;
                        float3 nrm = normalize(float3(-gx * _ReliefStrength, 1, -gy * _ReliefStrength));
                        float az = radians(_LightAzimuth);
                        float3 L = normalize(float3(sin(az), 1, cos(az)));
                        float ndotl = saturate(dot(nrm, L));
                        float bright = lerp(_ReliefAmbient, 1.0, ndotl);
                        col = col * bright + _LightColor.rgb * ndotl * _ColdLight;
                    }

                    // тонкая тёмная линия между разными СЕМЕЙСТВАМИ биомов (не на берегу:
                    // Coast=2 исключён, там будет мягкий пляж; 255 = клин, тоже пропускаем)
                    float2 flt = _LabelTexel * 1.5;
                    int fa = labelAt(i.uv + float2(flt.x, 0)).x;
                    int fb = labelAt(i.uv - float2(flt.x, 0)).x;
                    int fc = labelAt(i.uv + float2(0, flt.y)).x;
                    int fd = labelAt(i.uv - float2(0, flt.y)).x;
                    bool famEdge =
                        (fa != famL && fa != 255 && fa != 2) ||
                        (fb != famL && fb != 255 && fb != 2) ||
                        (fc != famL && fc != 255 && fc != 2) ||
                        (fd != famL && fd != 255 && fd != 2);
                    if (famEdge && famL != 2) col = lerp(col, _BiomeLineColor.rgb, _BiomeLineStrength);

                    // тёмная обводка берега (сторона суши) - всегда; по сглаженной маске суша/вода,
                    // чтобы обводка следовала гладкому берегу, а не гранёным клеткам.
                    float2 t = _CellIdTexel * 2.0;
                    int w = (!landAt(i.uv + float2(t.x, 0)) ? 1 : 0) + (!landAt(i.uv - float2(t.x, 0)) ? 1 : 0)
                          + (!landAt(i.uv + float2(0, t.y)) ? 1 : 0) + (!landAt(i.uv - float2(0, t.y)) ? 1 : 0);
                    if (w > 0) col = lerp(col, _OutlineColor.rgb, 0.7);
                }

                // зерно (суша и вода)
                col += (vnoise(i.uv * _GrainScale) - 0.5) * _GrainAmount;

                // виньетка - затемнение к краям карты (суша и вода)
                float2 dc = i.uv - 0.5;
                float vign = 1.0 - saturate(length(dc) / 0.5) * saturate(_Darkness / 100.0);
                col *= vign;

                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
