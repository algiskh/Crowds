using Sirenix.OdinInspector;
using Sirenix.Serialization;
using UnityEngine;

[CreateAssetMenu(fileName = "BonusConfig", menuName = "Scriptable Objects/BonusConfig")]
public class BonusConfig : ScriptableObject
{
	[Title("Identity")]
	[PreviewField(60, ObjectFieldAlignment.Left), HideLabel, HorizontalGroup("Top", 70)]
	[SerializeField] private Sprite _preview;

	[VerticalGroup("Top/Right"), LabelText("ID"), Delayed]
	[SerializeField] private string _id;

	[VerticalGroup("Top/Right"), LabelText("Bonus type")]
	[SerializeField] private BonusType _type;

	[Title("Modifier")]
	[InfoBox("Speed: add a SpeedModifier (Value = speed multiplier, e.g. 1.5). " +
		"Shield: add a ShieldModifier (Value = incoming-damage multiplier, e.g. 0.5 = 50% reduction). " +
		"Lifetime = bonus duration in seconds (drives the bar fill and the seconds-left text).")]
	[SerializeReference, OdinSerialize] private Modifier _modifier;

	public string Id => _id;
	public Sprite Preview => _preview;
	public BonusType Type => _type;
	public Modifier Modifier => _modifier;

	/// <summary>Свежий клон модификатора бонуса — каждый подбор получает свой инстанс с собственным Lifetime.</summary>
	public Modifier CreateModifierInstance() => _modifier?.Clone<Modifier>();
}
