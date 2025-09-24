using ImageTrackingPackage;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARCore;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Threading.Tasks;
using System;
using TMPro;
using System.Collections.Generic;
using System.Collections;

public class TestCloudAnchors : MonoBehaviour
{
    [SerializeField] private ARAnchorManager aRAnchorManager;
    [SerializeField] private ConfigurableImageTracker tracker;
    [SerializeField] private GameObject cubePrefab;
    [SerializeField] private GameObject spherePrefab;
    [SerializeField] private Material yellowMaterial;
    [SerializeField] private Material blueMaterial;
    [SerializeField] private Material greenMaterial;
    [SerializeField] private Material redMaterial;
    [SerializeField] private Button EraseButton;

    [Tooltip("The button to trigger loading the saved anchor.")]
    [SerializeField] private Button LoadButton;

    [Header("Client Mode UI")]
    [SerializeField] private TMP_InputField guidInputField;
    [SerializeField] private Button loadWithGuidButton;
    [SerializeField] private Button pasteGuidButton;
    [SerializeField] private Button copyGuidButton;
    [SerializeField] private TextMeshProUGUI guidDisplayText;
    [SerializeField] private TMP_Dropdown imageSelectionDropdown;

    // Track anchors per image
    private Dictionary<string, ARAnchor> anchorsByImage = new Dictionary<string, ARAnchor>();

    // Track spawned objects per image
    private Dictionary<string, (GameObject cube, GameObject sphere)> objectsByImage = new Dictionary<string, (GameObject, GameObject)>();

    // Track saved GUIDs per image
    private Dictionary<string, SerializableGuid> savedGuidsByImage = new Dictionary<string, SerializableGuid>();

    // Track loading states per image
    private Dictionary<string, bool> isSavingByImage = new Dictionary<string, bool>();

    // Track loaded anchors (for client mode)
    private Dictionary<string, ARAnchor> loadedAnchorsByImage = new Dictionary<string, ARAnchor>();
    private Dictionary<string, GameObject> loadedAnchorGOsByImage = new Dictionary<string, GameObject>();

    private string lastSavedImageName = string.Empty;

    void Start()
    {
        // Buttons should be disabled until an anchor is successfully saved.
        if (EraseButton != null) EraseButton.interactable = false;
        if (LoadButton != null) LoadButton.interactable = false;

        // Setup client mode UI
        if (loadWithGuidButton != null)
        {
            loadWithGuidButton.onClick.AddListener(LoadAnchorWithInputGuid);
            loadWithGuidButton.interactable = false;
        }

        if (pasteGuidButton != null)
        {
            pasteGuidButton.onClick.AddListener(PasteGuidFromClipboard);
            pasteGuidButton.interactable = Application.platform == RuntimePlatform.Android ||
                                         Application.platform == RuntimePlatform.IPhonePlayer;
        }

        if (copyGuidButton != null)
        {
            copyGuidButton.onClick.AddListener(CopyCurrentGuidToClipboard);
            copyGuidButton.interactable = false;
        }

        if (guidInputField != null)
        {
            guidInputField.onValueChanged.AddListener(OnGuidInputChanged);
        }

        if (guidDisplayText != null)
        {
            guidDisplayText.text = "No GUID saved";
        }

        // Setup image selection dropdown
        if (imageSelectionDropdown != null)
        {
            imageSelectionDropdown.ClearOptions();
            imageSelectionDropdown.interactable = false;
            imageSelectionDropdown.onValueChanged.AddListener(OnImageSelected);
        }
    }

    void OnEnable()
    {
        tracker.OnImageTracked.AddListener(TrackedImage);

        if (EraseButton != null) EraseButton.onClick.AddListener(EraseAllSavedAnchors);
        if (LoadButton != null) LoadButton.onClick.AddListener(LoadAllSavedAnchors);
    }

