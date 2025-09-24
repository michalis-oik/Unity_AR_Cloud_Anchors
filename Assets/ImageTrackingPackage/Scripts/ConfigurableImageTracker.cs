using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.Networking;
using UnityEngine.Events;
using System.Collections;
using System.Collections.Generic;

namespace ImageTrackingPackage
{
    public class ConfigurableImageTracker : MonoBehaviour
    {
        public enum TrackingMode
        {
            AnchorBased,
            TransformBased
        }

        public enum TrackingState
        {
            NotInitialized,
            Downloading,
            ReadyToScan,
            Tracking,
            Limited,
            Lost
        }

        public enum LibrarySource
        {
            DynamicCreation,
            InspectorReference
        }

        [Header("AR System Dependencies")]
        [SerializeField] private ARTrackedImageManager trackedImageManager;
        [SerializeField] private ARAnchorManager anchorManager;

        [Header("Tracking Configuration")]
        [SerializeField] private TrackingMode trackingMode = TrackingMode.AnchorBased;
        [SerializeField] private LibrarySource librarySource = LibrarySource.DynamicCreation;

        // [Header("Image Target Setup (Dynamic Creation)")]
        [SerializeField] private URLTrackedImageCollection urlImageCollection;

        // [Header("Reference Library (Inspector Reference Mode Only)")]
        [SerializeField] private XRReferenceImageLibrary referenceImageLibrary;

        // State Variables
        private MutableRuntimeReferenceImageLibrary runtimeLibrary;
        private bool libraryInitialized = false;
        private TrackingState currentState = TrackingState.NotInitialized;

        // Dictionaries to handle multiple images and objects
        private Dictionary<string, Texture2D> downloadedTextures = new Dictionary<string, Texture2D>();
        private Dictionary<string, GameObject> trackedGameObjects = new Dictionary<string, GameObject>();

        [Header("Tracking Events")]
        [Tooltip("Fired when a new image is successfully tracked, providing the result.")]
        public TrackedImageResultEvent OnImageTracked;

        [Header("Setup & Initialization Events")]
        public UnityEvent OnTrackingInitialized;
        public UnityEvent OnAllImagesDownloaded;
        public UnityEvent OnReadyToScan;

        [Header("Tracking State Events")]
        public UnityEvent<TrackingState> OnTrackingStateChanged;
        public UnityEvent OnTrackingLost;
        public UnityEvent OnTrackingReset;

        void Start()
        {
            SetTrackingState(TrackingState.NotInitialized);
            UpdateStatus("Press 'Track Image' to begin");
        }

        void OnEnable()
        {
            if (trackedImageManager != null)
            {
                trackedImageManager.trackablesChanged.AddListener(OnTrackablesChanged);
            }
        }

        void OnDisable()
        {
            if (trackedImageManager != null)
            {
                trackedImageManager.trackablesChanged.RemoveListener(OnTrackablesChanged);
            }
        }

        #region Public API Methods
        public void StartImageTracking()
        {
            if (currentState == TrackingState.Downloading) return;

            if (librarySource == LibrarySource.DynamicCreation && downloadedTextures.Count == 0)
            {
                StartCoroutine(DownloadAndSetupImages());
            }
            else
            {
                SetupImageTracking();
            }
        }

        public void ResetExperience()
        {
            UpdateStatus("Resetting experience...");

            foreach (var trackedObject in trackedGameObjects.Values)
            {
                Destroy(trackedObject);
            }
            trackedGameObjects.Clear();

            libraryInitialized = false;

            if (trackedImageManager != null)
            {
                trackedImageManager.enabled = false;
            }

            OnTrackingReset?.Invoke();
            SetTrackingState(TrackingState.NotInitialized);
            UpdateStatus("Press 'Track Image' to begin");
        }

        public void SetImageCollection(URLTrackedImageCollection collection)
        {
            urlImageCollection = collection;
            if (currentState != TrackingState.NotInitialized)
            {
                ResetExperience();
                downloadedTextures.Clear();
            }
        }
        #endregion

