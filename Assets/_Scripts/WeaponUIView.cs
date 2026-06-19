using LightSide;
using UnityEngine;
using UnityEngine.UI;

public class WeaponUIView : MonoBehaviour
{
	[SerializeField] private Image _weaponView;
	[SerializeField] private UniText _ammoText;
	[SerializeField] private UniText _weaponTitle;
	[SerializeField] private UniText _caliberText; // опционально: тип патрона текущего оружия

	public void SetWeaponView(GunConfig config, int ammo)
	{
		_weaponView.sprite = config.Preview;
		_ammoText.Text = $"{config.MagazineCapacity} / {ammo}";
		_weaponTitle.Text = config.Id;
		if (_caliberText != null)
			_caliberText.Text = config.Caliber.ToDisplay();
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
