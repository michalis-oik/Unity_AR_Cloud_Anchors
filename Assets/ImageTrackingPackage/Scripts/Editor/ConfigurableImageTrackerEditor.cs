using UnityEngine;
using UnityEditor;
using ImageTrackingPackage;

[CustomEditor(typeof(ConfigurableImageTracker))]
public class ConfigurableImageTrackerEditor : Editor
{
    private SerializedProperty trackedImageManager;
    private SerializedProperty anchorManager;
    private SerializedProperty trackingMode;
    private SerializedProperty librarySource;
    private SerializedProperty urlImageCollection;
    private SerializedProperty referenceImageLibrary;
    private SerializedProperty onImageTracked;
    private SerializedProperty onTrackingInitialized;
    private SerializedProperty onAllImagesDownloaded;
    private SerializedProperty onReadyToScan;
    private SerializedProperty onTrackingStateChanged;
    private SerializedProperty onTrackingLost;
    private SerializedProperty onTrackingReset;

    private void OnEnable()
    {
        // Get references to all serialized properties
        trackedImageManager = serializedObject.FindProperty("trackedImageManager");
        anchorManager = serializedObject.FindProperty("anchorManager");
        trackingMode = serializedObject.FindProperty("trackingMode");
        librarySource = serializedObject.FindProperty("librarySource");
        urlImageCollection = serializedObject.FindProperty("urlImageCollection");
        referenceImageLibrary = serializedObject.FindProperty("referenceImageLibrary");
        onImageTracked = serializedObject.FindProperty("OnImageTracked");
        onTrackingInitialized = serializedObject.FindProperty("OnTrackingInitialized");
        onAllImagesDownloaded = serializedObject.FindProperty("OnAllImagesDownloaded");
        onReadyToScan = serializedObject.FindProperty("OnReadyToScan");
        onTrackingStateChanged = serializedObject.FindProperty("OnTrackingStateChanged");
        onTrackingLost = serializedObject.FindProperty("OnTrackingLost");
        onTrackingReset = serializedObject.FindProperty("OnTrackingReset");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Draw the script reference
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.ObjectField("Script", MonoScript.FromMonoBehaviour((ConfigurableImageTracker)target), typeof(MonoScript), false);
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space();

        // AR System Dependencies Section
        EditorGUILayout.LabelField("AR System Dependencies", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(trackedImageManager, new GUIContent("Tracked Image Manager"));
        EditorGUILayout.PropertyField(anchorManager, new GUIContent("Anchor Manager"));

        EditorGUILayout.Space();

        // Tracking Configuration Section
        EditorGUILayout.LabelField("Tracking Configuration", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(trackingMode, new GUIContent("Tracking Mode"));
        EditorGUILayout.PropertyField(librarySource, new GUIContent("Library Source"));

        EditorGUILayout.Space();

        // Conditionally show different sections based on LibrarySource
        ConfigurableImageTracker.LibrarySource currentLibrarySource = (ConfigurableImageTracker.LibrarySource)librarySource.enumValueIndex;

        if (currentLibrarySource == ConfigurableImageTracker.LibrarySource.DynamicCreation)
        {
            // Show Image Target Setup (Dynamic Creation) section
            EditorGUILayout.LabelField("Image Target Setup (Dynamic Creation)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(urlImageCollection, new GUIContent("URL Image Collection"));
            
            // Show help box for dynamic creation mode
            EditorGUILayout.HelpBox("In Dynamic Creation mode, images will be downloaded from URLs and added to a runtime library.", MessageType.Info);
        }
        else if (currentLibrarySource == ConfigurableImageTracker.LibrarySource.InspectorReference)
        {
            // Show Reference Library (Inspector Reference Mode Only) section
            EditorGUILayout.LabelField("Reference Library (Inspector Reference Mode Only)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(referenceImageLibrary, new GUIContent("Reference Image Library"));
            
            // Show help box for inspector reference mode
            EditorGUILayout.HelpBox("In Inspector Reference mode, a pre-configured XR Reference Image Library will be used.", MessageType.Info);
        }

        EditorGUILayout.Space();

        // Tracking Events Section
        EditorGUILayout.LabelField("Tracking Events", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(onImageTracked, new GUIContent("On Image Tracked"));

        EditorGUILayout.Space();

        // Setup & Initialization Events Section
        EditorGUILayout.LabelField("Setup & Initialization Events", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(onTrackingInitialized, new GUIContent("On Tracking Initialized"));
        EditorGUILayout.PropertyField(onAllImagesDownloaded, new GUIContent("On All Images Downloaded"));
        EditorGUILayout.PropertyField(onReadyToScan, new GUIContent("On Ready To Scan"));

        EditorGUILayout.Space();

        // Tracking State Events Section
        EditorGUILayout.LabelField("Tracking State Events", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(onTrackingStateChanged, new GUIContent("On Tracking State Changed"));
        EditorGUILayout.PropertyField(onTrackingLost, new GUIContent("On Tracking Lost"));
        EditorGUILayout.PropertyField(onTrackingReset, new GUIContent("On Tracking Reset"));

        // Apply changes
        serializedObject.ApplyModifiedProperties();
    }
}
