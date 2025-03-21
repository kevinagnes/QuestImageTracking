using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.UnityUtils;
using OpenCVForUnity.UnityUtils.Helper;
using UnityEngine.UI;
using PassthroughCameraSamples;
using Meta.XR.Samples;
using TryAR.MarkerTracking;
using UnityEngine.Assertions;

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
    [Tooltip("The AR camera.")]
    [SerializeField] private Camera ARCamera;
    [Tooltip("An anchor for the camera.")]
    [SerializeField] private Transform _cameraAnchor;
    [SerializeField] private Canvas _cameraCanvas;
    [SerializeField] private float _canvasDistance = 1f;

    [Header("Image Tracking")]
    [Tooltip("The ImageTracking component that handles pattern detection.")]
    [SerializeField] private ImageTracking _imageTracking;

    [Header("AR Object")]
    [Tooltip("The GameObject to be updated with the tracked pose.")]
    [SerializeField] private GameObject ARGameObject;

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
        SetARObjectVisibility(!_showCameraCanvas);
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
        int divideNumber = _imageTracking.DivideNumber;
        _resultTexture = new Texture2D(width/divideNumber, height/divideNumber, TextureFormat.RGB24, false);
        _resultRawImage.texture = _resultTexture;
    }


    void Update()
    {
        // Skip if camera isn't ready
        if (_webCamTextureManager.WebCamTexture == null || !_imageTracking.IsReady)
            return;

        // Toggle between camera view and AR visualization on button press
        HandleVisualizationToggle();

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
            SetARObjectVisibility(!_showCameraCanvas);
        }
    }

        /// <summary>
    /// Processes the current camera frame for image tracking.
    /// </summary>
    private void ProcessImageTracking()
    {
        if(_imageTracking.IsImageDetected(_webCamTextureManager.WebCamTexture, _resultTexture))
        {
            Debug.Log("Image detected!");
            _imageTracking.EstimateImagePose(ARGameObject, ARCamera.transform);
        }
    }

    /// <summary>
    /// Sets the visibility of the AR GameObject
    /// </summary>
    private void SetARObjectVisibility(bool isVisible)
    {
        if (ARGameObject != null)
        {
            var rendererList = ARGameObject.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in rendererList)
            {
                renderer.enabled = isVisible;
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
}
