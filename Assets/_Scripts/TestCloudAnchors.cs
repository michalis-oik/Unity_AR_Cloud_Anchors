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
using System.Linq;

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

    // No longer need lastSavedImageName, as we handle all GUIDs now.
    // private string lastSavedImageName = string.Empty;

    void Start()
    {
        // Buttons should be disabled until an anchor is successfully saved.
        if (EraseButton != null) EraseButton.interactable = false;
        if (LoadButton != null) LoadButton.interactable = false;

        // Setup client mode UI
        if (loadWithGuidButton != null)
        {
            // MODIFIED: Point to the new function that handles multiple GUIDs
            loadWithGuidButton.onClick.AddListener(LoadAnchorsFromGuidString);
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
            guidDisplayText.text = "No GUIDs saved";
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

            cube.GetComponent<MeshRenderer>().material = yellowMaterial;
            sphere.GetComponent<MeshRenderer>().material = yellowMaterial;
        }

        ARAnchor anchor = result.RootTransform.GetComponent<ARAnchor>();
        if (anchor != null)
        {
            anchorsByImage[imageName] = anchor;

            if (!isSavingByImage.ContainsKey(imageName))
            {
                isSavingByImage[imageName] = false;
            }

            UpdateImageSelectionDropdown();
            CheckQualityAndSaveAnchor(aRAnchorManager, anchor, imageName);
        }
    }

    void CheckQualityAndSaveAnchor(ARAnchorManager manager, ARAnchor anchor, string imageName)
    {
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

                Debug.Log($"Anchor saved for {imageName} with guid: {savedguid}");

                if (objectsByImage.ContainsKey(imageName))
                {
                    objectsByImage[imageName].cube.GetComponent<MeshRenderer>().material = greenMaterial;
                    objectsByImage[imageName].sphere.GetComponent<MeshRenderer>().material = greenMaterial;
                }

                UpdateUIAfterSave();
                UpdateSharedGuidDisplay();
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

    // MODIFIED: This function now updates the display with ALL saved GUIDs.
    private void UpdateSharedGuidDisplay()
    {
        if (guidDisplayText == null) return;

        if (savedGuidsByImage.Count == 0)
        {
            guidDisplayText.text = "No GUIDs saved";
            return;
        }

        string allGuidsString = GetAllGuidsAsString();
        guidDisplayText.text = $"Saved GUIDs ({savedGuidsByImage.Count}):\n{allGuidsString}";

        // Auto-copy the full list to the clipboard every time a new anchor is added.
        CopyToClipboard(allGuidsString);
    }

    // MODIFIED: This function now copies ALL saved GUIDs.
    public void CopyCurrentGuidToClipboard()
    {
        if (savedGuidsByImage.Count == 0)
        {
            Debug.LogWarning("No GUIDs to copy - no anchors have been saved yet.");
            return;
        }

        string allGuidsString = GetAllGuidsAsString();
        CopyToClipboard(allGuidsString);
        ShowCopyConfirmation();
        Debug.Log($"{savedGuidsByImage.Count} GUID(s) copied to clipboard.");
    }

    // NEW HELPER: Creates a string of all GUIDs separated by newlines.
    private string GetAllGuidsAsString()
    {
        // We use .Select to get all the Guid values from the dictionary,
        // convert them to strings, and then join them with a newline character.
        return string.Join("\n", savedGuidsByImage.Values.Select(guid => guid.ToString()));
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
        // This function's utility is reduced now, but we can keep it to log the selected GUID.
        string selectedImageName = imageSelectionDropdown.options[index].text;
        if (savedGuidsByImage.ContainsKey(selectedImageName))
        {
            Debug.Log($"GUID for selected image '{selectedImageName}': {savedGuidsByImage[selectedImageName]}");
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

    // NEW: This is the primary function for the client side.
    // It reads the input field, splits the text into multiple GUIDs, and loads each one.
    public async void LoadAnchorsFromGuidString()
    {
        if (guidInputField == null || string.IsNullOrEmpty(guidInputField.text))
        {
            Debug.LogError("GUID input field is empty.");
            return;
        }

        string allGuidsText = guidInputField.text;

        // Split the string by newline characters. This handles cases where users might paste
        // extra empty lines by using RemoveEmptyEntries.
        string[] guidStrings = allGuidsText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        if (guidStrings.Length == 0)
        {
            Debug.LogWarning("No valid GUIDs found in the input string after splitting.");
            return;
        }

        // ---- THIS IS OLD VERSION THAT WAITS ONE BY ONE TO LOAD EACH GUID -----
        // Debug.Log($"Found {guidStrings.Length} GUID(s) to load. Starting process...");

        // int successfulLoads = 0;
        // for (int i = 0; i < guidStrings.Length; i++)
        // {
        //     string guidString = guidStrings[i].Trim(); // Trim whitespace
        //     try
        //     {
        //         SerializableGuid guidToLoad = ParseGuidFromString(guidString);
        //         // We'll give a generic name to anchors loaded this way.
        //         string imageName = $"LoadedFromInput_{i + 1}";
        //         await LoadAnchorByGuid(guidToLoad, imageName);
        //         successfulLoads++;
        //     }
        //     catch (Exception e)
        //     {
        //         Debug.LogError($"Failed to parse or load GUID '{guidString}': {e.Message}");
        //     }
        // }

        // Debug.Log($"Finished loading process. Successfully loaded {successfulLoads} of {guidStrings.Length} anchors.");

        Debug.Log($"Found {guidStrings.Length} GUID(s) to load. Starting all processes concurrently...");

        // 1. Start all the loading tasks without waiting for each one
        List<Task> loadingTasks = new List<Task>();
        for (int i = 0; i < guidStrings.Length; i++)
        {
            try
            {
                string guidString = guidStrings[i].Trim();
                SerializableGuid guidToLoad = ParseGuidFromString(guidString);
                string imageName = $"LoadedFromInput_{i + 1}";

                // Start the task but DON'T await it yet.
                // It will run in the background. Add the task to a list.
                loadingTasks.Add(LoadAnchorByGuid(guidToLoad, imageName));
            }
            catch(Exception e)
            {
                Debug.LogError($"Failed to parse or start loading GUID '{guidStrings[i]}': {e.Message}");
            }
        }

        // 2. Now, wait for ALL of the started tasks to finish
        await Task.WhenAll(loadingTasks);

        // This line will only be reached after every single anchor has finished loading.
        // Note: Counting successful loads is more complex here, as tasks can fail.
        Debug.Log($"Finished loading process. All {loadingTasks.Count} anchor-loading tasks have completed.");
    }

    // This function is no longer directly called by a button, but is used by LoadAnchorsFromGuidString.
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

            // Group the loaded objects under a single parent for easier scene management
            GameObject anchorGO = new GameObject($"LoadedAnchor_{imageName}");
            // Set the loaded anchor as a child of our new GameObject
            loadedAnchor.transform.SetParent(anchorGO.transform, true);

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

        foreach (string imageName in imagesToRemove)
        {
            savedGuidsByImage.Remove(imageName);
            if (objectsByImage.ContainsKey(imageName))
            {
                objectsByImage[imageName].cube.GetComponent<MeshRenderer>().material = yellowMaterial;
                objectsByImage[imageName].sphere.GetComponent<MeshRenderer>().material = yellowMaterial;
            }
        }

        UpdateUIAfterErase();
        UpdateImageSelectionDropdown();
    }

    // MODIFIED: Simplified the logic after erasing.
    private void UpdateUIAfterErase()
    {
        bool hasSavedAnchors = savedGuidsByImage.Count > 0;
        if (EraseButton != null) EraseButton.interactable = hasSavedAnchors;
        if (LoadButton != null) LoadButton.interactable = hasSavedAnchors;
        if (copyGuidButton != null) copyGuidButton.interactable = hasSavedAnchors;

        // Update the display text to reflect the erased state.
        UpdateSharedGuidDisplay();
    }

    void Update()
    {
        foreach (var kvp in anchorsByImage.ToList()) // Use ToList() to avoid collection modification issues
        {
            string imageName = kvp.Key;
            ARAnchor anchor = kvp.Value;

            if (anchor == null)
            {
                anchorsByImage.Remove(imageName);
                continue;
            }

            if (savedGuidsByImage.ContainsKey(imageName)) continue;

            CheckQualityAndSaveAnchor(aRAnchorManager, anchor, imageName);
        }
    }
}