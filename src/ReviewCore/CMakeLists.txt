add_library(ReviewCore SHARED ReviewCore.cpp)
target_compile_features(ReviewCore PRIVATE cxx_std_17)
target_compile_definitions(ReviewCore PRIVATE REVIEWCORE_EXPORTS)

if(USE_OPENCV)
  find_package(OpenCV 4.5 REQUIRED COMPONENTS core imgcodecs)
  target_link_libraries(ReviewCore PRIVATE ${OpenCV_LIBS})
  target_compile_definitions(ReviewCore PRIVATE REVIEWCORE_WITH_OPENCV)
endif()

if(MSVC)
  target_compile_options(ReviewCore PRIVATE /W4 /utf-8)
endif()
