using UnityEngine;

public class SceneEffect : MonoBehaviour
{
	[SerializeField] private ParticleSystem _particles;
	[SerializeField] private AudioSource _audioSource;
	// Optional pool of interchangeable sounds; one is picked at random on each Show().
	// Direct clip references (no runtime id lookups) keep this allocation-free and cheap.
	[SerializeField] private AudioClip[] _sounds;
	public string Id { get; private set; }

	public void Initialize(string id)
	{
		Id = id;
	}

	public void Show()
	{
		gameObject.SetActive(true);
		PlaySound();
		if (_particles != null)
		{
			_particles.Play();
		}
	}

	private void PlaySound()
	{
		if (_audioSource == null)
			return;

		if (_sounds != null && _sounds.Length > 0)
		{
			var clip = _sounds[Random.Range(0, _sounds.Length)];
			if (clip != null)
				_audioSource.PlayOneShot(clip);
		}
		else if (_audioSource.clip != null)
		{
			// Backward compatible: play the clip embedded on the AudioSource.
			_audioSource.Play();
		}
	}

	public void SetParent(Transform parent)
	{
		transform.SetParent(parent);
	}

	public void Hide()
	{
		gameObject.SetActive(false);
	}
}
