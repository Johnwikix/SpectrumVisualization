#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#define _WINDOWS
#include <windows.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <wrl/client.h>
#include <string>
#include <memory>
#include <stdexcept>
#include <cstring>
#include <cmath>
#include <limits>
#include <xess/xess_d3d12.h>
#include <ffx_api_loader.h>
#include <dx12/ffx_api_dx12.h>
#include <ffx_upscale.h>
#include <nvsdk_ngx_helpers.h>
#include <nvsdk_ngx_helpers_d3d.h>

using Microsoft::WRL::ComPtr;
#define API extern "C" __declspec(dllexport)

// C ABI: all resources are borrowed. The caller drains its queue before destroying a context.
struct Frame
{
    ID3D12Resource* color;
    ID3D12Resource* depth;
    ID3D12Resource* motion;
    ID3D12Resource* reactive;
    ID3D12Resource* output;
    uint32_t width, height;
    float jitterX, jitterY, milliseconds;
    uint32_t reset;
    float nearPlane, farPlane, verticalFieldOfView, padding;
};
static_assert(sizeof(Frame) == 80);

static std::wstring Directory()
{
    HMODULE module = nullptr;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(&Directory), &module);
    wchar_t path[32768];
    auto length = GetModuleFileNameW(module, path, _countof(path));
    if (!length || length == _countof(path)) throw std::runtime_error("Module path unavailable");
    std::wstring result(path, length);
    return result.substr(0, result.find_last_of(L"\\/") + 1);
}
static HMODULE Load(const wchar_t* name)
{
    return LoadLibraryExW((Directory() + name).c_str(), nullptr,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
}
template<class T> static T Symbol(HMODULE module, const char* name)
{
    auto proc = GetProcAddress(module, name);
    if (!proc) throw std::runtime_error("SDK export unavailable");
    return reinterpret_cast<T>(proc);
}

struct Context
{
    int mode = 0;
    ID3D12Device* device = nullptr;
    HMODULE library = nullptr;
    xess_context_handle_t xess = nullptr;
    decltype(&xessDestroyContext) xessDestroy = nullptr;
    decltype(&xessD3D12Execute) xessExecute = nullptr;
    ffxFunctions ffx{};
    ffxContext fsr = nullptr;
    ffxCreateBackendDX12Desc backend{};
    ffxCreateContextDescUpscale fsrDesc{};
    ffxOverrideVersion version{};
    NVSDK_NGX_Parameter* parameters = nullptr;
    NVSDK_NGX_Handle* dlss = nullptr;
    bool ngx = false;
    std::wstring ngxDirectory;
    std::wstring ngxCache;
    const wchar_t* ngxPaths[1]{};
    NVSDK_NGX_FeatureCommonInfo ngxInfo{};
    uint32_t outputWidth = 0, outputHeight = 0;

    ~Context()
    {
        if (xess) xessDestroy(xess);
        if (fsr) ffx.DestroyContext(&fsr, nullptr);
        if (dlss) NVSDK_NGX_D3D12_ReleaseFeature(dlss);
        if (parameters) NVSDK_NGX_D3D12_DestroyParameters(parameters);
        if (ngx) NVSDK_NGX_D3D12_Shutdown1(device);
        if (library) FreeLibrary(library);
    }
};

static bool Nvidia(ID3D12Device* device)
{
    ComPtr<IDXGIFactory4> factory;
    ComPtr<IDXGIAdapter1> adapter;
    DXGI_ADAPTER_DESC1 desc{};
    return SUCCEEDED(CreateDXGIFactory1(IID_PPV_ARGS(&factory)))
        && SUCCEEDED(factory->EnumAdapterByLuid(device->GetAdapterLuid(), IID_PPV_ARGS(&adapter)))
        && SUCCEEDED(adapter->GetDesc1(&desc)) && desc.VendorId == 0x10DE;
}

static bool InitNgx(Context& context)
{
    if (!Nvidia(context.device)) return false;
    // A private project identifier, not an NVIDIA-issued application identifier.
    context.ngxDirectory = Directory();
    context.ngxPaths[0] = context.ngxDirectory.c_str();
    context.ngxInfo.PathListInfo.Path = context.ngxPaths;
    context.ngxInfo.PathListInfo.Length = 1;
    wchar_t cache[32768];
    if (!GetTempPathW(_countof(cache), cache)) return false;
    context.ngxCache = cache;
    auto result = NVSDK_NGX_D3D12_Init_with_ProjectID("8796023e-91c2-4811-8465-6b9756d1ef0a",
        NVSDK_NGX_ENGINE_TYPE_CUSTOM, "1.0", context.ngxCache.c_str(), context.device, &context.ngxInfo);
    if (NVSDK_NGX_FAILED(result)) return false;
    context.ngx = true;
    if (NVSDK_NGX_FAILED(NVSDK_NGX_D3D12_GetCapabilityParameters(&context.parameters))) return false;
    int available = 0;
    return NVSDK_NGX_SUCCEED(context.parameters->Get(NVSDK_NGX_Parameter_SuperSampling_Available, &available)) && available;
}

static bool InitFsr(Context& context)
{
    context.library = Load(L"amd_fidelityfx_loader_dx12.dll");
    if (!context.library) return false;
    ffxLoadFunctions(&context.ffx, context.library);
    if (!context.ffx.CreateContext || !context.ffx.DestroyContext || !context.ffx.Query || !context.ffx.Dispatch) return false;
    uint64_t count = 16, ids[16]{};
    const char* names[16]{};
    ffxQueryDescGetVersions query{};
    query.header.type = FFX_API_QUERY_DESC_TYPE_GET_VERSIONS;
    query.createDescType = FFX_API_CREATE_CONTEXT_DESC_TYPE_UPSCALE;
    query.device = context.device;
    query.outputCount = &count;
    query.versionIds = ids;
    query.versionNames = names;
    if (context.ffx.Query(nullptr, &query.header) != FFX_API_RETURN_OK) return false;
    for (uint64_t i = 0; i < count && i < 16; i++)
    {
        if (names[i] && std::strstr(names[i], "3.1"))
        {
            context.version.header.type = FFX_API_DESC_TYPE_OVERRIDE_VERSION;
            context.version.versionId = ids[i];
            return true;
        }
    }
    return false;
}

API uint32_t ReconstructionCapabilities(ID3D12Device* device) noexcept
{
    if (!device) return 0;
    uint32_t bits = 0;
    try
    {
        Context x; x.device = device;
        x.library = Load(L"libxess.dll");
        if (x.library)
        {
            x.xessDestroy = Symbol<decltype(x.xessDestroy)>(x.library, "xessDestroyContext");
            auto create = Symbol<decltype(&xessD3D12CreateContext)>(x.library, "xessD3D12CreateContext");
            if (create(device, &x.xess) >= 0) bits |= 1u << 3;
        }
    } catch (...) {}
    try { Context f; f.device = device; if (InitFsr(f)) bits |= 1u << 4; } catch (...) {}
    try { Context n; n.device = device; if (InitNgx(n)) bits |= 1u << 5; } catch (...) {}
    return bits;
}

// V2: the host owns the exact input size; quality enums are internal SDK initialization hints.
// -30 means this input/output pair is unsupported, not that the device lacks the provider.
API int ReconstructionCreateV2(ID3D12Device* device, ID3D12GraphicsCommandList* commands,
    int mode, int preset, uint32_t width, uint32_t height, uint32_t inputWidth, uint32_t inputHeight, Context** output) noexcept
{
    if (!device || !commands || !output || !inputWidth || !inputHeight || !width || !height || inputWidth > width || inputHeight > height) return -1;
    *output = nullptr;
    try
    {
        auto ctx = std::make_unique<Context>();
        ctx->device = device; ctx->mode = mode; ctx->outputWidth = width; ctx->outputHeight = height;
        if (mode == 3)
        {
            ctx->library = Load(L"libxess.dll");
            if (!ctx->library) return -2;
            ctx->xessDestroy = Symbol<decltype(ctx->xessDestroy)>(ctx->library, "xessDestroyContext");
            auto create = Symbol<decltype(&xessD3D12CreateContext)>(ctx->library, "xessD3D12CreateContext");
            auto init = Symbol<decltype(&xessD3D12Init)>(ctx->library, "xessD3D12Init");
            auto resolution = Symbol<decltype(&xessGetOptimalInputResolution)>(ctx->library, "xessGetOptimalInputResolution");
            ctx->xessExecute = Symbol<decltype(ctx->xessExecute)>(ctx->library, "xessD3D12Execute");
            if (create(device, &ctx->xess) < 0) return -3;
            const xess_quality_settings_t qualities[] = { XESS_QUALITY_SETTING_AA, XESS_QUALITY_SETTING_ULTRA_QUALITY_PLUS,
                XESS_QUALITY_SETTING_ULTRA_QUALITY, XESS_QUALITY_SETTING_QUALITY, XESS_QUALITY_SETTING_BALANCED,
                XESS_QUALITY_SETTING_PERFORMANCE, XESS_QUALITY_SETTING_ULTRA_PERFORMANCE };
            xess_d3d12_init_params_t desc{};
            desc.outputResolution = { width, height };
            double best = std::numeric_limits<double>::max();
            for (auto quality : qualities)
            {
                xess_2d_t optimal{}, minimum{}, maximum{};
                if (resolution(ctx->xess, &desc.outputResolution, quality, &optimal, &minimum, &maximum) < 0) continue;
                // Use the recommendation to select a model, not to reject a fixed input size.
                // Execute receives the actual dimensions; its return value is authoritative.
                double distance = std::abs(double(optimal.x) - inputWidth) + std::abs(double(optimal.y) - inputHeight);
                if (distance < best) { best = distance; desc.qualitySetting = quality; }
            }
            if (best == std::numeric_limits<double>::max()) return -30;
            desc.initFlags = XESS_INIT_FLAG_RESPONSIVE_PIXEL_MASK;
            desc.creationNodeMask = desc.visibleNodeMask = 1;
            if (init(ctx->xess, &desc) < 0) return -5;
        }
        else if (mode == 4)
        {
            if (!InitFsr(*ctx)) return -6;
            ctx->backend.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_BACKEND_DX12;
            ctx->backend.device = device;
            ctx->backend.header.pNext = &ctx->version.header;
            ctx->fsrDesc.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_UPSCALE;
            ctx->fsrDesc.header.pNext = &ctx->backend.header;
            ctx->fsrDesc.flags = FFX_UPSCALE_ENABLE_HIGH_DYNAMIC_RANGE;
            ctx->fsrDesc.maxRenderSize = { inputWidth, inputHeight };
            ctx->fsrDesc.maxUpscaleSize = { width, height };
            if (ctx->ffx.CreateContext(&ctx->fsr, &ctx->fsrDesc.header, nullptr) != FFX_API_RETURN_OK) return -8;
        }
        else if (mode == 5)
        {
            if (!InitNgx(*ctx)) return -9;
            if (width < 32 || height < 32) return -30;
            if (preset < NVSDK_NGX_DLSS_Hint_Render_Preset_J || preset > NVSDK_NGX_DLSS_Hint_Render_Preset_M) return -1;
            // Set every quality slot: the chosen model remains explicit when the input ratio changes.
            for (auto key : { NVSDK_NGX_Parameter_DLSS_Hint_Render_Preset_DLAA, NVSDK_NGX_Parameter_DLSS_Hint_Render_Preset_Quality,
                NVSDK_NGX_Parameter_DLSS_Hint_Render_Preset_Balanced, NVSDK_NGX_Parameter_DLSS_Hint_Render_Preset_Performance,
                NVSDK_NGX_Parameter_DLSS_Hint_Render_Preset_UltraPerformance })
                ctx->parameters->Set(key, static_cast<unsigned int>(preset));
            const NVSDK_NGX_PerfQuality_Value qualities[] = { NVSDK_NGX_PerfQuality_Value_DLAA, NVSDK_NGX_PerfQuality_Value_MaxQuality,
                NVSDK_NGX_PerfQuality_Value_Balanced, NVSDK_NGX_PerfQuality_Value_MaxPerf, NVSDK_NGX_PerfQuality_Value_UltraPerformance };
            NVSDK_NGX_PerfQuality_Value selected = NVSDK_NGX_PerfQuality_Value_MaxPerf;
            double best = std::numeric_limits<double>::max();
            for (auto quality : qualities)
            {
                uint32_t optimalW, optimalH, maxW, maxH, minW, minH; float sharpness;
                if (NVSDK_NGX_FAILED(NGX_DLSS_GET_OPTIMAL_SETTINGS(ctx->parameters, width, height, quality, &optimalW, &optimalH,
                    &maxW, &maxH, &minW, &minH, &sharpness))) continue;
                double distance = std::abs(double(optimalW) - inputWidth) + std::abs(double(optimalH) - inputHeight);
                if (distance < best)
                {
                    best = distance;
                    selected = quality;
                }
            }
            NVSDK_NGX_DLSS_Create_Params desc{};
            // A scale change recreates the feature: its input size is fixed for its lifetime.
            // The query's min/max describe DRS within a recommended feature configuration,
            // not a veto on custom fixed input sizes (UltraPerformance reports min == max).
            desc.Feature.InWidth = inputWidth; desc.Feature.InHeight = inputHeight;
            desc.Feature.InTargetWidth = width; desc.Feature.InTargetHeight = height;
            desc.Feature.InPerfQualityValue = selected;
            desc.InFeatureCreateFlags = NVSDK_NGX_DLSS_Feature_Flags_IsHDR | NVSDK_NGX_DLSS_Feature_Flags_MVLowRes;
            auto result = NGX_D3D12_CREATE_DLSS_EXT(commands, 1, 1, &ctx->dlss, ctx->parameters, &desc);
            if (result == NVSDK_NGX_Result_FAIL_InvalidParameter) return -30;
            if (NVSDK_NGX_FAILED(result)) return -11;
        }
        else return -12;
        *output = ctx.release();
        return 0;
    }
    catch (...) { return -100; }
}

API int ReconstructionExecute(Context* ctx, ID3D12GraphicsCommandList* commands, const Frame* frame) noexcept
{
    if (!ctx || !commands || !frame) return -1;
    try
    {
        const auto& f = *frame;
        if (ctx->mode == 3)
        {
            xess_d3d12_execute_params_t args{};
            args.pColorTexture = f.color; args.pDepthTexture = f.depth; args.pVelocityTexture = f.motion;
            args.pResponsivePixelMaskTexture = f.reactive; args.pOutputTexture = f.output;
            args.inputWidth = f.width; args.inputHeight = f.height;
            args.jitterOffsetX = f.jitterX; args.jitterOffsetY = f.jitterY;
            args.exposureScale = 1; args.resetHistory = f.reset;
            return ctx->xessExecute(ctx->xess, commands, &args) < 0 ? -20 : 0;
        }
        if (ctx->mode == 4)
        {
            ffxDispatchDescUpscale args{};
            args.header.type = FFX_API_DISPATCH_DESC_TYPE_UPSCALE;
            args.commandList = commands;
            args.color = ffxApiGetResourceDX12(f.color);
            args.depth = ffxApiGetResourceDX12(f.depth);
            args.depth.description.format = FFX_API_SURFACE_FORMAT_R32_FLOAT;
            args.motionVectors = ffxApiGetResourceDX12(f.motion);
            args.reactive = ffxApiGetResourceDX12(f.reactive);
            args.output = ffxApiGetResourceDX12(f.output, FFX_API_RESOURCE_STATE_UNORDERED_ACCESS);
            args.jitterOffset = { f.jitterX, f.jitterY };
            args.motionVectorScale = { 1, 1 };
            args.renderSize = { f.width, f.height };
            args.upscaleSize = { ctx->outputWidth, ctx->outputHeight };
            args.frameTimeDelta = f.milliseconds; args.preExposure = 1; args.reset = f.reset != 0;
            args.cameraNear = f.nearPlane; args.cameraFar = f.farPlane; args.cameraFovAngleVertical = f.verticalFieldOfView;
            args.viewSpaceToMetersFactor = 1;
            return ctx->ffx.Dispatch(&ctx->fsr, &args.header) == FFX_API_RETURN_OK ? 0 : -21;
        }
        NVSDK_NGX_D3D12_DLSS_Eval_Params args{};
        args.Feature.pInColor = f.color; args.Feature.pInOutput = f.output;
        args.pInDepth = f.depth; args.pInMotionVectors = f.motion; args.pInBiasCurrentColorMask = f.reactive;
        args.InJitterOffsetX = f.jitterX; args.InJitterOffsetY = f.jitterY;
        args.InRenderSubrectDimensions = { f.width, f.height };
        args.InReset = f.reset; args.InMVScaleX = args.InMVScaleY = 1;
        args.InPreExposure = args.InExposureScale = 1; args.InFrameTimeDeltaInMsec = f.milliseconds;
        return NVSDK_NGX_SUCCEED(NGX_D3D12_EVALUATE_DLSS_EXT(commands, ctx->dlss, ctx->parameters, &args)) ? 0 : -22;
    }
    catch (...) { return -100; }
}

API void ReconstructionDestroy(Context* context) noexcept { delete context; }