    void OnDisable()
    {
        tracker.OnImageTracked.RemoveListener(TrackedImage);

        if (EraseButton != null) EraseButton.onClick.RemoveListener(EraseAllSavedAnchors);
        if (LoadButton != null) LoadButton.onClick.RemoveListener(LoadAllSavedAnchors);
    }

    private void TrackedImage(TrackedImageResult result)
    {
        string imageName = result.ImageName;

        Debug.Log($"Tracked image: {imageName}, IsAnchor: {result.IsRootAnchor}");

        // If we already spawned objects for this image and they exist, ignore
        if (objectsByImage.ContainsKey(imageName) && objectsByImage[imageName].cube != null)
        {
            return;
        }

        Transform anchorTransform = result.RootTransform;

        if (anchorTransform != null)
        {
            GameObject cube = Instantiate(cubePrefab, anchorTransform);
            GameObject sphere = Instantiate(spherePrefab, anchorTransform);

            objectsByImage[imageName] = (cube, sphere);

            // Set initial material to yellow (waiting for quality check)
            cube.GetComponent<MeshRenderer>().material = yellowMaterial;
            sphere.GetComponent<MeshRenderer>().material = yellowMaterial;
        }

        ARAnchor anchor = result.RootTransform.GetComponent<ARAnchor>();
        if (anchor != null)
        {
            anchorsByImage[imageName] = anchor;

            // Initialize saving state
            if (!isSavingByImage.ContainsKey(imageName))
            {
                isSavingByImage[imageName] = false;
            }

            // Update dropdown
            UpdateImageSelectionDropdown();

            CheckQualityAndSaveAnchor(aRAnchorManager, anchor, imageName);
        }
    }

    void CheckQualityAndSaveAnchor(ARAnchorManager manager, ARAnchor anchor, string imageName)
    {
        // Skip if already saved or currently saving
        if (savedGuidsByImage.ContainsKey(imageName) || isSavingByImage[imageName])
            return;

        if (manager.subsystem is ARCoreAnchorSubsystem arCoreAnchorSubsystem)
        {
            var quality = ArFeatureMapQuality.AR_FEATURE_MAP_QUALITY_SUFFICIENT;

            XRResultStatus resultStatus = arCoreAnchorSubsystem.EstimateFeatureMapQualityForHosting(anchor.trackableId, ref quality);

            if (!resultStatus.IsSuccess())
            {
                Debug.Log($"EstimateFeatureMapQualityForHosting failed for {imageName}: {resultStatus}");
                return;
            }

            if (quality == ArFeatureMapQuality.AR_FEATURE_MAP_QUALITY_INSUFFICIENT)
            {
                Debug.Log($"Feature map quality is insufficient for {imageName}");
                if (objectsByImage.ContainsKey(imageName))
                {
                    objectsByImage[imageName].cube.GetComponent<MeshRenderer>().material = yellowMaterial;
                    objectsByImage[imageName].sphere.GetComponent<MeshRenderer>().material = yellowMaterial;
                }
                return;
            }
        }

        Debug.Log($"Start Saving Anchor for {imageName}!");
        isSavingByImage[imageName] = true;
        TrySaveAnchorWithLifespanAsync(manager, anchor, imageName);
    }

