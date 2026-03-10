using LightSide;
using UnityEngine;
using UnityEngine.UI;

public class WeaponUIView : MonoBehaviour
{
	[SerializeField] private Image _weaponView;
	[SerializeField] private UniText _ammoText;
	[SerializeField] private UniText _weaponTitle;

	public void SetWeaponView(GunConfig config, int ammo)
	{
		_weaponView.sprite = config.Preview;
		_ammoText.Text = $"{config.MagazineCapacity} / {ammo}";
		_weaponTitle.Text = config.Id;
	}

	public void UpdateMagazine(int currentAmmo, int maxAmmo)
	{
		_ammoText.Text = $"{currentAmmo} / {maxAmmo}";
	}

	public void ShowReloading(float progress)
	{
		_weaponView.fillAmount = progress;
	}
}
