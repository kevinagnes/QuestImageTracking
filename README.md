# Quest ArUco Marker Tracking

This repository enables **single and multi-marker detection and tracking** using OpenCV with the **Meta Quest 3/3S Passthrough Camera API**.  
It provides a robust solution for **augmented reality applications** on Quest devices, supporting both individual markers and multiple markers simultaneously.

For a demonstration, check out the following videos:

▶ **Single Marker Tracking Demo**  
[![Single Marker Demo](https://img.youtube.com/vi/cJSjYMuJu8w/0.jpg)](https://www.youtube.com/watch?v=cJSjYMuJu8w)

▶ **Multi-Marker Tracking Demo**  
[![Multi-Marker Demo](https://img.youtube.com/vi/Y0mqQ_nxve8/0.jpg)](https://www.youtube.com/watch?v=Y0mqQ_nxve8)

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
Additionally, the marker size is assumed to be **0.1m**.   
If needed, feel free to modify these settings to fit your use case.  

🔄 **View Mode Switching**  
You can switch the view mode by pressing the **A button** on the controller.

## Reference Repositories

This implementation is based on the following sample repositories:  

- [Unity-PassthroughCameraApiSamples](https://github.com/oculus-samples/Unity-PassthroughCameraApiSamples)  
- [QuestCameraKit](https://github.com/xrdevrob/QuestCameraKit)  

## Contact

If you have any questions, feel free to reach out:  

- **X (Twitter)**:  
  - (EN) [@Tks_Yoshinaga](https://x.com/Tks_Yoshinaga)  
  - (JP) [@Taka_Yoshinaga](https://x.com/Taka_Yoshinaga)  
- **LinkedIn**: [Tks Yoshinaga](https://www.linkedin.com/in/tks-yoshinaga/)  
- **Discord**: [Join the community](https://discord.gg/kDENwuPD4t)  
