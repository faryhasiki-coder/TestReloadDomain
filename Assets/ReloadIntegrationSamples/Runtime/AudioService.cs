using UnityEngine;

namespace ReloadIntegrationSamples
{
    public sealed class AudioService : MonoBehaviour
    {
        public int PlayedClips { get; private set; }

        public void Play(string clip) => PlayedClips++;
    }
}
