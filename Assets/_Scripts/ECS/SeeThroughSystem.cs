using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Feeds the "Crowds/Lit See-Through" shader: every frame writes the player's screen position, eye depth and feet
	/// height into shader globals (<see cref="SeeThroughShader"/>), so environment materials cut a soft dithered round
	/// hole where they cover the player. Cost: three SetGlobalVector calls per frame, nothing per object.
	/// Settings: PlayerConfig.SeeThrough. Registered last — after the final CameraShakeSystem — so it sees the
	/// camera exactly where it renders. On destroy (scene change) the effect is switched off, so other scenes
	/// (main menu) draw the same materials as plain Lit.
	/// </summary>
	public sealed class SeeThroughSystem : IEcsRunSystem, IEcsDestroySystem
	{
		private float _strength;

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var settings = world.GetAsSingleton<MainHolderComponent>().Value.PlayerConfig?.SeeThrough;
			Camera cam = world.TryGetAsSingleton<CameraComponent>(out var camComponent) ? camComponent.Value : null;
			Transform player = world.TryGetAsSingleton<PlayerComponent>(out var playerComponent) && playerComponent.Value != null
				? playerComponent.Value.transform
				: null;

			if (settings == null || cam == null || player == null)
			{
				_strength = 0f;
				SeeThroughShader.Disable();
				return;
			}

			Vector3 focus = player.position + Vector3.up * settings.FocusHeight;
			Vector3 viewport = cam.WorldToViewportPoint(focus);
			bool visible = viewport.z > 0f;

			float target = settings.Enabled && visible && player.gameObject.activeInHierarchy ? 1f : 0f;
			_strength = Mathf.MoveTowards(_strength, target, settings.FadeSpeed * Time.unscaledDeltaTime);

			// viewport.z = distance along the camera forward = the shader's linear eye depth.
			Shader.SetGlobalVector(SeeThroughShader.CenterId,
				new Vector4(viewport.x, viewport.y, settings.Radius, viewport.z));
			Shader.SetGlobalVector(SeeThroughShader.ParamsId,
				new Vector4(settings.EdgeSoftness, settings.DepthMargin, settings.DepthFade, _strength));
			Shader.SetGlobalVector(SeeThroughShader.HeightId,
				new Vector4(player.position.y + settings.HeightOffset, settings.HeightFade, 0f, 0f));
		}

		public void Destroy(IEcsSystems systems) => SeeThroughShader.Disable();
	}
}
