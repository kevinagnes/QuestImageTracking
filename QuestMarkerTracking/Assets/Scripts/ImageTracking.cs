using System.Collections.Generic;
using OpenCVForUnity.Calib3dModule;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.UnityUtils;
using OpenCVMarkerLessAR;
using UnityEngine;

/// <summary>
/// A component that performs image (pattern) tracking using a multi-pattern detector.
/// It initializes patterns from Texture2Ds, computes poses from a live frame,
/// and applies transformations to the corresponding AR objects.
/// </summary>
public class ImageTracking : MonoBehaviour
{
    [Header("Tracking Settings")]
    /// <summary>
    /// Division factor for input image resolution. Higher values improve performance but reduce detection accuracy.
    /// </summary>
    [SerializeField] private int _divideNumber = 2;

    /// <summary>
    /// Coefficient for low-pass filter (0-1). Higher values mean more smoothing.
    /// </summary>
    [Range(0, 1)]
    [SerializeField] private float _poseFilterCoefficient = 0.5f;
    public int DivideNumber { get => _divideNumber; set => _divideNumber = value; }

    [Header("Markers")]
    [SerializeField] private List<ARTrackedImage> _trackedImages = new List<ARTrackedImage>();
    
    // Camera calibration matrices.
    private Mat _cameraIntrinsicMatrix;
    private MatOfDouble _cameraDistortionCoeffs;

    // Matrices for converting from OpenCV's right-handed to Unity's left-handed coordinate system.
    private Matrix4x4 _invertYM;
    private Matrix4x4 _invertZM;

    // Pattern detection objects.
    private MultiplePatternDetector _multiPatternDetector;
    private List<string> _detectedPatternIds = new List<string>();
    private Dictionary<string, PatternTrackingInfo> _patternTrackingInfos = new Dictionary<string, PatternTrackingInfo>();

    // OpenCV matrices for image processing
    /// <summary>
    /// Full-size RGBA mat from original webcam image.
    /// </summary>
    private Mat _originalWebcamMat;

    /// <summary>
    /// Resized mat for intermediate processing.
    /// </summary>
    private Mat _halfSizeMat;

    private bool _isReady = false;
    public bool IsReady { get => _isReady; set => _isReady = value; }

    public List<ARTrackedImage> TrackedImages => _trackedImages;

    /// <summary>
    /// Initialize the marker tracking system with camera parameters
    /// </summary>
    /// <param name="imageWidth">Camera image width in pixels</param>
    /// <param name="imageHeight">Camera image height in pixels</param>
    /// <param name="cx">Principal point X coordinate</param>
    /// <param name="cy">Principal point Y coordinate</param>
    /// <param name="fx">Focal length X</param>
    /// <param name="fy">Focal length Y</param>
    public void Initialize(int imageWidth, int imageHeight, float cx, float cy, float fx, float fy)
    {
        InitializeMatrices(imageWidth, imageHeight, cx, cy, fx, fy);
        
        // Create multi-pattern detector
        _multiPatternDetector = new MultiplePatternDetector();
        _multiPatternDetector.enableRatioTest = true;
        _multiPatternDetector.enableHomographyRefinement = true;
        _multiPatternDetector.homographyReprojectionThreshold = 3.0f;
        
        // Initialize all markers
        foreach (var image in _trackedImages)
        {
            InitializeTrackedImage(image);
        }
        
        _isReady = true;
    }

    /// <summary>
    /// Initialize a single marker's pattern
    /// </summary>
    private void InitializeTrackedImage(ARTrackedImage trackedImage)
    {
        if (trackedImage.markerTexture != null)
        {
            // Convert the Texture2D to an OpenCV Mat
            Mat patternMat = new Mat(trackedImage.markerTexture.height, trackedImage.markerTexture.width, CvType.CV_8UC4);
            Utils.texture2DToMat(trackedImage.markerTexture, patternMat);

            // Create the pattern object if needed
            if (trackedImage.pattern == null)
            {
                trackedImage.pattern = new Pattern();
            }
            
            // Create tracking info if needed
            if (trackedImage.trackingInfo == null)
            {
                trackedImage.trackingInfo = new PatternTrackingInfo();
            }

            // Build and register the pattern with the detector
            bool patternBuildSucceeded = _multiPatternDetector.BuildAndRegisterPattern(
                patternMat, 
                trackedImage.pattern,
                trackedImage.id);
                
            if (patternBuildSucceeded)
            {
                DebugHelpers imageDebugger = trackedImage.virtualObject.GetComponentInChildren<DebugHelpers>();
                if(imageDebugger != null)
                {
                    imageDebugger.showMat(patternMat);
                }

                // Store reference to tracking info
                _patternTrackingInfos[trackedImage.id] = trackedImage.trackingInfo;
            }
            else
            {
                Debug.LogError($"Pattern build failed for marker {trackedImage.id}! Check the pattern texture for sufficient keypoints.");
            }
            
            patternMat.Dispose();
        }
        else
        {
            Debug.LogError($"Pattern texture not set for marker {trackedImage.id}!");
        }
    }

