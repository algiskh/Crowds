using UnityEngine;

[CreateAssetMenu(fileName = "GunConfig", menuName = "Scriptable Objects/GunConfig")]
public class GunConfig : ScriptableObject
{
    [SerializeField] private string _id;
    [SerializeField] private float _bulletSpeed;
    [SerializeField] private float _bulletDamage;
    [SerializeField] private float _bulletLifeTime;
	[SerializeField] private float _fireRate; // pause between shots in seconds
	[SerializeField] private float _radius; // radius of hit area
	[SerializeField] private int _magazineCapacity;
	[SerializeField] private string _fireSoundId;
	[SerializeField] private Bullet _bulletPrefab;
	public string Id => _id;
	public float BulletSpeed => _bulletSpeed;
	public float BulletDamage => _bulletDamage;
	public float BulletLifeTime => _bulletLifeTime;
	public float FireCoolDown => _fireRate;
	public int MagazineCapacity => _magazineCapacity;
	public string FireSoundId => _fireSoundId;
	public Bullet BulletPrefab => _bulletPrefab;
	public float BulletRadius => _radius;
}
