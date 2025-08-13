
using System;

[Serializable]
public class SpeedModifier
{
	public string Id;
	public BuffSource Source;
	public float Value;
	public bool IsTemporary;
	public float LifeTime;
}