        #region Image Tracking Setup
        private IEnumerator DownloadAndSetupImages()
        {
            SetTrackingState(TrackingState.Downloading);

            if (urlImageCollection == null || urlImageCollection.uRLTrackedImages.Count == 0)
            {
                UpdateStatus("Error: No images in the collection to download.");
                SetTrackingState(TrackingState.NotInitialized);
                yield break;
            }

            int totalImages = urlImageCollection.uRLTrackedImages.Count;
            int downloadedCount = 0;

            foreach (var imageToTrack in urlImageCollection.uRLTrackedImages)
            {
                UpdateStatus($"Downloading image {downloadedCount + 1}/{totalImages}: {imageToTrack.name}");
                UnityWebRequest request = UnityWebRequestTexture.GetTexture(imageToTrack.url);
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    Texture2D texture = DownloadHandlerTexture.GetContent(request);
                    texture.name = imageToTrack.name;
                    downloadedTextures[imageToTrack.name] = texture;
                    downloadedCount++;
                }
                else
                {
                    UpdateStatus($"Error downloading {imageToTrack.name}: {request.error}");
                }
            }

            if (downloadedCount > 0)
            {
                UpdateStatus($"{downloadedCount}/{totalImages} images downloaded. Setting up tracking...");
                OnAllImagesDownloaded?.Invoke();
                SetupImageTracking();
            }
            else
            {
                UpdateStatus("Error: Failed to download any images.");
                SetTrackingState(TrackingState.NotInitialized);
            }
        }

        private void SetupImageTracking()
        {
            if (trackedImageManager == null)
            {
                UpdateStatus("Error: No AR Tracked Image Manager assigned");
                return;
            }

            trackedImageManager.enabled = false;

            if (librarySource == LibrarySource.InspectorReference)
            {
                SetupInspectorReferenceLibrary();
            }
            else
            {
                SetupDynamicLibrary();
            }

            if (!libraryInitialized)
            {
                SetTrackingState(TrackingState.NotInitialized);
                return;
            }

            trackedImageManager.enabled = true;

            UpdateStatus("Ready! Please scan for images.");

            SetTrackingState(TrackingState.ReadyToScan);
            OnTrackingInitialized?.Invoke();
            OnReadyToScan?.Invoke();
        }

        private void SetupInspectorReferenceLibrary()
        {
            if (referenceImageLibrary == null)
            {
                UpdateStatus("Error: No reference image library assigned in Inspector");
                return;
            }

            // Validate that all reference images have names
            for (int i = 0; i < referenceImageLibrary.count; i++)
            {
                var referenceImage = referenceImageLibrary[i];
                if (string.IsNullOrEmpty(referenceImage.name))
                {
                    UpdateStatus($"Warning: Reference image at index {i} has no name. This may cause tracking issues.");
                }
            }

            trackedImageManager.referenceLibrary = referenceImageLibrary;
            libraryInitialized = true;
            UpdateStatus($"Using inspector reference library with {referenceImageLibrary.count} images");
        }

        private void SetupDynamicLibrary()
        {
            if (runtimeLibrary == null)
            {
                runtimeLibrary = trackedImageManager.CreateRuntimeLibrary() as MutableRuntimeReferenceImageLibrary;
                if (runtimeLibrary == null)
                {
                    UpdateStatus("Error: Mutable runtime library not supported.");
                    return;
                }
            }

            if (downloadedTextures.Count == 0)
            {
                UpdateStatus("Error: No downloaded textures to create a library from.");
                return;
            }

            foreach (var imageToTrack in urlImageCollection.uRLTrackedImages)
            {
                if (downloadedTextures.ContainsKey(imageToTrack.name))
                {
                    Texture2D texture = downloadedTextures[imageToTrack.name];
                    runtimeLibrary.ScheduleAddImageWithValidationJob(
                        texture,
                        imageToTrack.name,
                        imageToTrack.physicalImageSize
                    );
                }
            }

            trackedImageManager.referenceLibrary = runtimeLibrary;
            libraryInitialized = true;
        }
        #endregion

