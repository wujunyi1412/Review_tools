#include <algorithm>
#include <cctype>
#include <string>
#include <unordered_set>

#ifdef _WIN32
#define REVIEW_API extern "C" __declspec(dllexport)
#else
#define REVIEW_API extern "C"
#endif

REVIEW_API int review_core_api_version() { return 1; }

REVIEW_API int review_core_is_supported_extension(const char* extension) {
    if (!extension) return 0;
    std::string value(extension);
    std::transform(value.begin(), value.end(), value.begin(),
                   [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
    if (!value.empty() && value.front() != '.') value.insert(value.begin(), '.');
    static const std::unordered_set<std::string> supported{
        ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".gif"
    };
    return supported.count(value) ? 1 : 0;
}
