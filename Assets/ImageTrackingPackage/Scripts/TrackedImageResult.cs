using UnityEngine;
using UnityEngine.Events;

namespace ImageTrackingPackage
{
    /// <summary>
    /// Contains the results of a successful image tracking event.
    /// This is a 'struct' which is a lightweight data container.
    /// </summary>
    [System.Serializable]
    public struct TrackedImageResult
    {
        public string ImageName;
        public Transform RootTransform;
        public bool IsRootAnchor;
    }

    /// <summary>
    /// A custom UnityEvent that can pass 'TrackedImageResult' data through the inspector.
    /// This allows other scripts to easily listen for when an image is tracked.
    /// </summary>
    [System.Serializable]
    public class TrackedImageResultEvent : UnityEvent<TrackedImageResult> { }
}
