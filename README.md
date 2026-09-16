# 图片复判工具

面向检测结果图片的 Windows 桌面复判软件。支持递归扫描、结果图/原图对照、同步缩放和平移、双层标签、实时统计、多选过滤、自动保存及分类导出。

## 构建

要求：Windows、.NET 8 SDK、CMake 3.20+、Visual Studio 2022 C++ 工具链。

```powershell
cmake -S . -B build -G "Visual Studio 17 2022" -A x64
cmake --build build --config Release
```

也可仅构建并运行界面（没有原生 DLL 时会自动使用托管格式判断）：

```powershell
dotnet run --project src/ReviewApp/ReviewApp.csproj
```

运行核心流程冒烟测试：

```powershell
dotnet run --project tests/ReviewApp.SmokeTests/ReviewApp.SmokeTests.csproj -c Release
```

C++ 原生核心是可选扩展点，界面在 DLL 不存在时自动回退到同等的托管格式判断。安装 Visual Studio C++ 工具链后可启用：

```powershell
cmake -S . -B build-native -G "Visual Studio 17 2022" -A x64 -DBUILD_NATIVE_CORE=ON
cmake --build build-native --config Release
```

OpenCV 默认不需要；后续需要特殊格式解码或图像算法时可启用：

```powershell
cmake -S . -B build-native -G "Visual Studio 17 2022" -A x64 -DBUILD_NATIVE_CORE=ON -DUSE_OPENCV=ON -DOpenCV_DIR="D:/opencv-4.5.3/opencv-4.5.3/build/install/x64/vc16/lib"
```

## 使用

1. 选择结果文件夹，可选原图文件夹，设置扩展名后点击“打开”。
2. 原图优先按相对路径匹配；找不到时再按唯一文件名匹配。
3. 鼠标滚轮缩放，左键拖动画面；双击任一画面复位，两张图保持同一视角。
4. 标签修改后实时统计并自动保存到结果文件夹的 `.review-data.json`。
5. 左侧勾选标签用于过滤列表；导出默认使用当前过滤条件。

导出目录结构为 `缺陷标签/结果标签/result|original/原相对目录/原文件名`，因此图片文件名保持不变且避免同名冲突。
