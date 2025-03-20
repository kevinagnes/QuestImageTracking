# QuestArUcoMarkerTracking

This repository is based on the following sample code for the Meta Quest 3/3S Passthrough Camera API, with added functionality for marker detection using OpenCV:

- [Unity-PassthroughCameraApiSamples](https://github.com/oculus-samples/Unity-PassthroughCameraApiSamples)
- [QuestCameraKit](https://github.com/xrdevrob/QuestCameraKit)

For a demonstration of how it works, check out the following video:  
[![Demo Video](https://img.youtube.com/vi/cJSjYMuJu8w/0.jpg)](https://www.youtube.com/watch?v=cJSjYMuJu8w)

## Dependencies

⚠ **Important Notice**  
When opening the project for the first time, you will likely encounter errors. This is because **OpenCV for Unity** is not yet installed. **Please ignore the errors initially, proceed to open the project, and install OpenCV for Unity manually.**  

This project uses **OpenCV for Unity**.   
Please purchase and install it from the Unity Asset Store:  
[OpenCV for Unity](https://assetstore.unity.com/packages/tools/integration/opencv-for-unity-21088?locale=en-US)  

Tested with **OpenCV for Unity v2.6.5**.

## Usage

To use this project, please download and install the required **ArUco markers** from the link below:  
[ArUco Marker PDF](https://github.com/TakashiYoshinaga/QuestArUcoMarkerTracking/blob/main/ArUcoMarker.pdf)  

By default, this project uses **DICT_4X4_50** as the marker dictionary.
