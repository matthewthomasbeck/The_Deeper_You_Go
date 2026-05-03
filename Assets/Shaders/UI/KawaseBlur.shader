Shader "Hidden/Dungeon/KawaseBlur"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Offset ("Offset", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Offset;

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 o = _Offset * _MainTex_TexelSize.xy;
                float2 uv = i.uv;
                fixed4 c = tex2D(_MainTex, uv + float2(o.x, o.y)) * 0.25;
                c += tex2D(_MainTex, uv + float2(-o.x, o.y)) * 0.25;
                c += tex2D(_MainTex, uv + float2(o.x, -o.y)) * 0.25;
                c += tex2D(_MainTex, uv + float2(-o.x, -o.y)) * 0.25;
                return c;
            }
            ENDCG
        }
    }
}
