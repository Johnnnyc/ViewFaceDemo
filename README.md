# ViewFaceDemo - 人脸实时比对系统

基于 [ViewFaceCore](https://github.com/ViewFaceCore/ViewFaceCore)（SeetaFace6 .NET 封装）的 Windows 桌面人脸实时比对应用。通过摄像头实时采集人脸，与预先注册的比对库进行 1:N 人脸识别，返回相似度排名。

## 功能特性

- **摄像头实时采集**：支持 USB/内置摄像头实时画面预览
- **人脸检测 + 特征提取**：基于 SeetaFace6 深度学习模型
- **1:N 人脸比对**：将摄像头人脸与比对库中所有图片逐一比对
- **实时比对模式**：每 500ms 自动检测并比对，无需手动操作
- **手动拍照比对**：点击按钮抓取当前帧进行单次比对
- **相似度排名**：按相似度降序排列，清晰展示匹配结果
- **同一人判定**：相似度 ≥ 0.6 自动标记为同一人，绿色高亮
- **灵活比对库**：支持 jpg/jpeg/png/bmp 格式，可随时切换图片目录
- **批量测试脚本**：`batch_test.ps1` 生成比对库图片间的人脸相似度矩阵

## 界面预览

```
┌─────────────────────────────────────────────────────────┐
│  [Start Camera] [Stop Camera] [Capture] [Realtime]  ... │
├──────────────────────┬──────────────────────────────────┤
│                      │  排名  图片      相似度   判定    │
│     摄像头画面       │   1   1.jpg    0.9234  ★ 同一人  │
│                      │   2   3.jpg    0.7156  ★ 同一人  │
│                      │   3   2.jpg    0.4512  不同人    │
│                      │  ...                            │
├──────────────────────┴──────────────────────────────────┤
│  就绪  | 比对库: 3/3 张已注册  | ★ 最佳匹配: 1.jpg (0.9234) │
└─────────────────────────────────────────────────────────┘
```

## 技术栈

| 组件 | 版本 | 用途 |
|------|------|------|
| .NET | 9.0 | 运行时框架 |
| Windows Forms | - | 桌面 GUI |
| [ViewFaceCore](https://www.nuget.org/packages/ViewFaceCore) | 0.3.7 | SeetaFace6 人脸识别引擎 |
| [ViewFaceCore.all_models](https://www.nuget.org/packages/ViewFaceCore.all_models) | 6.0.7 | 全部人脸模型（检测/关键点/识别/活体/属性等） |
| [ViewFaceCore.Extension.SkiaSharp](https://www.nuget.org/packages/ViewFaceCore.Extension.SkiaSharp) | 0.3.7 | SkiaSharp 图像扩展 |
| [OpenCvSharp4.Windows](https://www.nuget.org/packages/OpenCvSharp4.Windows) | 4.10.0 | 摄像头采集与图像处理 |

### 使用的模型

| 模型文件 | 功能 |
|----------|------|
| `face_detector.csta` | 人脸检测 |
| `face_landmarker_pts5.csta` | 5点关键点定位 |
| `face_landmarker_pts68.csta` | 68点关键点定位 |
| `face_recognizer.csta` | 人脸识别（高精度） |
| `face_recognizer_light.csta` | 人脸识别（轻量） |
| `face_recognizer_mask.csta` | 口罩人脸识别 |
| `face_landmarker_mask_pts5.csta` | 口罩人脸关键点 |
| `mask_detector.csta` | 口罩检测 |
| `age_predictor.csta` | 年龄预测 |
| `gender_predictor.csta` | 性别预测 |
| `eye_state.csta` | 眼睛状态检测 |
| `pose_estimation.csta` | 姿态估计 |
| `fas_first.csta` / `fas_second.csta` | 活体检测 |
| `quality_lbn.csta` | 质量评估 |

## 环境要求

- **操作系统**：Windows 10/11 (x64)
- **.NET SDK**：.NET 9.0 或更高版本
- **摄像头**：USB 摄像头或内置摄像头
- **Visual Studio**：2022 或更高版本（推荐），也可使用 VS Code / Rider

## 快速开始

### 1. 克隆项目

```bash
git clone https://github.com/你的用户名/ViewFaceDemo.git
cd ViewFaceDemo
```

### 2. 编译运行

```bash
dotnet restore
dotnet build
dotnet run
```

或在 Visual Studio 中打开 `ViewFaceDemo.csproj` 直接 F5 运行。

### 3. 准备比对图片

将需要比对的人脸照片放入 `test-images/` 目录（支持 jpg、jpeg、png、bmp 格式）。程序启动时会自动加载该目录下的图片。

也可以点击界面上的 **"Load Images"** 按钮切换到其他目录。

### 4. 使用流程

1. 点击 **Start Camera** 启动摄像头
2. 点击 **Capture** 拍照比对，或点击 **Realtime OFF** 切换到实时比对模式
3. 右侧表格实时显示比对结果

## 人脸相似度阈值

- **≥ 0.6**：判定为同一人（绿色高亮 ★）
- **< 0.6**：判定为不同人

阈值可在 `MainForm.cs` 中修改 `SamePersonThreshold` 常量：

```csharp
private const float SamePersonThreshold = 0.6f;
```

## 批量测试

使用 `batch_test.ps1` 脚本可以生成比对库中所有图片之间的人脸相似度矩阵：

```powershell
.\batch_test.ps1
```

结果保存在 `result.txt`，格式如下：

```
===== Face Similarity Matrix (3x3) =====
         1    2    3
     ----------------
   1|  ---  0.92 0.45
   2|       ---  0.48
   3|            ---

Threshold: >= 0.6 = same person
```

## 项目结构

```
ViewFaceDemo/
├── MainForm.cs              # 主窗体（UI + 核心逻辑）
├── Program.cs               # 程序入口
├── ViewFaceDemo.csproj      # 项目文件
├── nuget.config             # NuGet 源配置（含本地离线包）
├── batch_test.ps1           # 批量测试脚本
├── test-images/             # 比对图片库
│   ├── 1.jpg
│   ├── 2.jpg
│   └── 3.jpg
├── local-packages/          # 本地 NuGet 离线包
└── .gitignore
```

## 常见问题

**Q: 运行时提示找不到 DLL？**

A: 确保以 x64 模式编译运行，ViewFaceCore 的 native DLL 仅支持 Windows x64。

**Q: 摄像头打不开？**

A: 检查摄像头是否被其他应用占用，或修改 `CameraIndex` 常量尝试其他摄像头索引。

**Q: NuGet 包还原失败？**

A: 项目配置了本地离线包源（`local-packages/`），如果本地包不可用，可修改 `nuget.config` 移除 `<add key="local-packages" .../>` 行，改为从 nuget.org 在线下载。

## 许可证

本项目仅供学习和研究使用。ViewFaceCore（SeetaFace6）的使用需遵循其开源协议。

## 致谢

- [ViewFaceCore](https://github.com/ViewFaceCore/ViewFaceCore) - 人脸识别 .NET SDK
- [SeetaFace6](https://github.com/SeetaFace6Open/index) - 中科视拓人脸识别引擎
- [OpenCvSharp](https://github.com/shimat/opencvsharp) - OpenCV .NET 封装
- [SkiaSharp](https://github.com/mono/SkiaSharp) - 跨平台 2D 图形库