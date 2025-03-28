using System.Collections.Generic;
using OpenCVForUnity.Calib3dModule;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.UnityUtils;
using OpenCVMarkerLessAR;
using UnityEngine;

/// <summary>
/// Defines the tracking mode for image markers
/// </summary>
public enum ImageTrackingMode
{
    /// <summary>
    /// Dynamic mode continuously tracks pattern poses every frame
    /// </summary>
    Dynamic,
    
    /// <summary>
    /// Static mode tracks until a stable pose is determined, then locks the transform
    /// </summary>
    Static
}

/// <summary>
/// A component that performs image (pattern) tracking using a multi-pattern detector.
/// It initializes patterns from Texture2Ds, computes poses from a live frame,
/// and applies transformations to the corresponding AR objects.
/// </summary>
public class ImageTracking : MonoBehaviour
{
    [Header("Tracking Mode")]
    [SerializeField] private ImageTrackingMode _trackingMode = ImageTrackingMode.Dynamic;
    
    [Header("Detection Settings")]
    /// <summary>
    /// Downsample factor for input image resolution. Higher values improve performance but reduce detection accuracy.
    /// </summary>
    [Range(0.1f, 1.0f)]
    [SerializeField] private float _processingDownsampleFactor = 0.5f;
    public float ProcessingDownsampleFactor { get => _processingDownsampleFactor; set => _processingDownsampleFactor = value; }

    [Header("Dynamic Mode Settings")]
    /// <summary>
    /// Coefficient for low-pass filter (0-1). Higher values mean more smoothing.
    /// </summary>
    [Range(0, 1)]
    [SerializeField] private float _dynamicPoseFilterCoefficient = 0.5f;
    
    /// <summary>
    /// Whether to hide objects when their markers are not detected in dynamic mode
    /// </summary>
    [SerializeField] private bool _hideObjectsWhenNotDetected = true;

    [Header("Static Mode Settings")]
    /// <summary>
    /// Number of consecutive similar poses required to consider a pose as stable
    /// </summary>
    [SerializeField] private int _stabilityPoseCount = 10;
    
    /// <summary>
    /// Maximum allowed position difference (in meters) between poses to be considered similar
    /// </summary>
    [SerializeField] private float _maxPositionDifference = 0.01f;
    
    /// <summary>
    /// Maximum allowed angle difference (in degrees) between poses to be considered similar
    /// </summary>
    [SerializeField] private float _maxAngleDifference = 2.0f;

    [Header("Tracked images")]
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
    /// Gets the current tracking mode
    /// </summary>
    public ImageTrackingMode GetTrackingMode()
    {
        return _trackingMode;
    }

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
        if (trackedImage.data.markerTexture != null)
        {
            // Convert the Texture2D to an OpenCV Mat
            Mat patternMat = new Mat(trackedImage.data.markerTexture.height, trackedImage.data.markerTexture.width, CvType.CV_8UC4);
            Utils.texture2DToMat(trackedImage.data.markerTexture, patternMat);

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
                trackedImage.data.id);
                
            if (patternBuildSucceeded)
            {
                DebugHelpers imageDebugger = trackedImage.virtualObject.GetComponentInChildren<DebugHelpers>(true);
                if(imageDebugger != null)
                {
                    imageDebugger.showMat(patternMat);
                }

                // Store reference to tracking info
                _patternTrackingInfos[trackedImage.data.id] = trackedImage.trackingInfo;
            }
            else
            {
                Debug.LogError($"Pattern build failed for marker {trackedImage.data.id}! Check the pattern texture for sufficient keypoints.");
            }
            
