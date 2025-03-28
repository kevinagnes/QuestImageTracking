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
    [Tooltip("Optional raw image for visualizing the camera feed.")]
    [SerializeField] private RawImage _resultRawImage;

    [Tooltip("An anchor for the camera.")]
    [SerializeField] private Transform _cameraAnchor;
    [SerializeField] private Canvas _cameraCanvas;
    [SerializeField] private float _canvasDistance = 1f;

    [Header("Image Tracking")]
    [Tooltip("The ImageTracking component that handles pattern detection.")]
    [SerializeField] private ImageTracking _imageTracking;
    [SerializeField] private TextMeshPro _modeText;

    private Texture2D _resultTexture;
    private bool _showCameraCanvas = true;

    private IEnumerator Start()
    {
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

        // Configure UI and tracking components
        ScaleCameraCanvas();

        // Initialize the image tracking with proper camera parameters
        InitializeImageTracking();

        // Set initial visibility states
        _cameraCanvas.gameObject.SetActive(_showCameraCanvas);
        SetARObjectsVisibility(!_showCameraCanvas);
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
    /// Calculates the dimensions of the canvas based on the distance from the camera origin and the camera resolution.
    /// </summary>
    private void ScaleCameraCanvas()
    {
        var cameraCanvasRectTransform = _cameraCanvas.GetComponentInChildren<RectTransform>();
        
        // Calculate field of view based on camera parameters
        var leftSidePointInCamera = PassthroughCameraUtils.ScreenPointToRayInCamera(CameraEye, new Vector2Int(0, CameraResolution.y / 2));
        var rightSidePointInCamera = PassthroughCameraUtils.ScreenPointToRayInCamera(CameraEye, new Vector2Int(CameraResolution.x, CameraResolution.y / 2));
        var horizontalFoVDegrees = Vector3.Angle(leftSidePointInCamera.direction, rightSidePointInCamera.direction);
        var horizontalFoVRadians = horizontalFoVDegrees / 180 * System.Math.PI;
        
        // Calculate canvas size to match camera view
        var newCanvasWidthInMeters = 2 * _canvasDistance * System.Math.Tan(horizontalFoVRadians / 2);
        var localScale = (float)(newCanvasWidthInMeters / cameraCanvasRectTransform.sizeDelta.x);
        cameraCanvasRectTransform.localScale = new Vector3(localScale, localScale, localScale);
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

        // Set up result texture if needed
        ConfigureResultTexture(width, height);
    }

    /// <summary>
    /// Configures the texture for displaying camera and tracking results.
    /// </summary>
    private void ConfigureResultTexture(int width, int height)
    {
        float downsampleFactor = _imageTracking.ProcessingDownsampleFactor;
        _resultTexture = new Texture2D(Mathf.RoundToInt(width * downsampleFactor), Mathf.RoundToInt(height * downsampleFactor), TextureFormat.RGB24, false);
        _resultRawImage.texture = _resultTexture;
    }

    void Update()
    {
        // Skip if camera isn't ready
        if (_webCamTextureManager.WebCamTexture == null || !_imageTracking.IsReady)
            return;

        // Toggle between camera view and AR visualization on button press
        HandleVisualizationToggle();

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
    private void HandleVisualizationToggle()
    {
        if (OVRInput.GetDown(OVRInput.Button.One))
        {
            _showCameraCanvas = !_showCameraCanvas;
            _cameraCanvas.gameObject.SetActive(_showCameraCanvas);
            // SetARObjectsVisibility(!_showCameraCanvas);
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

        // Position the canvas in front of the camera
        if (_cameraCanvas != null)
        {
            _cameraCanvas.transform.position = cameraPose.position + cameraPose.rotation * Vector3.forward * _canvasDistance;
            _cameraCanvas.transform.rotation = cameraPose.rotation;
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