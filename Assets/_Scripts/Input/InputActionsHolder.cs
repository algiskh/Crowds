using UnityEngine;
using UnityEngine.InputSystem;

public class InputActionsHolder : MonoBehaviour
{
	[SerializeField] private InputActionAsset _actions;

	public InputActionAsset Actions => _actions;

	private void OnEnable() => _actions.Enable();
	private void OnDisable() => _actions.Disable();
}
