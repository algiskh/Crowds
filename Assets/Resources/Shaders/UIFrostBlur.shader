// Dual-Kawase style blur used by the pause menu's frosted-glass panel (PauseMenu.cs).
// Runs only once when the menu opens (the world is frozen meanwhile), on a 1/4-resolution copy of the
// screen, so it costs nothing per frame. Lives in Resources so a build always includes it.
Shader "Hidden/Crowds/UIFrostBlur"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
		_Offset ("Offset", Float) = 1
	}
	SubShader
	{
		Cull Off ZWrite Off ZTest Always

		CGINCLUDE
		#include "UnityCG.cginc"

		sampler2D _MainTex;
		float4 _MainTex_TexelSize;
		float _Offset;

		struct v2f
		{
			float4 pos : SV_POSITION;
			float2 uv : TEXCOORD0;
		};

		v2f vert(appdata_img v)
		{
			v2f o;
			o.pos = UnityObjectToClipPos(v.vertex);
			o.uv = v.texcoord;
			return o;
		}
		ENDCG

		// 0: downsample (centre + 4 diagonal taps)
		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			fixed4 frag(v2f i) : SV_Target
			{
				float2 d = _MainTex_TexelSize.xy * _Offset;
				fixed4 c = tex2D(_MainTex, i.uv) * 4;
				c += tex2D(_MainTex, i.uv + float2(-d.x, -d.y));
				c += tex2D(_MainTex, i.uv + float2( d.x, -d.y));
				c += tex2D(_MainTex, i.uv + float2(-d.x,  d.y));
				c += tex2D(_MainTex, i.uv + float2( d.x,  d.y));
				return c / 8;
			}
			ENDCG
		}

		// 1: upsample (8-tap tent)
		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			fixed4 frag(v2f i) : SV_Target
			{
				float2 d = _MainTex_TexelSize.xy * _Offset;
				fixed4 c = tex2D(_MainTex, i.uv + float2(-d.x * 2, 0));
				c += tex2D(_MainTex, i.uv + float2( d.x * 2, 0));
				c += tex2D(_MainTex, i.uv + float2(0, -d.y * 2));
				c += tex2D(_MainTex, i.uv + float2(0,  d.y * 2));
				c += tex2D(_MainTex, i.uv + float2(-d.x,  d.y)) * 2;
				c += tex2D(_MainTex, i.uv + float2( d.x,  d.y)) * 2;
				c += tex2D(_MainTex, i.uv + float2(-d.x, -d.y)) * 2;
				c += tex2D(_MainTex, i.uv + float2( d.x, -d.y)) * 2;
				return c / 12;
			}
			ENDCG
		}
	}
}
