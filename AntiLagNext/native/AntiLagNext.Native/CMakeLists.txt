cmake_minimum_required(VERSION 3.16)
project(AntiLagNext.Native LANGUAGES CXX)

add_library(AntiLagNext.Native SHARED AntiLagNext.Native.cpp)
target_compile_features(AntiLagNext.Native PRIVATE cxx_std_17)
set_target_properties(AntiLagNext.Native PROPERTIES
  OUTPUT_NAME "AntiLagNext.Native"
  RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
)

if(MSVC)
  target_compile_options(AntiLagNext.Native PRIVATE /W3 /O2)
endif()
