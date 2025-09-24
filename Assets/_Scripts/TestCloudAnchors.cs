using ImageTrackingPackage;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARCore;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Threading.Tasks;
using System;
using TMPro;

public class TestCloudAnchors : MonoBehaviour
{
    [SerializeField] private ARAnchorManager aRAnchorManager;
    [SerializeField] private ConfigurableImageTracker tracker;
    [SerializeField] private GameObject cubePrefab;
    [SerializeField] private GameObject spherePrefab;
    [SerializeField] private Material yellowMaterial;
    [SerializeField] private Material blueMaterial;
    [SerializeField] private Material greenMaterial;
    [SerializeField] private Button EraseButton;

    [Tooltip("The button to trigger loading the saved anchor.")]
    [SerializeField] private Button LoadButton;
    
    [Header("Client Mode UI")]
    [SerializeField] private TMP_InputField guidInputField;
    [SerializeField] private Button loadWithGuidButton;
    [SerializeField] private Button pasteGuidButton;
    [SerializeField] private Button copyGuidButton; // NEW: Button to copy GUID
    [SerializeField] private TextMeshProUGUI guidDisplayText; // NEW: Text element to display current GUID
    
    private GameObject originalAnchorGO;
    private ARAnchor _anchorToHost;
    private SerializableGuid savedguid;
    private GameObject cube;
    private GameObject sphere;
    private bool isAnchorSaved = false;
    
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
        
        // NEW: Setup copy GUID button
        if (copyGuidButton != null) 
        {
            copyGuidButton.onClick.AddListener(CopyCurrentGuidToClipboard);
            copyGuidButton.interactable = false; // Disabled until we have a GUID
        }
        
        if (guidInputField != null)
        {
            guidInputField.onValueChanged.AddListener(OnGuidInputChanged);
        }
        
