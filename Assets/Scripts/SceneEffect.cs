using UnityEngine;

public class SceneEffect : MonoBehaviour
{
	[SerializeField] private ParticleSystem _particles;
	[SerializeField] private AudioSource _audioSource;
	public string Id { get; private set; }

	public void Initialize(string id)
	{
		Id = id;
	}

	public void Show()
	{
		gameObject.SetActive(true);
		if (_audioSource != null)
		{
			_audioSource.Play();
		}
		if (_particles != null)
		{
			_particles.Play();
		}
	}

	public void Hide()
	{
		gameObject.SetActive(false);
	}
}