    private async void TrySaveAnchorWithLifespanAsync(ARAnchorManager manager, ARAnchor anchor, string imageName)
    {
        Debug.Log($"Entered try func for {imageName}");

        if (objectsByImage.ContainsKey(imageName))
        {
            objectsByImage[imageName].cube.GetComponent<MeshRenderer>().material = blueMaterial;
            objectsByImage[imageName].sphere.GetComponent<MeshRenderer>().material = blueMaterial;
        }

        if (manager.subsystem is ARCoreAnchorSubsystem arCoreAnchorSubsystem)
        {
            try
            {
                var result = await arCoreAnchorSubsystem.TrySaveAnchorWithLifespanAsync(anchor.trackableId, 180);

                if (result.status.IsError())
                {
                    Debug.LogError($"Failed to save anchor for {imageName}: {result.status}");
                    anchorsByImage.Remove(imageName);
                    isSavingByImage[imageName] = false;

                    if (objectsByImage.ContainsKey(imageName))
                    {
                        objectsByImage[imageName].cube.GetComponent<MeshRenderer>().material = redMaterial;
                        objectsByImage[imageName].sphere.GetComponent<MeshRenderer>().material = redMaterial;
                    }
                    return;
                }

                SerializableGuid savedguid = result.value;
                savedGuidsByImage[imageName] = savedguid;
                lastSavedImageName = imageName;

                Debug.Log($"Anchor saved for {imageName} with guid: {savedguid}");

                // Change materials to indicate success
                if (objectsByImage.ContainsKey(imageName))
                {
                    objectsByImage[imageName].cube.GetComponent<MeshRenderer>().material = greenMaterial;
                    objectsByImage[imageName].sphere.GetComponent<MeshRenderer>().material = greenMaterial;
                }

                // Update UI
                UpdateUIAfterSave();
                DisplayGuidForSharing(savedguid, imageName);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Exception saving anchor for {imageName}: {ex.Message}");
                isSavingByImage[imageName] = false;
            }
            finally
            {
                isSavingByImage[imageName] = false;
            }
        }
    }

    private void UpdateUIAfterSave()
    {
        if (EraseButton != null) EraseButton.interactable = savedGuidsByImage.Count > 0;
        if (LoadButton != null) LoadButton.interactable = savedGuidsByImage.Count > 0;
        if (copyGuidButton != null) copyGuidButton.interactable = savedGuidsByImage.Count > 0;
    }

    private void DisplayGuidForSharing(SerializableGuid guid, string imageName)
    {
        string guidString = guid.ToString();
        Debug.Log($"ANCHOR GUID FOR {imageName}: {guidString}");

        if (guidDisplayText != null)
        {
            guidDisplayText.text = $"Last Saved: {imageName}\nGUID: {guidString}";
        }

        CopyToClipboard(guidString);
    }

    public void CopyCurrentGuidToClipboard()
    {
        if (savedGuidsByImage.Count == 0)
        {
            Debug.LogWarning("No GUID to copy - no anchors have been saved yet.");
            return;
        }

        // Copy the last saved GUID, or allow selection via dropdown
        string imageNameToCopy = lastSavedImageName;
        if (imageSelectionDropdown != null && imageSelectionDropdown.options.Count > 0)
        {
            imageNameToCopy = imageSelectionDropdown.options[imageSelectionDropdown.value].text;
        }

        if (savedGuidsByImage.ContainsKey(imageNameToCopy))
        {
            string guidString = savedGuidsByImage[imageNameToCopy].ToString();
            CopyToClipboard(guidString);
            ShowCopyConfirmation();
            Debug.Log($"GUID for {imageNameToCopy} copied to clipboard: {guidString}");
        }
    }

    private void ShowCopyConfirmation()
    {
        if (copyGuidButton != null && copyGuidButton.GetComponentInChildren<Text>() != null)
        {
            Text buttonText = copyGuidButton.GetComponentInChildren<Text>();
            string originalText = buttonText.text;
            buttonText.text = "Copied!";

            StartCoroutine(ResetButtonTextAfterDelay(buttonText, originalText, 2f));
        }
    }

    private System.Collections.IEnumerator ResetButtonTextAfterDelay(Text textElement, string originalText, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (textElement != null)
        {
            textElement.text = originalText;
        }
    }

    private void CopyToClipboard(string text)
    {
        GUIUtility.systemCopyBuffer = text;
    }

