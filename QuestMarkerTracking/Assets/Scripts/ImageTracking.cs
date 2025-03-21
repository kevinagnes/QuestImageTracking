using OpenCVForUnity.Calib3dModule;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.UnityUtils;
using OpenCVMarkerLessAR;
using UnityEngine;

/// <summary>
/// A component that performs image (pattern) tracking using a pattern detector.
/// It initializes the pattern from a given Texture2D, computes the pose from a live frame,
/// and applies the transformation to either an AR object or the AR camera.
/// </summary>
public class ImageTracking : MonoBehaviour
{
    [Header("Tracking Settings")]
    /// <summary>
    /// Division factor for input image resolution. Higher values improve performance but reduce detection accuracy.
    /// </summary>
    [SerializeField] private int _divideNumber = 2;
    public int DivideNumber { get => _divideNumber; set => _divideNumber = value; }

    [Header("Pattern Settings")]
    [Tooltip("The image that will be tracked.")]
    public Texture2D patternTexture;

    // Camera calibration matrices.
    private Mat _cameraIntrinsicMatrix;
    private MatOfDouble _cameraDistortionCoeffs;

    // Matrices for converting from OpenCV’s right-handed to Unity’s left-handed coordinate system.
    private Matrix4x4 _invertYM;
    private Matrix4x4 _invertZM;

    // Pattern detection objects.
    private Pattern _pattern;
    private PatternTrackingInfo _patternTrackingInfo;
    private PatternDetector _patternDetector;

    // OpenCV matrices for image processing
    /// <summary>
    /// Full-size RGBA mat from original webcam image.
    /// </summary>
    private Mat _originalWebcamMat;

    /// <summary>
    /// Resized mat for intermediate processing.
    /// </summary>
    private Mat _halfSizeMat;

    /// <summary>
    /// Grayscale mat for pattern detection.
    /// </summary>
    private Mat _grayMat;

    private bool _isReady = false;
    public bool IsReady { get => _isReady; set => _isReady = value; }


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
        _grayMat = new Mat(processingHeight, processingWidth, CvType.CV_8UC1);

        // Set up conversion matrices.
        _invertYM = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1, -1, 1));
        _invertZM = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1, 1, -1));

        // Initialize the pattern detector if a pattern texture is provided.
        if (patternTexture != null)
        {
            // Convert the Texture2D to an OpenCV Mat.
            Mat patternMat = new Mat(patternTexture.height, patternTexture.width, CvType.CV_8UC4);
            Utils.texture2DToMat(patternTexture, patternMat);
            // (Optional: convert color space if needed.)

            // Create the pattern and tracking info objects.
            _pattern = new Pattern();
            _patternTrackingInfo = new PatternTrackingInfo();
            _patternDetector = new PatternDetector(null, null, null, true);

            // Build and train the pattern from the provided image.
            bool patternBuildSucceeded = _patternDetector.buildPatternFromImage(patternMat, _pattern);
            if (patternBuildSucceeded)
            {
                _patternDetector.train(_pattern);
                _isReady = true;
            }
            else
            {
                Debug.LogError("Pattern build failed! Check the pattern texture for sufficient keypoints.");
            }
            patternMat.Dispose();
        }
        else
        {
            Debug.LogError("Pattern texture not set!");
        }
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
        if (_grayMat != null)
        {
            _grayMat.release();
        }
    }

    /// <summary>
    /// Detect if the pattern is in the provided webcam texture
    /// </summary>
    /// <param name="webCamTexture"></param>
    /// <param name="resultTexture"></param>
    public bool IsImageDetected(WebCamTexture webCamTexture, Texture2D resultTexture = null)
    {
        if(!_isReady || webCamTexture == null)
        {
            return false;
        }

        // Get image from webcam at full size
        Utils.webCamTextureToMat(webCamTexture, _originalWebcamMat);

        // Resize for processing
        Imgproc.resize(_originalWebcamMat, _halfSizeMat, _halfSizeMat.size());

        // Convert to grayscale for image processing
        if (_halfSizeMat.channels() == 4)
        {
            Imgproc.cvtColor(_halfSizeMat, _grayMat, Imgproc.COLOR_RGBA2GRAY);
        }
        else if (_halfSizeMat.channels() == 3)
        {
            Imgproc.cvtColor(_halfSizeMat, _grayMat, Imgproc.COLOR_RGB2GRAY);
        }

        if (resultTexture != null)
        {
            Utils.matToTexture2D(_grayMat, resultTexture);
        }

        return _patternDetector.findPattern(_grayMat, _patternTrackingInfo);
    }

    /// <summary>
    /// Estimates the pose of the detected pattern and applies it to the AR object.
    /// </summary>
    /// <param name="ARGameObject"></param>
    /// <param name="camTransform"></param>
    public void EstimateImagePose(GameObject ARGameObject, Transform camTransform)
    {
        if (!_isReady)
        {
            Debug.LogError("ImageTracking has not been initialized.");
            return;
        }

        // Compute the 3D pose from the detected pattern.
        _patternTrackingInfo.computePose(_pattern, _cameraIntrinsicMatrix, _cameraDistortionCoeffs);
        Matrix4x4 transformationM = _patternTrackingInfo.pose3d;

        // Convert coordinate systems.
        Matrix4x4 ARM = _invertYM * transformationM * _invertYM;
        ARM = ARM * _invertYM * _invertZM;

        // Update the transform based on the chosen mode.
        ARM = camTransform.localToWorldMatrix * ARM;
        ARUtils.SetTransformFromMatrix(ARGameObject.transform, ref ARM);
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
