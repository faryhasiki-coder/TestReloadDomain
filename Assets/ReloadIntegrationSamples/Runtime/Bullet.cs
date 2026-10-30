using UnityEngine;

namespace ReloadIntegrationSamples
{
    public sealed class Bullet : MonoBehaviour
    {
        public void Fire(Vector3 position) => transform.position = position;
    }
}