    private void UpdateImageSelectionDropdown()
    {
        if (imageSelectionDropdown == null) return;

        List<string> imageNames = new List<string>(savedGuidsByImage.Keys);
        if (imageNames.Count > 0)
        {
            imageSelectionDropdown.ClearOptions();
            imageSelectionDropdown.AddOptions(imageNames);
            imageSelectionDropdown.interactable = true;
        }
        else
        {
            imageSelectionDropdown.ClearOptions();
            imageSelectionDropdown.interactable = false;
        }
    }

    private void OnImageSelected(int index)
    {
        if (imageSelectionDropdown == null || imageSelectionDropdown.options.Count <= index) return;

        string selectedImageName = imageSelectionDropdown.options[index].text;
        if (savedGuidsByImage.ContainsKey(selectedImageName))
        {
            DisplayGuidForSharing(savedGuidsByImage[selectedImageName], selectedImageName);
        }
    }

    public async void LoadAllSavedAnchors()
    {
        if (savedGuidsByImage.Count == 0)
        {
            Debug.LogWarning("No anchors have been saved yet. Cannot load.");
            return;
        }

        foreach (var kvp in savedGuidsByImage)
        {
            await LoadAnchorByGuid(kvp.Value, kvp.Key);
        }
    }

    public async void LoadAnchorWithInputGuid()
    {
        if (guidInputField == null || string.IsNullOrEmpty(guidInputField.text))
        {
            Debug.LogError("No GUID input provided.");
            return;
        }

        string guidString = guidInputField.text.Trim();
        string imageName = "Manual Input"; // Default name for manually loaded anchors

        try
        {
            SerializableGuid guidToLoad = ParseGuidFromString(guidString);
            await LoadAnchorByGuid(guidToLoad, imageName);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to parse GUID: {e.Message}");
        }
    }

