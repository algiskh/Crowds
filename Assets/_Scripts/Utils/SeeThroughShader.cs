using UnityEngine;

/// <summary>
/// Global shader inputs of the "Crowds/Lit See-Through" shader (Assets/Shader/CrowdsSeeThrough.hlsl), written by
/// ECS.SeeThroughSystem. Environment builders create their materials on this shader (falls back to URP Lit).
/// </summary>
public static class SeeThroughShader
{
	public const string ShaderName = "Crowds/Lit See-Through";

	public static readonly int CenterId = Shader.PropertyToID("_CrowdsSeeThrough");
	public static readonly int ParamsId = Shader.PropertyToID("_CrowdsSeeThroughParams");
	public static readonly int HeightId = Shader.PropertyToID("_CrowdsSeeThroughHeight");

	public static Shader FindLit() => Shader.Find(ShaderName) ?? Shader.Find("Universal Render Pipeline/Lit");

	/// <summary>Strength 0: every See-Through material renders exactly like Lit (menus, edit mode, no player).</summary>
	public static void Disable() => Shader.SetGlobalVector(ParamsId, Vector4.zero);
}
