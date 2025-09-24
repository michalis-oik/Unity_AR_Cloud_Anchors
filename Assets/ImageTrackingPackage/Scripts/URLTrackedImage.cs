using UnityEngine;

namespace ImageTrackingPackage
{
    [System.Serializable]
    public class URLTrackedImage
    {
        [SerializeField] public string name;
        [SerializeField] public string url;
        [SerializeField] public float physicalImageSize = 0.1f;
    }
}
