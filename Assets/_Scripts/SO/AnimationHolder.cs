using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(fileName = "AnimationHolder", menuName = "Scriptable Objects/AnimationHolder")]
public class AnimationHolder : ScriptableObject
{
    [SerializeField] private List<Animations> _animations;

    public AnimationClip GetAnimationById(string animationGroupId, string animationId)
	{
		var animationGroup = _animations.FirstOrDefault(a => a.Id == animationGroupId);
		if (animationGroup != null)
		{
			var animationWrapper = animationGroup.AnimationWrapper.Find(aw => aw.Id == animationId);
			if (animationWrapper != null)
			{
				return animationWrapper.Animation;
			}
		}
		return null;
	}
}

[Serializable]
public class Animations
{
    public string Id;
    public List<AnimationWrapper> AnimationWrapper;
}

[Serializable]
public class AnimationWrapper
{
    public string Id;
    public AnimationClip Animation;
}