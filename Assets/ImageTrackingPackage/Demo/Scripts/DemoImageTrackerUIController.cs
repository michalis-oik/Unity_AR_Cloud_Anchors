using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR.ARFoundation;
using System;

namespace ImageTrackingPackage
{
    public class DemoImageTrackerUIController : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Button trackButton;
        [SerializeField] private Button resetButton;
        [SerializeField] private TextMeshProUGUI statusText;

        [Header("Tracker Reference")]
        [SerializeField] private ConfigurableImageTracker imageTracker;

        [Header("Plane Visualization")]
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private bool autoManagePlanes = true;

        void Start()
        {
            if (trackButton != null) trackButton.onClick.AddListener(OnTrackButtonClicked);
            if (resetButton != null)
            {
                resetButton.onClick.AddListener(OnResetButtonClicked);
                resetButton.gameObject.SetActive(false);
            }

            if (planeManager == null && autoManagePlanes)
            {
                planeManager = FindFirstObjectByType<ARPlaneManager>();
            }

            if (imageTracker != null)
            {
                // Listen to all relevant events
                imageTracker.OnTrackingStateChanged.AddListener(OnTrackingStateChanged);
                imageTracker.OnImageTracked.AddListener(OnImageTracked);
                imageTracker.OnTrackingReset.AddListener(OnTrackingReset);
                imageTracker.OnTrackingInitialized.AddListener(OnTrackingInitialized);
                imageTracker.OnAllImagesDownloaded.AddListener(OnAllImagesDownloaded);
                imageTracker.OnReadyToScan.AddListener(OnReadyToScan);
                imageTracker.OnTrackingLost.AddListener(OnTrackingLost);
            }

            UpdateStatus("Press 'Track Image' to begin");
        }

        private void OnTrackButtonClicked()
        {
            imageTracker.StartImageTracking();
            if (autoManagePlanes && planeManager != null)
            {
                ShowPlanes();
            }
        }

        private void OnResetButtonClicked()
        {
            imageTracker.ResetExperience();
            if (autoManagePlanes && planeManager != null)
            {
                ShowPlanes();
            }
        }

        private void OnImageTracked(TrackedImageResult result)
        {
            Debug.Log($"UI Controller received tracked image: {result.ImageName}, IsAnchor: {result.IsRootAnchor}");
            UpdateStatus($"Tracked image: {result.ImageName}");
            if (autoManagePlanes && planeManager != null)
            {
                HidePlanes();
            }
        }

        // Event handlers for status updates
        private void OnTrackingStateChanged(ConfigurableImageTracker.TrackingState state)
        {
            switch (state)
            {
                case ConfigurableImageTracker.TrackingState.NotInitialized:
                    UpdateStatus("Press 'Track Image' to begin");
                    SetTrackButtonState(true);
                    SetResetButtonState(false);
                    break;
                case ConfigurableImageTracker.TrackingState.Downloading:
                    UpdateStatus("Downloading images...");
                    SetTrackButtonState(false);
                    SetResetButtonState(false);
                    break;
                case ConfigurableImageTracker.TrackingState.ReadyToScan:
                    UpdateStatus("Ready! Please scan for images.");
                    SetTrackButtonState(true);
                    SetResetButtonState(false);
                    break;
                case ConfigurableImageTracker.TrackingState.Tracking:
                    //UpdateStatus("Tracking image!");
                    SetTrackButtonState(false);
                    SetResetButtonState(true);
                    break;
                case ConfigurableImageTracker.TrackingState.Limited:
                    //UpdateStatus("Tracking limited");
                    SetTrackButtonState(false);
                    SetResetButtonState(true);
                    break;
                case ConfigurableImageTracker.TrackingState.Lost:
                    UpdateStatus("Tracking lost");
                    SetTrackButtonState(false);
                    SetResetButtonState(true);
                    break;
            }
        }

        private void OnTrackingReset()
        {
            UpdateStatus("Experience reset");
            SetTrackButtonState(true);
            SetResetButtonState(false);
        }

        private void OnTrackingInitialized()
        {
            UpdateStatus("Tracking initialized");
            SetTrackButtonState(true);
            SetResetButtonState(false);
        }

        private void OnAllImagesDownloaded()
        {
            UpdateStatus("All images downloaded. Setting up tracking...");
        }

        private void OnReadyToScan()
        {
            UpdateStatus("Ready! Please scan for images.");
        }

        private void OnTrackingLost()
        {
            UpdateStatus("Tracking lost");
            SetTrackButtonState(false);
            SetResetButtonState(true);
        }

        private void UpdateStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
            Debug.Log($"[UI Status]: {message}");
        }

        private void SetTrackButtonState(bool interactable)
        {
            if (trackButton != null)
            {
                trackButton.interactable = interactable;
            }
        }

        private void SetResetButtonState(bool visible)
        {
            if (resetButton != null)
            {
                resetButton.gameObject.SetActive(visible);
            }
        }

        #region Plane Management
        private void SetAllPlanesActive(bool isVisible)
        {
            if (planeManager == null) return;
            foreach (ARPlane plane in planeManager.trackables)
            {
                plane.gameObject.SetActive(isVisible);
            }
        }

        public void ShowPlanes()
        {
            SetAllPlanesActive(true);
        }

        public void HidePlanes()
        {
            SetAllPlanesActive(false);
        }
        #endregion

        void OnDestroy()
        {
            if (imageTracker != null)
            {
                imageTracker.OnTrackingStateChanged.RemoveListener(OnTrackingStateChanged);
                imageTracker.OnImageTracked.RemoveListener(OnImageTracked);
                imageTracker.OnTrackingReset.RemoveListener(OnTrackingReset);
                imageTracker.OnTrackingInitialized.RemoveListener(OnTrackingInitialized);
                imageTracker.OnAllImagesDownloaded.RemoveListener(OnAllImagesDownloaded);
                imageTracker.OnReadyToScan.RemoveListener(OnReadyToScan);
                imageTracker.OnTrackingLost.RemoveListener(OnTrackingLost);
            }

            if (trackButton != null) trackButton.onClick.RemoveAllListeners();
            if (resetButton != null) resetButton.onClick.RemoveAllListeners();
        }
    }
}
