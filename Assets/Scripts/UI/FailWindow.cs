using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class FailWindow : MonoBehaviour
{
	[SerializeField] private Button _restartButton;
	[SerializeField] private Button _quitButton;
	[SerializeField] private Canvas _canvas;
	public void Awake()
	{
		_quitButton.onClick.AddListener(OnPressQuit);
		_restartButton.onClick.AddListener(OnPressRestart);
	}

	private void OnPressQuit()
	{
		Application.Quit();
	}

	private void OnPressRestart()
	{
		SceneManager.LoadScene(SceneManager.GetActiveScene().name);
	}

	public void Open()
	{
		_canvas.enabled = true;
	}
}
