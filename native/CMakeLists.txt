add_executable(hapi_abi_probe abi/probe.cpp)
target_link_libraries(hapi_abi_probe PRIVATE Houdini)
add_library(DXSharp_Passthrough SHARED hdk/SOP_Passthrough.cpp)
target_link_libraries(DXSharp_Passthrough PRIVATE Houdini)
# Stage the operator inside the build tree, not the user's Houdini profile.
houdini_configure_target(DXSharp_Passthrough INSTDIR "${CMAKE_BINARY_DIR}/stage/dso")
