# Builds edo9300/ygopro-core as a Unity native plugin.
# Clone first: ThirdParty/OcgCore/ygopro-core
#
#   cmake -S Native/OcgCorePlugin -B Native/OcgCorePlugin/build -DBUILD_SHARED_LIBS=ON
# Linux:  copy libocgcore.so  → Assets/Plugins/x86_64/
# Windows: copy ocgcore.dll   → Assets/Plugins/x86_64/

cmake_minimum_required(VERSION 3.16)
project(ocgcore_unity C CXX)
set(CMAKE_CXX_STANDARD 17)
add_compile_definitions(OCGCORE_EXPORT_FUNCTIONS)
set(CORE_DIR "${CMAKE_CURRENT_LIST_DIR}/../../ThirdParty/OcgCore/ygopro-core")
if(NOT EXISTS "${CORE_DIR}/ocgapi.h")
  message(FATAL_ERROR "Missing ${CORE_DIR}/ocgapi.h — clone edo9300/ygopro-core")
endif()
file(GLOB CORE_SRC "${CORE_DIR}/*.cpp")
add_library(ocgcore SHARED ${CORE_SRC})
target_include_directories(ocgcore PUBLIC "${CORE_DIR}")
