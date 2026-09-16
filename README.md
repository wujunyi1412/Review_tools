# 图片复判工具

面向检测结果图片的 Windows 桌面复判软件。支持递归扫描、结果图/原图对照、同步缩放和平移、单套检出标签、实时统计、多选过滤、自动保存及分类导出。

当前版本号显示在界面顶部右侧，统一由 `src/ReviewApp/ReviewApp.csproj` 的 `Version` 属性管理。

## 构建

开发机要求：Windows、.NET 8 SDK、CMake 3.20+、Visual Studio 2022 C++ 工具链。

双击 [build.bat](build.bat) 一键编译，程序位于 `artifacts/app/ImageReviewTool.exe`。

双击 [package.bat](package.bat) 一键发布，输出位于 `artifacts/releases/`。发布脚本生成 Windows x64 自包含版本及 ZIP；目标电脑无需预装 .NET。C++ 核心使用静态 MSVC 运行库。首次发布可能需要联网下载 .NET 运行时包。每次发布会创建带时间戳的新目录，不覆盖旧包。

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

1. 在左侧选择结果文件夹、可选原图文件夹，勾选要扫描的图片格式后点击“打开文件夹”。
2. 原图优先按相对路径匹配；找不到时再按唯一文件名匹配。
3. 鼠标滚轮缩放，左键拖动画面；双击任一画面复位，两张图保持同一视角。
4. 右侧单击检出标签即可标注，默认自动跳到下一张；可取消“标注后自动跳转”。修改后实时统计并自动保存到结果文件夹的 `.review-data.json`。
5. 顶部实时显示全部、当前过滤及各检出标签数量；左侧勾选标签会立即过滤列表。图片列表显示“当前序号 / 过滤后总数”，导出默认使用当前过滤条件。

右侧可新增、删除自定义标签。删除已用于图片的标签需要确认，相关图片会改为“待定”；内置标签不可删除。未打开结果目录时添加的标签仅在内存中，首次打开目录后会带入并保存。

导出目录结构为 `检出标签/result|original/原相对目录/原文件名`，因此图片文件名保持不变且避免同名冲突。
