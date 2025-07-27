using UnityEngine;

namespace Scene.Animation
{
    public interface IAnimator
    {
        void PlayAnimation(string key, Vector3 direction = default);
        void PlayAnimation(AnimationType type, Vector3 direction = default);
        void Play();
        void Pause();
        void Stop();
    }
}