            patternMat.Dispose();
        }
        else
        {
            Debug.LogError($"Pattern texture not set for marker {trackedImage.data.id}!");
        }
    }

    /// <summary>
    /// Initialize all OpenCV matrices and detector parameters
    /// </summary>
    public void InitializeMatrices(int originalWidth, int originalHeight, float cX, float cY, float fX, float fY)
    {
        // Processing dimensions (scaled by downsample factor)
        int processingWidth = Mathf.RoundToInt(originalWidth * _processingDownsampleFactor);
        int processingHeight = Mathf.RoundToInt(originalHeight * _processingDownsampleFactor);
        fX = fX * _processingDownsampleFactor;
        fY = fY * _processingDownsampleFactor;
        cX = cX * _processingDownsampleFactor;
        cY = cY * _processingDownsampleFactor;

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
    /// Sets the tracking mode and resets stabilization status if needed
    /// </summary>
    /// <param name="mode">The new tracking mode</param>
    /// <param name="resetStabilizedMarkers">Whether to reset markers that were already stabilized</param>
    public void SetTrackingMode(ImageTrackingMode mode, bool resetStabilizedMarkers = false)
    {
        // If changing from static to dynamic or resetting, reset stabilization
        if ((mode == ImageTrackingMode.Dynamic && _trackingMode == ImageTrackingMode.Static) || 
            (mode == ImageTrackingMode.Static && resetStabilizedMarkers))
        {
            foreach (var image in _trackedImages)
            {
                image.isStabilized = false;
                image.similarPoseCount = 0;
                image.recentPoses.Clear();
            }
        }
        
        _trackingMode = mode;
        Debug.Log($"Tracking mode set to {mode}");
    }
    
    /// <summary>
    /// Resets a specific marker's stabilization status (useful for testing)
    /// </summary>
    public void ResetMarkerStabilization(string markerId)
    {
        var marker = _trackedImages.Find(img => img.data.id == markerId);
        if (marker != null)
        {
            marker.isStabilized = false;
            marker.similarPoseCount = 0;
            marker.recentPoses.Clear();
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
            ARTrackedImage trackedImage = _trackedImages.Find(img => img.data.id == patternId);
            if (trackedImage != null)
            {
                trackedImage.isDetected = true;
                
                // Since the detector writes directly to our tracking infos dictionary,
                // we don't need to copy any data here
            }
        }
    }

    public bool AreAllImagesStabilized()
    {
        foreach (var image in _trackedImages)
        {
            if (!image.isStabilized)
            {
                return false;
            }
        }
        return true;
    }

        /// <summary>
    /// Estimates the poses of all currently detected marker images
    /// </summary>
    /// <param name="camTransform">The transform of the camera</param>
    public void EstimateImagePoses(Transform camTransform)
    {
        if (!_isReady)
        {
            Debug.LogError("ImageTracking has not been initialized.");
            return;
        }
    
        foreach (var image in _trackedImages)
        {
            // Skip already stabilized images in static mode
            if (_trackingMode == ImageTrackingMode.Static && image.isStabilized)
            {
                continue;
            }
    
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
                currentPose.pos = camTransform.position + direction * (currentDistance * image.data.physicalSize);
                
                if (_trackingMode == ImageTrackingMode.Dynamic)
                {
                    // Apply smoothing if we have previous pose data
                    if (image.prevPose.rot != default)
                    {
                        // Lerp between previous and current poses based on the filter coefficient
                        currentPose.pos = Vector3.Lerp(image.prevPose.pos, currentPose.pos, 1 - _dynamicPoseFilterCoefficient);
                        currentPose.rot = Quaternion.Slerp(image.prevPose.rot, currentPose.rot, 1 - _dynamicPoseFilterCoefficient);
                    }
    
                    // Save the current pose for next frame
                    image.prevPose = currentPose;
    
                    // Apply pose to virtual object
                    Matrix4x4 smoothedARM = ARUtils.ConvertPoseDataToMatrix(ref currentPose);
                    ARUtils.SetTransformFromMatrix(image.virtualObject.transform, ref smoothedARM);
                    
                    // Apply scale adjustment directly to the game object based on this marker's size
                    image.virtualObject.transform.localScale = Vector3.one * image.data.physicalSize;
                    
                    // Ensure the virtual object is visible
                    SetGameObjectVisibility(image.virtualObject, true);
                }
                else if (_trackingMode == ImageTrackingMode.Static)
                {
                    // Process for static mode
                    ProcessStaticPose(image, currentPose);
                }
            }
            else if (image.virtualObject != null)
            {
                // Handle when marker is not detected
                if (_trackingMode == ImageTrackingMode.Dynamic && _hideObjectsWhenNotDetected)
                {
                    SetGameObjectVisibility(image.virtualObject, false);
                }
                else if (_trackingMode == ImageTrackingMode.Static && !image.isStabilized)
                {
                    // Reset stability counter when lost tracking before stabilization
                    image.similarPoseCount = 0;
                    image.recentPoses.Clear();
                }
            }
        }
    }
    
    /// <summary>
    /// Processes poses for static tracking mode, checking for stability
    /// </summary>
    private void ProcessStaticPose(ARTrackedImage image, PoseData currentPose)
    {
        // If we don't have enough poses yet, just add the current one
        if (image.recentPoses.Count < _stabilityPoseCount)
        {
            // First pose, just add it
            if (image.recentPoses.Count == 0)
            {
                image.recentPoses.Add(currentPose);
                image.similarPoseCount = 1;
            }
            else
            {
                // Check if this pose is similar to the last one
                PoseData lastPose = image.recentPoses[image.recentPoses.Count - 1];
                
                if (IsPoseSimilar(lastPose, currentPose))
                {
                    // The pose is similar, increment counter and add to list
                    image.similarPoseCount++;
                    image.recentPoses.Add(currentPose);
                }
                else
                {
                    // The pose is different, reset counter and list
                    image.similarPoseCount = 1;
                    image.recentPoses.Clear();
                    image.recentPoses.Add(currentPose);
                }
            }
            
            // Hide the object until stabilized
            SetGameObjectVisibility(image.virtualObject, false);
        }
        
        // Check if we have achieved stability
        if (image.similarPoseCount >= _stabilityPoseCount && !image.isStabilized)
        {
            // Calculate the average pose
            PoseData averagePose = CalculateAveragePose(image.recentPoses);
            
            // Apply the stable pose to the virtual object
            Matrix4x4 stableMatrix = ARUtils.ConvertPoseDataToMatrix(ref averagePose);
            ARUtils.SetTransformFromMatrix(image.virtualObject.transform, ref stableMatrix);
            
            // Apply scale adjustment
            image.virtualObject.transform.localScale = Vector3.one * image.data.physicalSize;
            
            // Mark as stabilized and make visible
            image.isStabilized = true;
            SetGameObjectVisibility(image.virtualObject, true);
            
            if(image.virtualObject.TryGetComponent(out ARTrackedImageEvent arTrackedImageEvent))
            {
                // Invoke the event to notify that the image has been stabilized
                arTrackedImageEvent.OnImageStabilized?.Invoke();
            }
            else
            {
                Debug.LogWarning($"ARTrackedImageEvent component not found on {image.virtualObject.name}.");
            }
            
            Debug.Log($"Marker {image.data.id} stabilized after {image.similarPoseCount} similar poses");
        }
    }
    
    /// <summary>
    /// Checks if two poses are similar within the configured thresholds
    /// </summary>
    private bool IsPoseSimilar(PoseData pose1, PoseData pose2)
    {
        // Check position distance
        float positionDifference = Vector3.Distance(pose1.pos, pose2.pos);
        
        // Check angle difference
        float angleDifference = Quaternion.Angle(pose1.rot, pose2.rot);
        
        return positionDifference <= _maxPositionDifference && 
               angleDifference <= _maxAngleDifference;
    }
    
    /// <summary>
    /// Calculates the average of a set of poses
    /// </summary>
    private PoseData CalculateAveragePose(List<PoseData> poses)
    {
        if (poses.Count == 0)
            return new PoseData();
            
        if (poses.Count == 1)
            return poses[0];
        
        Vector3 sumPosition = Vector3.zero;
        Vector4 sumRotation = Vector4.zero;
        
        // Sum all positions
        foreach (var pose in poses)
        {
            sumPosition += pose.pos;
            
            // Convert quaternion to vector4 for averaging
            Vector4 q = new Vector4(pose.rot.x, pose.rot.y, pose.rot.z, pose.rot.w);
            
            // Handle quaternion double-cover: ensure all quaternions are in the same hemisphere
            if (Vector4.Dot(q, sumRotation) < 0)
            {
                q = -q;
            }
            
            sumRotation += q;
        }
        
        // Divide by count to get average
        Vector3 avgPosition = sumPosition / poses.Count;
        
        // Normalize the average quaternion
        Vector4 avgRotationVec = sumRotation.normalized;
        Quaternion avgRotation = new Quaternion(avgRotationVec.x, avgRotationVec.y, avgRotationVec.z, avgRotationVec.w);
        
        return new PoseData { pos = avgPosition, rot = avgRotation };
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
        if (!_trackedImages.Exists(img => img.data.id == image.data.id))
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
            Debug.LogWarning($"Image with ID {image.data.id} already exists.");
        }
    }
    
    /// <summary>
    /// Remove a marker at runtime
    /// </summary>
    public bool RemoveImage(string imageMarkerId)
    {
        int index = _trackedImages.FindIndex(m => m.data.id == imageMarkerId);
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