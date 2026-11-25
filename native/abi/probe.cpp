#include <HAPI/HAPI.h>
#include <UT/UT_HDKVersion.h>
#include <cstddef>
#include <iostream>
#include <type_traits>

static_assert(HDK_API_VERSION == 22000475, "Unqualified HDK API");
static_assert(sizeof(HAPI_Bool) == 1);
static_assert(sizeof(HAPI_SessionId) == 8);

int main() {
    std::cout << "{\n"
        << "  \"hdkApi\": " << HDK_API_VERSION << ",\n"
        << "  \"boolSize\": " << sizeof(HAPI_Bool) << ",\n"
        << "  \"sessionSize\": " << sizeof(HAPI_Session) << ",\n"
        << "  \"sessionIdOffset\": " << offsetof(HAPI_Session, id) << ",\n"
        << "  \"sessionInfoSize\": " << sizeof(HAPI_SessionInfo) << ",\n"
        << "  \"sessionInfoPortsOffset\": " << offsetof(HAPI_SessionInfo, ports) << ",\n"
        << "  \"cookOptionsSize\": " << sizeof(HAPI_CookOptions) << ",\n"
        << "  \"partInfoSize\": " << sizeof(HAPI_PartInfo) << ",\n"
        << "  \"attributeInfoSize\": " << sizeof(HAPI_AttributeInfo) << "\n}\n";
}
