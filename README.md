# MR Vision 3D Bounding Box



## 📖 About The Project

**MR Vision 3D Bounding Box** is a Mixed Reality (MR) computer vision application built for the Meta Quest platform using Unity. This project demonstrates how to leverage real-time passthrough camera feeds and depth maps to interact with and analyze the physical environment. 

The application breaks down the spatial understanding process into iterative phases—from basic MR passthrough and depth reading, to generating real-time 3D point clouds, and ultimately rendering precise 3D bounding boxes around real-world objects. It is designed as a modular pipeline for developers looking to integrate advanced environmental understanding and object segmentation into their XR applications.

**Key Features:**
* **Passthrough & Depth Vision:** Real-time access to the Meta Quest's camera and depth sensors.
* **Point Cloud Generation:** Dynamic conversion of depth maps into interactive 3D point clouds.
* **Object Selection:** Pixel and precise point selection tools to isolate objects in the environment.
* **3D Bounding Boxes:** Automated and manual generation of precise 3D bounding boxes around physical objects.
However The Bouding box formed is not so accurate.
---

## 🚀 How to Run

### Prerequisites
* **Unity Engine:** Ensure you have a recent version of Unity installed (with Android Build Support).
* **Hardware:** Meta Quest 3 (or compatible Meta MR headset).
* **Meta XR SDK:** The project relies on the Meta XR Core SDK and Passthrough features.

### Installation & Setup

1. **Clone the Repository**
   ```bash
   git clone https://github.com/meta-quest-app/MR-Vision-3d-Bounding-Box-.git
   ```

2. **Open in Unity**
   * Launch Unity Hub.
   * Click **Open** and select the cloned repository folder.
   * Wait for Unity to import all assets and resolve packages.

3. **Explore the Scenes**
   The project is divided into multiple phases to easily understand the pipeline. Navigate to the `Assets/` folder to explore scenes like:
   * `Phase1_Passthrough.unity` - Basic MR Passthrough.
   * `phase2_depth.unity` - Depth map visualization.
   * `phase3_pointcloud.unity` - 3D Point Cloud generation.
   * `Phase7_BB.unity` / `BestModel.unity` - Full 3D Bounding Box rendering.

4. **Build and Run**
   * Go to `File > Build Settings`.
   * Ensure the platform is set to **Android** (and ASTC texture compression is enabled).
   * Connect your Meta Quest headset via USB.
   * Click **Build and Run** to deploy the app directly to your headset, or use **Meta Quest Link** to test in Play mode inside the editor.

---

## 🛠️ Built With
* [Unity](https://unity.com/)
* [Meta XR SDK](https://developer.oculus.com/downloads/package/meta-xr-core-sdk)
* C#
