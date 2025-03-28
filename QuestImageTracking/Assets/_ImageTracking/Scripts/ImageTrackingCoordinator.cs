using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.UnityUtils;
using OpenCVForUnity.UnityUtils.Helper;
using UnityEngine.UI;
using PassthroughCameraSamples;
using Meta.XR.Samples;
using UnityEngine.Assertions;
using TMPro;

/// <summary>
/// A coordinator that sets up the Quest passthrough camera (using WebCamTextureManager),
/// initializes the ImageTracking component with the proper camera calibration values,
/// and runs the image tracking detection in the Update loop.
/// </summary>
[MetaCodeSample("PassthroughCameraApiSamples-ImageTracking")]
public class ImageTrackingCoordinator : MonoBehaviour
{
    [Header("Camera Setup")]
    [Tooltip("Component that manages the camera feed.")]
    [SerializeField] private WebCamTextureManager _webCamTextureManager;
    private PassthroughCameraEye CameraEye => _webCamTextureManager.Eye;
    private Vector2Int CameraResolution => _webCamTextureManager.RequestedResolution;

    [Tooltip("An anchor for the camera.")]
    [SerializeField] private Transform _cameraAnchor;

    [Header("Image Tracking")]
    [Tooltip("The ImageTracking component that handles pattern detection.")]
    [SerializeField] private ImageTracking _imageTracking;
    [SerializeField] private TextMeshPro _modeText;
    private Texture2D _resultTexture;
    private bool _isDebugMode = false;
    GameObject[] _arHelpers;

    private IEnumerator Start()
    {
        _arHelpers = GameObject.FindGameObjectsWithTag("ARHelper");
        SetDebugMode(_isDebugMode);

        // Validate required components
        if (_webCamTextureManager == null)
        {
            Debug.LogError($"PCA: {nameof(_webCamTextureManager)} field is required " +
                        $"for the component {nameof(ImageTrackingCoordinator)} to operate properly");
            enabled = false;
            yield break;
        }

        // Wait for camera permissions
        Assert.IsFalse(_webCamTextureManager.enabled);
        yield return WaitForCameraPermission();

        // Initialize camera
        yield return InitializeCamera();

        // Initialize the image tracking with proper camera parameters
        InitializeImageTracking();
    }

    /// <summary>
    /// Waits until camera permission is granted.
    /// </summary>
    private IEnumerator WaitForCameraPermission()
    {
        while (PassthroughCameraPermissions.HasCameraPermission != true)
        {
            yield return null;
        }
    }

    /// <summary>
    /// Initializes the camera with appropriate resolution and waits until ready.
    /// </summary>
    private IEnumerator InitializeCamera()
    {
        // Set the resolution and enable the camera manager
        _webCamTextureManager.RequestedResolution = PassthroughCameraUtils.GetCameraIntrinsics(CameraEye).Resolution;
        _webCamTextureManager.enabled = true;

        // Wait until the camera texture is available
        while(_webCamTextureManager.WebCamTexture == null)
        {
            yield return null;
        }
    }

    /// <summary>
    /// Initializes the image tracking system with accurate camera parameters.
    /// </summary>
    private void InitializeImageTracking()
    {
        // Get accurate camera intrinsics from PassthroughCameraUtils
        var intrinsics = PassthroughCameraUtils.GetCameraIntrinsics(CameraEye);
        var cx = intrinsics.PrincipalPoint.x;  // Principal point X (optical center)
        var cy = intrinsics.PrincipalPoint.y;  // Principal point Y (optical center)
        var fx = intrinsics.FocalLength.x;     // Focal length X
        var fy = intrinsics.FocalLength.y;     // Focal length Y
        var width = intrinsics.Resolution.x;   // Image width
        var height = intrinsics.Resolution.y;  // Image height

        // Configure the ImageTracking component
        _imageTracking.Initialize(width, height, cx, cy, fx, fy);

        _modeText.text = _imageTracking.GetTrackingMode() == ImageTrackingMode.Dynamic ? "Dynamic Mode" : "Static Mode";
    }

    void Update()
    {
        // Skip if camera isn't ready
        if (_webCamTextureManager.WebCamTexture == null || !_imageTracking.IsReady)
            return;

        // Toggle between camera view and AR visualization on button press
        HandleDebugVisualizationToggle();

        // Toggle between tracking modes on button press
        HandleModeToggle();

        // Update camera positions
        UpdateCameraPoses();

        // Process the current frame for image tracking
        ProcessImageTracking();
    }

    /// <summary>
    /// Handles button input to toggle between camera view and AR visualization.
    /// </summary>
    private void HandleDebugVisualizationToggle()
    {
        if (OVRInput.GetDown(OVRInput.Button.One))
        {
            SetDebugMode(!_isDebugMode);
        }
    }

    private void SetDebugMode(bool enabled)
    {
        _isDebugMode = enabled;
        foreach(var gameObject in _arHelpers)
        {
            gameObject.SetActive(enabled);
        }
    }

    // Add this to HandleVisualizationToggle() or create a new method
    private void HandleModeToggle()
    {
        if (OVRInput.GetDown(OVRInput.Button.Two))
        {
            // Toggle between modes
            ImageTrackingMode newMode = _imageTracking.GetTrackingMode() == ImageTrackingMode.Dynamic ? 
                ImageTrackingMode.Static : 
                ImageTrackingMode.Dynamic;
                
            _imageTracking.SetTrackingMode(newMode, true);
            _modeText.text = newMode == ImageTrackingMode.Dynamic ? "Dynamic Mode" : "Static Mode";
        }
    }

    /// <summary>
    /// Processes the current camera frame for image tracking.
    /// </summary>
    private void ProcessImageTracking()
    {
        //If we are in static mode and all the images have been stabilized, we can skip the detection step
        if (_imageTracking.GetTrackingMode() == ImageTrackingMode.Static && _imageTracking.AreAllImagesStabilized())
        {
            return;
        }

        // Detect all markers in the current frame
        _imageTracking.DetectImages(_webCamTextureManager.WebCamTexture, _resultTexture);
        
        // Update poses for all detected markers
        _imageTracking.EstimateImagePoses(_cameraAnchor);
    }

    /// <summary>
    /// Sets the visibility of all AR GameObjects
    /// </summary>
    private void SetARObjectsVisibility(bool isVisible)
    {
        foreach (var marker in _imageTracking.TrackedImages)
        {
            if (marker.virtualObject != null)
            {
                var rendererList = marker.virtualObject.GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in rendererList)
                {
                    renderer.enabled = isVisible && marker.isDetected;
                }
            }
        }
    }

    /// <summary>
    /// Updates the positions and rotations of camera-related transforms based on head and camera poses.
    /// </summary>
    private void UpdateCameraPoses()
    {
        // Get current camera pose
        var cameraPose = PassthroughCameraUtils.GetCameraPoseInWorld(CameraEye);
        
        // Update camera anchor position and rotation
        if (_cameraAnchor != null)
        {
            _cameraAnchor.position = cameraPose.position;
            _cameraAnchor.rotation = cameraPose.rotation;
        }
    }
    
    /// <summary>
    /// Add a new marker at runtime
    /// </summary>
    public void AddTrackedImage(ARTrackedImage marker)
    {
        if (_imageTracking != null && _imageTracking.IsReady)
        {
            _imageTracking.AddImage(marker);
        }
    }
}