    private SerializableGuid ParseGuidFromString(string guidString)
    {
        Debug.Log($"Attempting to parse GUID: {guidString}");

        guidString = guidString.Replace("{", "").Replace("}", "").Trim();

        try
        {
            if (guidString.Contains("-") && guidString.Length == 33)
            {
                string[] parts = guidString.Split('-');
                if (parts.Length == 2 && parts[0].Length == 16 && parts[1].Length == 16)
                {
                    Debug.Log("Detected ARCore cloud anchor ID format");

                    ulong guidLow = ulong.Parse(parts[0], System.Globalization.NumberStyles.HexNumber);
                    ulong guidHigh = ulong.Parse(parts[1], System.Globalization.NumberStyles.HexNumber);

                    Debug.Log($"Parsed as guidLow: {guidLow:X16}, guidHigh: {guidHigh:X16}");

                    return new SerializableGuid(guidLow, guidHigh);
                }
            }

            try
            {
                Guid standardGuid = new Guid(guidString);
                return new SerializableGuid(standardGuid);
            }
            catch (Exception fallbackEx)
            {
                Debug.LogError($"Standard GUID parsing also failed: {fallbackEx}");
                throw new Exception($"Invalid GUID format: {guidString}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"GUID parsing failed: {ex}");
            throw new Exception($"Invalid GUID format: {guidString}");
        }
    }

    private async Task LoadAnchorByGuid(SerializableGuid guid, string imageName)
    {
        Debug.Log($"Attempting to load anchor for {imageName} with GUID: {guid}");

        // Clear existing loaded anchor for this image if it exists
        if (loadedAnchorGOsByImage.ContainsKey(imageName))
        {
            Destroy(loadedAnchorGOsByImage[imageName]);
            loadedAnchorGOsByImage.Remove(imageName);
            loadedAnchorsByImage.Remove(imageName);
        }

        var result = await aRAnchorManager.TryLoadAnchorAsync(guid);

        if (result.status.IsSuccess())
        {
            Debug.Log($"Anchor loaded successfully for {imageName}!");
            ARAnchor loadedAnchor = result.value;

            GameObject loadedCube = Instantiate(cubePrefab, loadedAnchor.transform);
            GameObject loadedSphere = Instantiate(spherePrefab, loadedAnchor.transform);

            loadedCube.GetComponent<MeshRenderer>().material = blueMaterial;
            loadedSphere.GetComponent<MeshRenderer>().material = blueMaterial;

            GameObject anchorGO = new GameObject($"LoadedAnchor_{imageName}");
            loadedAnchor.transform.SetParent(anchorGO.transform);
            loadedCube.transform.SetParent(anchorGO.transform);
            loadedSphere.transform.SetParent(anchorGO.transform);

            loadedAnchorGOsByImage[imageName] = anchorGO;
            loadedAnchorsByImage[imageName] = loadedAnchor;
        }
        else
        {
            Debug.LogError($"Failed to load anchor for {imageName}: {result.status}");
        }
    }

    private void OnGuidInputChanged(string input)
    {
        if (loadWithGuidButton != null)
        {
            loadWithGuidButton.interactable = !string.IsNullOrEmpty(input.Trim());
        }
    }

    private void PasteGuidFromClipboard()
    {
        if (guidInputField != null)
        {
            guidInputField.text = GUIUtility.systemCopyBuffer;
        }
    }

    public async void EraseAllSavedAnchors()
    {
        if (savedGuidsByImage.Count == 0)
        {
            Debug.LogWarning("No anchors have been saved. Nothing to erase.");
            return;
        }

        List<string> imagesToRemove = new List<string>();

        foreach (var kvp in savedGuidsByImage)
        {
            string imageName = kvp.Key;
            SerializableGuid guid = kvp.Value;

            Debug.Log($"Attempting to erase anchor for {imageName} with GUID: {guid}");

            var resultStatus = await aRAnchorManager.TryEraseAnchorAsync(guid);

            if (resultStatus.IsSuccess())
            {
                Debug.Log($"Anchor erased successfully for {imageName}!");
                imagesToRemove.Add(imageName);
            }
            else
            {
                Debug.LogError($"Failed to erase anchor for {imageName}: {resultStatus}");
            }
        }

        // Remove erased anchors from dictionaries
        foreach (string imageName in imagesToRemove)
        {
            savedGuidsByImage.Remove(imageName);

            // Change materials back to yellow (not saved)
            if (objectsByImage.ContainsKey(imageName))
            {
                objectsByImage[imageName].cube.GetComponent<MeshRenderer>().material = yellowMaterial;
                objectsByImage[imageName].sphere.GetComponent<MeshRenderer>().material = yellowMaterial;
            }
        }

        UpdateUIAfterErase();
        UpdateImageSelectionDropdown();
    }

    private void UpdateUIAfterErase()
    {
        bool hasSavedAnchors = savedGuidsByImage.Count > 0;

        if (EraseButton != null) EraseButton.interactable = hasSavedAnchors;
        if (LoadButton != null) LoadButton.interactable = hasSavedAnchors;
        if (copyGuidButton != null) copyGuidButton.interactable = hasSavedAnchors;

        if (guidDisplayText != null)
        {
            guidDisplayText.text = hasSavedAnchors ?
                $"{savedGuidsByImage.Count} anchor(s) saved" :
                "No GUIDs saved";
        }
    }

    // Helper method to get all saved GUIDs as a formatted string
    public string GetAllSavedGuids()
    {
        if (savedGuidsByImage.Count == 0) return "No anchors saved";

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("Saved Anchors:");
        foreach (var kvp in savedGuidsByImage)
        {
            sb.AppendLine($"{kvp.Key}: {kvp.Value}");
        }
        return sb.ToString();
    }
    
    void Update()
    {
        foreach (var kvp in anchorsByImage)
        {
            string imageName = kvp.Key;
            ARAnchor anchor = kvp.Value;

            // Skip if already saved
            if (savedGuidsByImage.ContainsKey(imageName)) continue;

            // Try saving this anchor
            CheckQualityAndSaveAnchor(aRAnchorManager, anchor, imageName);
        }
    }
}