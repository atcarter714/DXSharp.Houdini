add_executable(hapi_abi_probe abi/probe.cpp)
target_link_libraries(hapi_abi_probe PRIVATE Houdini)
add_library(DXSharp_Passthrough SHARED hdk/SOP_Passthrough.cpp)
target_link_libraries(DXSharp_Passthrough PRIVATE Houdini)
# Stage the operator inside the build tree, not the user's Houdini profile.
houdini_configure_target(DXSharp_Passthrough INSTDIR "${CMAKE_BINARY_DIR}/stage/dso")

# Opt-in: no installation or registration of an unqualified renderer.
option(DXSHARP_HYDRA_PROBE "Compile Houdini/OpenUSD Hydra ABI probe" OFF)
if(DXSHARP_HYDRA_PROBE)
  add_executable(hydra_abi_probe hydra/abi_probe.cpp)
  target_compile_features(hydra_abi_probe PRIVATE cxx_std_17)
  target_link_libraries(hydra_abi_probe PRIVATE Houdini)
endif()
