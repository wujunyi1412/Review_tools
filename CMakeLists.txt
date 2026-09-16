cmake_minimum_required(VERSION 3.20)
project(ImageReviewTool LANGUAGES NONE)

option(BUILD_WPF_APP "Build the WPF application" ON)
option(BUILD_NATIVE_CORE "Build the optional C++ native extension" OFF)
option(USE_OPENCV "Enable OpenCV in the native extension" OFF)

if(BUILD_NATIVE_CORE)
  enable_language(CXX)
  add_subdirectory(src/ReviewCore)
endif()

if(BUILD_WPF_APP)
  find_program(DOTNET_EXECUTABLE dotnet REQUIRED)
  set(WPF_PROJECT "${CMAKE_SOURCE_DIR}/src/ReviewApp/ReviewApp.csproj")
  if(BUILD_NATIVE_CORE)
    add_custom_target(ReviewApp ALL
      COMMAND "${DOTNET_EXECUTABLE}" build "${WPF_PROJECT}" -c $<IF:$<CONFIG:Debug>,Debug,Release>
      COMMAND "${CMAKE_COMMAND}" -E copy_if_different "$<TARGET_FILE:ReviewCore>"
              "${CMAKE_SOURCE_DIR}/src/ReviewApp/bin/$<IF:$<CONFIG:Debug>,Debug,Release>/net8.0-windows/"
      WORKING_DIRECTORY "${CMAKE_SOURCE_DIR}"
      COMMENT "Building WPF review application")
    add_dependencies(ReviewApp ReviewCore)
  else()
    add_custom_target(ReviewApp ALL
      COMMAND "${DOTNET_EXECUTABLE}" build "${WPF_PROJECT}" -c $<IF:$<CONFIG:Debug>,Debug,Release>
      WORKING_DIRECTORY "${CMAKE_SOURCE_DIR}"
      COMMENT "Building WPF review application")
  endif()
endif()
