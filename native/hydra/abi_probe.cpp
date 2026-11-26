// Compile against Houdini's own HDK/OpenUSD headers, not a separate USD SDK.
#include <pxr/pxr.h>
#include <pxr/imaging/hd/version.h>
#include <pxr/imaging/hd/renderDelegate.h>
#include <pxr/imaging/hd/renderBuffer.h>
#include <pxr/imaging/hd/rendererPlugin.h>
#include <iostream>
#include <type_traits>

int main() {
    std::cout << "{\"pxrVersion\":" << PXR_VERSION
              << ",\"hdApiVersion\":" << HD_API_VERSION
              << ",\"renderDelegateSize\":" << sizeof(PXR_NS::HdRenderDelegate)
              << ",\"renderBufferSize\":" << sizeof(PXR_NS::HdRenderBuffer)
              << ",\"rendererPluginSize\":" << sizeof(PXR_NS::HdRendererPlugin)
#if __has_include(<pxr/imaging/hd/sceneIndex.h>)
              << ",\"sceneIndexHeader\":true"
#else
              << ",\"sceneIndexHeader\":false"
#endif
              << "}\n";
    return 0;
}
