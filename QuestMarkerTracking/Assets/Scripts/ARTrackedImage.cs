using UnityEngine;
using OpenCVMarkerLessAR;
using OpenCVForUnity.UnityUtils;


[System.Serializable]
public class ARTrackedImage
{
    [Tooltip("Unique identifier for this marker")]
    public string id;
    
    [Tooltip("The image that will be tracked")]
    public Texture2D markerTexture;
    
    [Tooltip("Physical size of the marker in meters")]
    public float physicalSize = 0.1f;
    
    [Tooltip("The GameObject to be updated with the tracked pose")]
    public GameObject virtualObject;
    
    // Runtime data - not serialized
    [System.NonSerialized]
    public Pattern pattern;
    
    [System.NonSerialized]
    public PatternTrackingInfo trackingInfo;
    
    [System.NonSerialized]
    public bool isDetected;
    
    [System.NonSerialized]
    public PoseData prevPose;

    public ARTrackedImage()
    {
        trackingInfo = new PatternTrackingInfo();
        prevPose = new PoseData();
        pattern = new Pattern();
    }
}