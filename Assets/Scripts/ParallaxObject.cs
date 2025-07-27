using UnityEngine;

public class ParallaxObject : MonoBehaviour
{
    [SerializeField] private float _parallaxFactor = 0.5f;
    public float ParallaxFactor => _parallaxFactor;

}