    /// <summary>
    /// Initialize all OpenCV matrices and detector parameters
    /// </summary>
    public void InitializeMatrices(int originalWidth, int originalHeight, float cX, float cY, float fX, float fY)
    {
        // Processing dimensions (scaled by divide number)
        int processingWidth = originalWidth / _divideNumber;
        int processingHeight = originalHeight / _divideNumber;
        fX = fX / _divideNumber;
        fY = fY / _divideNumber;
        cX = cX / _divideNumber;
        cY = cY / _divideNumber;

        // Create the camera intrinsic matrix.
        _cameraIntrinsicMatrix = new Mat(3, 3, CvType.CV_64FC1);
        _cameraIntrinsicMatrix.put(0, 0, fX);
        _cameraIntrinsicMatrix.put(0, 1, 0);
        _cameraIntrinsicMatrix.put(0, 2, cX);
        _cameraIntrinsicMatrix.put(1, 0, 0);
        _cameraIntrinsicMatrix.put(1, 1, fY);
        _cameraIntrinsicMatrix.put(1, 2, cY);
        _cameraIntrinsicMatrix.put(2, 0, 0);
        _cameraIntrinsicMatrix.put(2, 1, 0);
        _cameraIntrinsicMatrix.put(2, 2, 1.0f);

        // No distortion for Quest passthrough cameras.
        _cameraDistortionCoeffs = new MatOfDouble(0, 0, 0, 0);

        //Initialize all processing mats
        _originalWebcamMat = new Mat(originalHeight, originalWidth, CvType.CV_8UC4);
        _halfSizeMat = new Mat(processingHeight, processingWidth, CvType.CV_8UC4);

        // Set up conversion matrices.
        _invertYM = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1, -1, 1));
        _invertZM = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1, 1, -1));
    }

    /// <summary>
    /// Release all OpenCV resources
    /// </summary>
    public void ReleaseResources()
    {
        if (_cameraIntrinsicMatrix != null)
        {
            _cameraIntrinsicMatrix.release();
        }
        if (_cameraDistortionCoeffs != null)
        {
            _cameraDistortionCoeffs.release();
        }
        if (_originalWebcamMat != null)
        {
            _originalWebcamMat.release();
        }
        if (_halfSizeMat != null)
        {
            _halfSizeMat.release();
        }
        if (_multiPatternDetector != null)
        {
            _multiPatternDetector.Release();
        }
    }

    /// <summary>
    /// Detect images in the provided webcam texture
    /// </summary>
    /// <param name="webCamTexture"></param>
    /// <param name="resultTexture"></param>
    public void DetectImages(WebCamTexture webCamTexture, Texture2D resultTexture = null)
    {
        if(!_isReady || webCamTexture == null || _trackedImages.Count == 0)
        {
            return;
        }

        // Get image from webcam at full size
        Utils.webCamTextureToMat(webCamTexture, _originalWebcamMat);

        // Resize for processing
        Imgproc.resize(_originalWebcamMat, _halfSizeMat, _halfSizeMat.size());

        // Display result if requested
        if (resultTexture != null)
        {
            Utils.matToTexture2D(_halfSizeMat, resultTexture);
        }

        // Reset detection state for all markers
        foreach (var image in _trackedImages)
        {
            image.isDetected = false;
        }

        // Process image once to find all patterns
        _multiPatternDetector.DetectPatterns(_halfSizeMat, _detectedPatternIds, _patternTrackingInfos);
        
        // Update detection state
        foreach (var patternId in _detectedPatternIds)
        {
            // Find the corresponding tracked image
            ARTrackedImage trackedImage = _trackedImages.Find(img => img.id == patternId);
            if (trackedImage != null)
            {
                trackedImage.isDetected = true;
                
                // Since the detector writes directly to our tracking infos dictionary,
                // we don't need to copy any data here
            }
        }
    }

    public void EstimateImagePoses(Transform camTransform)
    {
        if (!_isReady)
        {
            Debug.LogError("ImageTracking has not been initialized.");
            return;
        }

        foreach (var image in _trackedImages)
        {
            if (image.isDetected && image.virtualObject != null)
            {
                // Compute the 3D pose from the detected pattern
                image.trackingInfo.computePose(image.pattern, _cameraIntrinsicMatrix, _cameraDistortionCoeffs);
                
                Matrix4x4 transformationM = image.trackingInfo.pose3d;
                
                // Convert from OpenCV coordinates to Unity coordinates
                Matrix4x4 ARM = _invertYM * transformationM * _invertYM;

                // Apply Y-axis and Z-axis reflection matrices (adjust the posture of the AR object)
                ARM = ARM * _invertYM * _invertZM;

                // Transform to world space
                ARM = camTransform.localToWorldMatrix * ARM;

                // Extract the current pose from the transformation matrix
                PoseData currentPose = ARUtils.ConvertMatrixToPoseData(ref ARM);
                
                // Apply depth adjustment (brings object closer or pushes it farther)
                Vector3 cameraToObject = currentPose.pos - camTransform.position;
                float currentDistance = cameraToObject.magnitude;
                Vector3 direction = cameraToObject.normalized;
                
                // Adjust the position along the camera-to-object vector based on this marker's size
                currentPose.pos = camTransform.position + direction * (currentDistance * image.physicalSize);
                
                // Apply smoothing if we have previous pose data
                if (image.prevPose.rot != default)
                {
                    // Lerp between previous and current poses based on the filter coefficient
                    currentPose.pos = Vector3.Lerp(image.prevPose.pos, currentPose.pos, 1 - _poseFilterCoefficient);
                    currentPose.rot = Quaternion.Slerp(image.prevPose.rot, currentPose.rot, 1 - _poseFilterCoefficient);
                }

                // Save the current pose for next frame
                image.prevPose = currentPose;

                // Convert back to matrix and apply to transform
                Matrix4x4 smoothedARM = ARUtils.ConvertPoseDataToMatrix(ref currentPose);
                ARUtils.SetTransformFromMatrix(image.virtualObject.transform, ref smoothedARM);
                
                // Apply scale adjustment directly to the game object based on this marker's size
                image.virtualObject.transform.localScale = Vector3.one * image.physicalSize;
                
                // Ensure the virtual object is visible
                SetGameObjectVisibility(image.virtualObject, true);
            }
            else if (image.virtualObject != null)
            {
                // Hide the object if marker is not detected
                //SetGameObjectVisibility(image.virtualObject, false);
            }
        }
    }
    
    /// <summary>
    /// Sets the visibility of a GameObject
    /// </summary>
    private void SetGameObjectVisibility(GameObject obj, bool isVisible)
    {
        var rendererList = obj.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in rendererList)
        {
            renderer.enabled = isVisible;
        }
    }

    /// <summary>
    /// Add a new marker at runtime
    /// </summary>
    public void AddImage(ARTrackedImage image)
    {
        // Only add if it doesn't already exist
        if (!_trackedImages.Exists(img => img.id == image.id))
        {
            _trackedImages.Add(image);
            
            // Initialize the new tracked image
            if (_isReady)
            {
                InitializeTrackedImage(image);
            }
        }
        else
        {
            Debug.LogWarning($"Image with ID {image.id} already exists.");
        }
    }
    
    /// <summary>
    /// Remove a marker at runtime
    /// </summary>
    public bool RemoveImage(string imageMarkerId)
    {
        int index = _trackedImages.FindIndex(m => m.id == imageMarkerId);
        if (index >= 0)
        {
            // Unregister from detector
            if (_multiPatternDetector != null)
            {
                _multiPatternDetector.UnregisterPattern(imageMarkerId);
            }
            
            // Remove from tracking info dictionary
            _patternTrackingInfos.Remove(imageMarkerId);
            
            // Remove from tracked images list
            _trackedImages.RemoveAt(index);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Explicitly release resources when the object is disposed
    /// </summary>
    public void Dispose()
    {
        ReleaseResources();
    }

    /// <summary>
    /// Clean up when object is destroyed
    /// </summary>
    void OnDestroy()
    {
        ReleaseResources();
    }
}