        #region Event Handlers & Processing
        private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> eventArgs)
        {
            if (!libraryInitialized) return;

            foreach (ARTrackedImage trackedImage in eventArgs.added)
            {
                ProcessTrackedImage(trackedImage);
            }

            foreach (ARTrackedImage trackedImage in eventArgs.updated)
            {
                ProcessTrackedImage(trackedImage);
            }
        }

        private async void ProcessTrackedImage(ARTrackedImage trackedImage)
        {
            // Check if referenceImage is null or if the name is null/empty
            Debug.Log("TRACKED IMAGE NAME IS " + trackedImage.referenceImage.name);
            if (trackedImage.referenceImage == null || string.IsNullOrEmpty(trackedImage.referenceImage.name))
            {
                Debug.LogWarning("Tracked image has no valid reference image or name.");
                return;
            }

            string imageName = trackedImage.referenceImage.name;

            if (trackedImage.trackingState == UnityEngine.XR.ARSubsystems.TrackingState.Tracking)
            {
                if (!trackedGameObjects.ContainsKey(imageName))
                {
                    SetTrackingState(TrackingState.Tracking);

                    GameObject rootObject = null;
                    bool isAnchor = false;

                    if (trackingMode == TrackingMode.AnchorBased && anchorManager != null)
                    {
                        Pose anchorPose = new Pose(trackedImage.transform.position, trackedImage.transform.rotation);
                        Result<ARAnchor> result = await anchorManager.TryAddAnchorAsync(anchorPose);

                        if (result.status.IsSuccess())
                        {
                            rootObject = result.value.gameObject;
                            rootObject.name = $"Anchor - {imageName}";
                            isAnchor = true;
                            UpdateStatus($"Anchor created for '{imageName}'");
                        }
                        else
                        {
                            UpdateStatus($"Failed to create anchor for '{imageName}'");
                            // Fall back to transform-based tracking
                            rootObject = new GameObject($"Transform - {imageName}");
                            rootObject.transform.SetPositionAndRotation(trackedImage.transform.position, trackedImage.transform.rotation);
                            isAnchor = false;
                            UpdateStatus($"Transform root created for '{imageName}' (anchor fallback)");
                        }
                    }
                    else // TransformBased or anchor manager is null
                    {
                        rootObject = new GameObject($"Transform - {imageName}");
                        rootObject.transform.SetPositionAndRotation(trackedImage.transform.position, trackedImage.transform.rotation);
                        isAnchor = false;
                        UpdateStatus($"Transform root created for '{imageName}'");
                    }

                    trackedGameObjects[imageName] = rootObject;

                    TrackedImageResult resultPayload = new TrackedImageResult
                    {
                        ImageName = imageName,
                        RootTransform = rootObject.transform,
                        IsRootAnchor = isAnchor
                    };
                    OnImageTracked?.Invoke(resultPayload);

                    //OnResetButtonStateChange?.Invoke(true);
                }
                else
                {
                    // Update existing tracked object position if using transform-based tracking
                    if (trackingMode == TrackingMode.TransformBased && trackedGameObjects.TryGetValue(imageName, out GameObject existingObject))
                    {
                        existingObject.transform.SetPositionAndRotation(trackedImage.transform.position, trackedImage.transform.rotation);
                    }
                }
            }
            else if (trackedImage.trackingState == UnityEngine.XR.ARSubsystems.TrackingState.Limited)
            {
                SetTrackingState(TrackingState.Limited);
            }
            else
            {
                OnTrackingLost?.Invoke();
            }
        }
        #endregion

        private void UpdateStatus(string message)
        {
            //OnStatusUpdate?.Invoke(message);
            Debug.Log($"[Status]: {message}");
        }

        private void SetTrackingState(TrackingState newState)
        {
            if (currentState != newState)
            {
                currentState = newState;
                OnTrackingStateChanged?.Invoke(newState);
            }
        }

        public Transform GetTrackedObjectTransform(string imageName)
        {
            if (trackedGameObjects.TryGetValue(imageName, out GameObject obj))
            {
                return obj.transform;
            }
            return null;
        }
    }
}