        // NEW: Initialize GUID display text
        if (guidDisplayText != null)
        {
            guidDisplayText.text = "No GUID saved";
        }
    }

    void OnEnable()
    {
        tracker.OnImageTracked.AddListener(TrackedImage);

        if (EraseButton != null) EraseButton.onClick.AddListener(EraseSavedAnchor);
        if (LoadButton != null) LoadButton.onClick.AddListener(LoadSavedAnchor);
    }

    void OnDisable()
    {
        tracker.OnImageTracked.RemoveListener(TrackedImage);

        if (EraseButton != null) EraseButton.onClick.RemoveListener(EraseSavedAnchor);
        if (LoadButton != null) LoadButton.onClick.RemoveListener(LoadSavedAnchor);
    }

    private void TrackedImage(TrackedImageResult result)
    {
        if (_anchorToHost != null || isAnchorSaved) return;

        Debug.Log($"Tracked image: {result.ImageName}, IsAnchor: {result.IsRootAnchor}");
        Transform anchorTransform = result.RootTransform;

        if (anchorTransform != null)
        {
            cube = Instantiate(cubePrefab, anchorTransform);
            sphere = Instantiate(spherePrefab, anchorTransform);
        }

        originalAnchorGO = result.RootTransform.gameObject;
        ARAnchor aRAnchor = result.RootTransform.gameObject.GetComponent<ARAnchor>();
        _anchorToHost = aRAnchor;

        CheckQualityAndSaveAnchor(aRAnchorManager, _anchorToHost);
    }

    void CheckQualityAndSaveAnchor(ARAnchorManager manager, ARAnchor anchor)
    {
        if (manager.subsystem is ARCoreAnchorSubsystem arCoreAnchorSubsystem)
        {
            var quality = ArFeatureMapQuality.AR_FEATURE_MAP_QUALITY_SUFFICIENT;

            XRResultStatus resultStatus = arCoreAnchorSubsystem.EstimateFeatureMapQualityForHosting(anchor.trackableId, ref quality);

            if (!resultStatus.IsSuccess())
            {
                Debug.Log("EstimateFeatureMapQualityForHosting failed: " + resultStatus);
                return;
            }

            if (quality == ArFeatureMapQuality.AR_FEATURE_MAP_QUALITY_INSUFFICIENT)
            {
                Debug.Log("Feature map quality is insufficient");
                cube.GetComponent<MeshRenderer>().material = yellowMaterial;
                return;
            }
        }

        Debug.Log("Start Saving Anchor!");
        TrySaveAnchorWithLifespanAsync(manager, anchor);
    }

    private async void TrySaveAnchorWithLifespanAsync(ARAnchorManager manager, ARAnchor anchor)
    {
        Debug.Log("entered try func");
        cube.GetComponent<MeshRenderer>().material = blueMaterial;
        if (manager.subsystem != null)
        {
            Debug.Log("Subsystem type is: " + manager.subsystem.GetType().FullName);
        }
        else
        {
            Debug.LogError("Anchor manager subsystem is NULL!");
            return;
        }
        if (manager.subsystem is ARCoreAnchorSubsystem arCoreAnchorSubsystem)
        {
            Debug.Log("Saving Anchor!");
            var result = await arCoreAnchorSubsystem.TrySaveAnchorWithLifespanAsync(anchor.trackableId, 180);

            if (result.status.IsError())
            {
                Debug.LogError($"Failed to save anchor: {result.status}");
                _anchorToHost = null;
                return;
            }

            savedguid = result.value;
            Debug.Log($"Anchor saved with guid: {savedguid}");
            
            // Display the GUID for sharing
            DisplayGuidForSharing(savedguid);
            
            isAnchorSaved = true;
            cube.GetComponent<MeshRenderer>().material = greenMaterial;
            sphere.GetComponent<MeshRenderer>().material = greenMaterial;

            _anchorToHost = null;

            if (EraseButton != null) EraseButton.interactable = true;
            if (LoadButton != null) LoadButton.interactable = true;
        }
    }

    // --- UPDATED: Display GUID for sharing ---
    private void DisplayGuidForSharing(SerializableGuid guid)
    {
        string guidString = guid.ToString();
        Debug.Log($"ANCHOR GUID FOR SHARING: {guidString}");
        
        // Update the GUID display text
        if (guidDisplayText != null)
        {
            guidDisplayText.text = $"GUID: {guidString}";
        }
        
        // Enable the copy button
        if (copyGuidButton != null)
        {
            copyGuidButton.interactable = true;
        }
        
        // Also copy to clipboard automatically
        CopyToClipboard(guidString);
    }
    
    // --- NEW FUNCTION: Copy current GUID to clipboard ---
    public void CopyCurrentGuidToClipboard()
    {
        if (!isAnchorSaved)
        {
            Debug.LogWarning("No GUID to copy - no anchor has been saved yet.");
            return;
        }
        
        string guidString = savedguid.ToString();
        CopyToClipboard(guidString);
        
        // Optional: Show some feedback that it was copied
        Debug.Log($"GUID copied to clipboard: {guidString}");
        
        // You could also show a temporary UI message
        ShowCopyConfirmation();
    }
    
    // --- NEW FUNCTION: Show copy confirmation ---
    private void ShowCopyConfirmation()
    {
        // You can implement a temporary UI message here
        // For example, change the button text temporarily
        if (copyGuidButton != null && copyGuidButton.GetComponentInChildren<Text>() != null)
        {
            Text buttonText = copyGuidButton.GetComponentInChildren<Text>();
            string originalText = buttonText.text;
            buttonText.text = "Copied!";
            
            // Reset after 2 seconds
            StartCoroutine(ResetButtonTextAfterDelay(buttonText, originalText, 2f));
        }
    }
    
    // --- COROUTINE: Reset button text after delay ---
    private System.Collections.IEnumerator ResetButtonTextAfterDelay(Text textElement, string originalText, float delay)
    {
        yield return new WaitForSeconds(delay);
        textElement.text = originalText;
    }
    
    private void CopyToClipboard(string text)
    {
        GUIUtility.systemCopyBuffer = text;
    }

    void Update()
    {
        if (_anchorToHost != null && !isAnchorSaved)
        {
            CheckQualityAndSaveAnchor(aRAnchorManager, _anchorToHost);
        }
    }

    public async void LoadSavedAnchor()
    {
        if (!isAnchorSaved)
        {
            Debug.LogWarning("No anchor has been saved yet. Cannot load.");
            return;
        }

        await LoadAnchorByGuid(savedguid);
    }
    
    public async void LoadAnchorWithInputGuid()
    {
        if (guidInputField == null || string.IsNullOrEmpty(guidInputField.text))
        {
            Debug.LogError("No GUID input provided.");
            return;
        }
        
        string guidString = guidInputField.text.Trim();
        
        try
        {
            SerializableGuid guidToLoad = ParseGuidFromString(guidString);
            await LoadAnchorByGuid(guidToLoad);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to parse GUID: {e.Message}");
        }
    }

    private SerializableGuid ParseGuidFromString(string guidString)
    {
        Debug.Log($"Attempting to parse GUID: {guidString}");

        // Remove any braces if present
        guidString = guidString.Replace("{", "").Replace("}", "").Trim();

        try
        {
            // For ARCore cloud anchors in format: "C488EDA8A5C7347B-3B72DD450BD61D3D"
            if (guidString.Contains("-") && guidString.Length == 33)
            {
                string[] parts = guidString.Split('-');
                if (parts.Length == 2 && parts[0].Length == 16 && parts[1].Length == 16)
                {
                    Debug.Log("Detected ARCore cloud anchor ID format");

                    // Parse each part as hexadecimal ulong
                    ulong guidLow = ulong.Parse(parts[0], System.Globalization.NumberStyles.HexNumber);
                    ulong guidHigh = ulong.Parse(parts[1], System.Globalization.NumberStyles.HexNumber);

                    Debug.Log($"Parsed as guidLow: {guidLow:X16}, guidHigh: {guidHigh:X16}");

                    // Use the correct constructor with 2 ulong parameters
                    return new SerializableGuid(guidLow, guidHigh);
                }
            }

            // Fallback: try standard GUID format
            try
            {
                Guid standardGuid = new Guid(guidString);
                // Use the constructor that takes a System.Guid
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
    
    private async Task LoadAnchorByGuid(SerializableGuid guid)
    {
        Debug.Log($"Attempting to load anchor with GUID: {guid}");

        if (originalAnchorGO != null)
        {
            Destroy(originalAnchorGO);
        }

        var result = await aRAnchorManager.TryLoadAnchorAsync(guid);

        if (result.status.IsSuccess())
        {
            Debug.Log("Anchor loaded successfully!");
            ARAnchor loadedAnchor = result.value;

            GameObject loadedCube = Instantiate(cubePrefab, loadedAnchor.transform);
            GameObject loadedSphere = Instantiate(spherePrefab, loadedAnchor.transform);

            loadedCube.GetComponent<MeshRenderer>().material = blueMaterial;
            loadedSphere.GetComponent<MeshRenderer>().material = blueMaterial;
            
            originalAnchorGO = loadedAnchor.gameObject;
        }
        else
        {
            Debug.LogError($"Failed to load anchor: {result.status}");
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

    public async void EraseSavedAnchor()
    {
        if (!isAnchorSaved)
        {
            Debug.LogWarning("No anchor has been saved. Nothing to erase.");
            return;
        }

        Debug.Log($"Attempting to erase anchor with GUID: {savedguid}");

        var resultStatus = await aRAnchorManager.TryEraseAnchorAsync(savedguid);

        if (resultStatus.IsSuccess())
        {
            Debug.Log("Anchor erased successfully from the cloud!");

            isAnchorSaved = false;
            savedguid = default;
            if (EraseButton != null) EraseButton.interactable = false;
            if (LoadButton != null) LoadButton.interactable = false;
            if (copyGuidButton != null) copyGuidButton.interactable = false; // NEW: Disable copy button
            
            // Update GUID display text
            if (guidDisplayText != null)
            {
                guidDisplayText.text = "No GUID saved";
            }

            if (originalAnchorGO != null)
            {
                Destroy(originalAnchorGO);
            }
        }
        else
        {
            Debug.LogError($"Failed to erase anchor: {resultStatus}");
        }
    }

}