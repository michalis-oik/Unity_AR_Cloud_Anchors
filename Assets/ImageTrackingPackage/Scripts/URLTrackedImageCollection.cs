using System.Collections.Generic;
using UnityEngine;

namespace ImageTrackingPackage
{
    [System.Serializable]
    public class URLTrackedImageCollection
    {
        [SerializeField] public List<URLTrackedImage> uRLTrackedImages = new List<URLTrackedImage>();
    }
}

