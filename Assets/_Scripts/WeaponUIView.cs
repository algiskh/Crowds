using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class WeaponUIView : MonoBehaviour
{
	[SerializeField] private Image _weaponView;
	[SerializeField] private TMP_Text _ammoText;
	[SerializeField] private TMP_Text _weaponTitle;

	public void SetWeaponView(GunConfig config, int ammo)
	{
		_weaponView.sprite = config.Preview;
		_ammoText.text = $"{config.MagazineCapacity} / {ammo}";
		_weaponTitle.text = config.Id;
	}

	public void UpdateMagazine(int currentAmmo, int maxAmmo)
	{
		_ammoText.text = $"{currentAmmo} / {maxAmmo}";
	}

	public void ShowReloading(float progress)
	{
		_weaponView.fillAmount = progress;
	}
}
