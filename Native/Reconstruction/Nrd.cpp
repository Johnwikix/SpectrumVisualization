#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d12.h>
#include <wrl/client.h>
#include <NRD.h>
#include <vector>
#include <memory>
#include <string>
#include <stdexcept>
#include <cstring>
#include <cstdio>
#include <algorithm>

using Microsoft::WRL::ComPtr;
#define API extern "C" __declspec(dllexport)
namespace {
thread_local char diagnostic[512]{};
constexpr nrd::Identifier denoiserId = 1;
constexpr auto readState = D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE;
void ModuleAnchor() {}

void Check(HRESULT result, const char* operation)
{
    if (FAILED(result)) {
        char text[160]; sprintf_s(text, "%s (0x%08X)", operation, unsigned(result));
        throw std::runtime_error(text);
    }
}
void Check(nrd::Result result, const char* operation)
{
    if (result != nrd::Result::SUCCESS) {
        char text[160]; sprintf_s(text, "%s (%u)", operation, unsigned(result));
        throw std::runtime_error(text);
    }
}
struct Module {
    HMODULE handle = nullptr;
    ~Module() { if (handle) FreeLibrary(handle); }
};
struct NrdApi {
    Module module;
    decltype(&nrd::GetLibraryDesc) library;
    decltype(&nrd::CreateInstance) create;
    decltype(&nrd::DestroyInstance) destroy;
    decltype(&nrd::GetInstanceDesc) description;
    decltype(&nrd::SetCommonSettings) common;
    decltype(&nrd::SetDenoiserSettings) settings;
    decltype(&nrd::GetComputeDispatches) dispatches;
    template<class T> void Load(T& target, const char* name) {
        target = reinterpret_cast<T>(GetProcAddress(module.handle, name));
        if (!target) throw std::runtime_error("NRD export missing");
    }
    NrdApi() {
        HMODULE bridge = nullptr;
        GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(&ModuleAnchor), &bridge);
        wchar_t path[32768];
        DWORD length = GetModuleFileNameW(bridge, path, _countof(path));
        if (!length || length == _countof(path)) throw std::runtime_error("NRD module path unavailable");
        std::wstring folder(path, length);
        folder.resize(folder.find_last_of(L"\\/") + 1);
        module.handle = LoadLibraryExW((folder + L"NRD.dll").c_str(), nullptr,
            LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        if (!module.handle) throw std::runtime_error("NRD.dll missing or incompatible");
        Load(library, "GetLibraryDesc"); Load(create, "CreateInstance"); Load(destroy, "DestroyInstance");
        Load(description, "GetInstanceDesc"); Load(common, "SetCommonSettings");
        Load(settings, "SetDenoiserSettings"); Load(dispatches, "GetComputeDispatches");
        const auto& desc = *library();
        if (desc.versionMajor != 4 || desc.versionMinor != 17 || desc.versionBuild != 3 ||
            desc.normalEncoding != nrd::NormalEncoding::RGBA16_SNORM || desc.roughnessEncoding != nrd::RoughnessEncoding::LINEAR)
            throw std::runtime_error("NRD version/encoding mismatch (requires 4.17.3, normal=4, roughness=1)");
    }
};

DXGI_FORMAT Format(nrd::Format format) {
    static constexpr DXGI_FORMAT formats[] = {
        DXGI_FORMAT_R8_UNORM, DXGI_FORMAT_R8_SNORM, DXGI_FORMAT_R8_UINT, DXGI_FORMAT_R8_SINT,
        DXGI_FORMAT_R8G8_UNORM, DXGI_FORMAT_R8G8_SNORM, DXGI_FORMAT_R8G8_UINT, DXGI_FORMAT_R8G8_SINT,
        DXGI_FORMAT_R8G8B8A8_UNORM, DXGI_FORMAT_R8G8B8A8_SNORM, DXGI_FORMAT_R8G8B8A8_UINT, DXGI_FORMAT_R8G8B8A8_SINT, DXGI_FORMAT_R8G8B8A8_UNORM_SRGB,
        DXGI_FORMAT_R16_UNORM, DXGI_FORMAT_R16_SNORM, DXGI_FORMAT_R16_UINT, DXGI_FORMAT_R16_SINT, DXGI_FORMAT_R16_FLOAT,
        DXGI_FORMAT_R16G16_UNORM, DXGI_FORMAT_R16G16_SNORM, DXGI_FORMAT_R16G16_UINT, DXGI_FORMAT_R16G16_SINT, DXGI_FORMAT_R16G16_FLOAT,
        DXGI_FORMAT_R16G16B16A16_UNORM, DXGI_FORMAT_R16G16B16A16_SNORM, DXGI_FORMAT_R16G16B16A16_UINT, DXGI_FORMAT_R16G16B16A16_SINT, DXGI_FORMAT_R16G16B16A16_FLOAT,
        DXGI_FORMAT_R32_UINT, DXGI_FORMAT_R32_SINT, DXGI_FORMAT_R32_FLOAT,
        DXGI_FORMAT_R32G32_UINT, DXGI_FORMAT_R32G32_SINT, DXGI_FORMAT_R32G32_FLOAT,
        DXGI_FORMAT_R32G32B32_UINT, DXGI_FORMAT_R32G32B32_SINT, DXGI_FORMAT_R32G32B32_FLOAT,
        DXGI_FORMAT_R32G32B32A32_UINT, DXGI_FORMAT_R32G32B32A32_SINT, DXGI_FORMAT_R32G32B32A32_FLOAT,
        DXGI_FORMAT_R10G10B10A2_UNORM, DXGI_FORMAT_R10G10B10A2_UINT, DXGI_FORMAT_R11G11B10_FLOAT, DXGI_FORMAT_R9G9B9E5_SHAREDEXP
    };
    static_assert(_countof(formats) == unsigned(nrd::Format::MAX_NUM));
    if (unsigned(format) >= _countof(formats)) throw std::runtime_error("Unknown NRD texture format");
    return formats[unsigned(format)];
}
struct Texture {
    ComPtr<ID3D12Resource> owned;
    ID3D12Resource* resource = nullptr;
    D3D12_RESOURCE_STATES state = readState;
    void Transition(ID3D12GraphicsCommandList* commands, D3D12_RESOURCE_STATES next) {
        D3D12_RESOURCE_BARRIER barrier{};
        if (state != next) {
            barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
            barrier.Transition = { resource, D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES, state, next };
        } else if (next == D3D12_RESOURCE_STATE_UNORDERED_ACCESS) {
            barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_UAV;
            barrier.UAV.pResource = resource;
        } else return;
        commands->ResourceBarrier(1, &barrier);
        state = next;
    }
};
struct Pipeline { ComPtr<ID3D12RootSignature> root; ComPtr<ID3D12PipelineState> state; };
}

// Borrowed textures enter and leave in NON_PIXEL_SHADER_RESOURCE. The host serializes frames.
struct NrdFrame {
    ID3D12Resource* color;
    ID3D12Resource* normalRoughness;
    ID3D12Resource* viewZ;
    ID3D12Resource* motion;
    ID3D12Resource* output;
    float worldToView[16], worldToViewPrev[16], viewToClip[16], viewToClipPrev[16];
    float jitter[2], jitterPrev[2];
    float milliseconds;
    uint32_t frameIndex, reset, frameSlot;
    ID3D12Resource* specular;
    ID3D12Resource* specularOutput;
    float denoisingRange;
    uint32_t maxAccumulatedFrames;
    ID3D12Resource* diffuseSh;
    ID3D12Resource* specularSh;
    ID3D12Resource* diffuseShOutput;
    ID3D12Resource* specularShOutput;
};
static_assert(sizeof(NrdFrame) == 384);

struct NrdContext {
    NrdApi api;
    nrd::Instance* instance = nullptr;
    ComPtr<ID3D12Device> device;
    std::vector<Texture> permanent, transient;
    std::vector<Pipeline> pipelines;
    ComPtr<ID3D12DescriptorHeap> heap;
    ComPtr<ID3D12Resource> constants;
    uint8_t* mapped = nullptr;
    uint32_t width = 0, height = 0, stride = 0, descriptorsPerSet = 0, constantStride = 0, maxSets = 0;
    uint32_t dispatchCount = 0, configuredMaxFrames = 0;
    bool sh = false, combined = false, settingsInitialized = false;
    bool sharedTransientPool = false;
    ~NrdContext() {
        if (mapped) constants->Unmap(0, nullptr);
        if (instance) api.destroy(*instance);
    }
    void Pool(std::vector<Texture>& pool, const nrd::TextureDesc* descriptions, uint32_t count) {
        pool.resize(count);
        for (uint32_t i = 0; i < count; i++) {
            auto& texture = pool[i]; const auto& desc = descriptions[i];
            D3D12_RESOURCE_DESC resource{};
            resource.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
            resource.Width = (width + desc.downsampleFactor - 1) / desc.downsampleFactor;
            resource.Height = (height + desc.downsampleFactor - 1) / desc.downsampleFactor;
            resource.DepthOrArraySize = resource.MipLevels = 1;
            resource.Format = Format(desc.format); resource.SampleDesc.Count = 1;
            resource.Flags = D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;
            D3D12_HEAP_PROPERTIES properties{}; properties.Type = D3D12_HEAP_TYPE_DEFAULT;
            Check(device->CreateCommittedResource(&properties, D3D12_HEAP_FLAG_NONE, &resource, readState, nullptr,
                IID_PPV_ARGS(&texture.owned)), "Create NRD texture");
            texture.resource = texture.owned.Get();
        }
    }
    void SetSettings(uint32_t maxFrames) {
        if (settingsInitialized && configuredMaxFrames == maxFrames) return;
        nrd::RelaxSettings settings{};
        settings.enableAntiFirefly = true;
        if (maxFrames) {
            settings.diffuseMaxAccumulatedFrameNum = maxFrames;
            settings.specularMaxAccumulatedFrameNum = maxFrames;
            settings.diffuseMaxFastAccumulatedFrameNum = std::min(settings.diffuseMaxFastAccumulatedFrameNum, maxFrames - 1);
            settings.specularMaxFastAccumulatedFrameNum = std::min(settings.specularMaxFastAccumulatedFrameNum, maxFrames - 1);
        }
        if (combined) settings.hitDistanceReconstructionMode = nrd::HitDistanceReconstructionMode::AREA_3X3;
        Check(api.settings(*instance, denoiserId, &settings), "Configure NRD RELAX");
        configuredMaxFrames = maxFrames; settingsInitialized = true;
    }
    void Initialize(ID3D12Device* gpu, uint32_t w, uint32_t h, bool pbr = false, bool radiancePbr = false,
        NrdContext* opticalPeer = nullptr) {
        device = gpu; width = w; height = h; sh = pbr; combined = pbr || radiancePbr;
        nrd::DenoiserDesc denoiser{denoiserId, sh ? nrd::Denoiser::RELAX_DIFFUSE_SPECULAR_SH : combined ? nrd::Denoiser::RELAX_DIFFUSE_SPECULAR : nrd::Denoiser::RELAX_DIFFUSE};
        nrd::InstanceCreationDesc creation{}; creation.denoisers = &denoiser; creation.denoisersNum = 1;
        Check(api.create(creation, instance), "Create NRD RELAX_DIFFUSE");
        SetSettings(0);
        const auto& desc = *api.description(*instance);
        Pool(permanent, desc.permanentPool, desc.permanentPoolSize);
        if (opticalPeer) {
            if (opticalPeer->transient.size() != desc.transientPoolSize || opticalPeer->pipelines.size() != desc.pipelinesNum)
                throw std::runtime_error("NRD optical contexts have different dispatch layouts");
            transient = opticalPeer->transient;
            sharedTransientPool = opticalPeer->sharedTransientPool = true;
        } else {
            Pool(transient, desc.transientPool, desc.transientPoolSize);
        }
        maxSets = desc.descriptorPoolDesc.setsMaxNum;
        descriptorsPerSet = desc.descriptorPoolDesc.perSetTexturesMaxNum + desc.descriptorPoolDesc.perSetStorageTexturesMaxNum;
        D3D12_DESCRIPTOR_HEAP_DESC heapDesc{D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV, maxSets * descriptorsPerSet * 2, D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE, 0};
        Check(device->CreateDescriptorHeap(&heapDesc, IID_PPV_ARGS(&heap)), "Create NRD descriptors");
        stride = device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);
        constantStride = (desc.constantBufferMaxDataSize + 255) & ~255u;
        D3D12_RESOURCE_DESC buffer{};
        buffer.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER; buffer.Width = uint64_t(constantStride) * maxSets * 2;
        buffer.Height = buffer.DepthOrArraySize = buffer.MipLevels = 1; buffer.SampleDesc.Count = 1;
        buffer.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
        D3D12_HEAP_PROPERTIES upload{}; upload.Type = D3D12_HEAP_TYPE_UPLOAD;
        Check(device->CreateCommittedResource(&upload, D3D12_HEAP_FLAG_NONE, &buffer, D3D12_RESOURCE_STATE_GENERIC_READ,
            nullptr, IID_PPV_ARGS(&constants)), "Create NRD constants");
        D3D12_RANGE empty{};
        Check(constants->Map(0, &empty, reinterpret_cast<void**>(&mapped)), "Map NRD constants");
        memset(mapped, 0, size_t(buffer.Width));
        if (opticalPeer) {
            pipelines = opticalPeer->pipelines;
            return;
        }
        pipelines.resize(desc.pipelinesNum);
        for (uint32_t i = 0; i < desc.pipelinesNum; i++) {
            auto& pipeline = pipelines[i]; const auto& p = desc.pipelines[i];
            std::vector<D3D12_DESCRIPTOR_RANGE> ranges(p.resourceRangesNum);
            for (uint32_t j = 0; j < p.resourceRangesNum; j++)
                ranges[j] = { p.resourceRanges[j].descriptorType == nrd::DescriptorType::TEXTURE ? D3D12_DESCRIPTOR_RANGE_TYPE_SRV : D3D12_DESCRIPTOR_RANGE_TYPE_UAV,
                    p.resourceRanges[j].descriptorsNum, desc.resourcesBaseRegisterIndex, desc.resourcesSpaceIndex, D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND };
            D3D12_ROOT_PARAMETER parameters[2]{};
            parameters[0].ParameterType = D3D12_ROOT_PARAMETER_TYPE_CBV;
            parameters[0].Descriptor = {desc.constantBufferRegisterIndex, desc.constantBufferAndSamplersSpaceIndex};
            parameters[1].ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
            parameters[1].DescriptorTable = {uint32_t(ranges.size()), ranges.data()};
            std::vector<D3D12_STATIC_SAMPLER_DESC> samplers(desc.samplersNum);
            for (uint32_t j = 0; j < desc.samplersNum; j++) {
                auto& sampler = samplers[j];
                sampler.Filter = desc.samplers[j] == nrd::Sampler::LINEAR_CLAMP ? D3D12_FILTER_MIN_MAG_MIP_LINEAR : D3D12_FILTER_MIN_MAG_MIP_POINT;
                sampler.AddressU = sampler.AddressV = sampler.AddressW = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
                sampler.MaxLOD = D3D12_FLOAT32_MAX; sampler.MaxAnisotropy = 1;
                sampler.ComparisonFunc = D3D12_COMPARISON_FUNC_ALWAYS;
                sampler.ShaderRegister = desc.samplersBaseRegisterIndex + j;
                sampler.RegisterSpace = desc.constantBufferAndSamplersSpaceIndex;
            }
            D3D12_ROOT_SIGNATURE_DESC root{2, parameters, uint32_t(samplers.size()), samplers.data(), D3D12_ROOT_SIGNATURE_FLAG_NONE};
            ComPtr<ID3DBlob> blob, errors;
            Check(D3D12SerializeRootSignature(&root, D3D_ROOT_SIGNATURE_VERSION_1, &blob, &errors), "Serialize NRD root signature");
            Check(device->CreateRootSignature(0, blob->GetBufferPointer(), blob->GetBufferSize(), IID_PPV_ARGS(&pipeline.root)), "Create NRD root signature");
            D3D12_COMPUTE_PIPELINE_STATE_DESC state{};
            state.pRootSignature = pipeline.root.Get(); state.CS = {p.computeShaderDXIL.bytecode, size_t(p.computeShaderDXIL.size)};
            Check(device->CreateComputePipelineState(&state, IID_PPV_ARGS(&pipeline.state)), "Create NRD compute pipeline");
        }
    }
    void Execute(ID3D12GraphicsCommandList* commands, const NrdFrame& frame) {
        if (frame.frameSlot >= 2) throw std::runtime_error("Invalid NRD frame slot");
        SetSettings(frame.maxAccumulatedFrames);
        nrd::CommonSettings common{};
        memcpy(common.worldToViewMatrix, frame.worldToView, 64); memcpy(common.worldToViewMatrixPrev, frame.worldToViewPrev, 64);
        memcpy(common.viewToClipMatrix, frame.viewToClip, 64); memcpy(common.viewToClipMatrixPrev, frame.viewToClipPrev, 64);
        memcpy(common.cameraJitter, frame.jitter, 8); memcpy(common.cameraJitterPrev, frame.jitterPrev, 8);
        common.resourceSize[0] = common.resourceSizePrev[0] = common.rectSize[0] = common.rectSizePrev[0] = uint16_t(width);
        common.resourceSize[1] = common.resourceSizePrev[1] = common.rectSize[1] = common.rectSizePrev[1] = uint16_t(height);
        common.motionVectorScale[0] = 1.0f / width; common.motionVectorScale[1] = 1.0f / height;
        common.frameIndex = frame.frameIndex; common.timeDeltaBetweenFrames = frame.milliseconds;
        common.denoisingRange = frame.denoisingRange > 0 ? frame.denoisingRange : 1000;
        common.accumulationMode = frame.reset ? nrd::AccumulationMode::CLEAR_AND_RESTART : nrd::AccumulationMode::CONTINUE;
        Check(api.common(*instance, common), "Set NRD camera settings");
        const nrd::DispatchDesc* dispatches = nullptr;
        Check(api.dispatches(*instance, &denoiserId, 1, dispatches, dispatchCount), "Get NRD dispatches");
        if (dispatchCount > maxSets) throw std::runtime_error("NRD descriptor capacity exceeded");
        Texture external[unsigned(nrd::ResourceType::MAX_NUM)]{};
        struct RestoreResources {
            NrdContext& context;
            ID3D12GraphicsCommandList* commands;
            decltype(external)& resources;
            ~RestoreResources() {
                for (auto& texture : resources) if (texture.resource) texture.Transition(commands, readState);
                if (context.sharedTransientPool)
                    for (auto& texture : context.transient) texture.Transition(commands, readState);
            }
        } restore{*this, commands, external};
        external[unsigned(sh ? nrd::ResourceType::IN_DIFF_SH0 : nrd::ResourceType::IN_DIFF_RADIANCE_HITDIST)].resource = frame.color;
        external[unsigned(nrd::ResourceType::IN_NORMAL_ROUGHNESS)].resource = frame.normalRoughness;
        external[unsigned(nrd::ResourceType::IN_VIEWZ)].resource = frame.viewZ;
        external[unsigned(nrd::ResourceType::IN_MV)].resource = frame.motion;
        external[unsigned(sh ? nrd::ResourceType::OUT_DIFF_SH0 : nrd::ResourceType::OUT_DIFF_RADIANCE_HITDIST)].resource = frame.output;
        external[unsigned(sh ? nrd::ResourceType::IN_SPEC_SH0 : nrd::ResourceType::IN_SPEC_RADIANCE_HITDIST)].resource = frame.specular;
        external[unsigned(sh ? nrd::ResourceType::OUT_SPEC_SH0 : nrd::ResourceType::OUT_SPEC_RADIANCE_HITDIST)].resource = frame.specularOutput;
        if (sh) {
            external[unsigned(nrd::ResourceType::IN_DIFF_SH1)].resource = frame.diffuseSh;
            external[unsigned(nrd::ResourceType::IN_SPEC_SH1)].resource = frame.specularSh;
            external[unsigned(nrd::ResourceType::OUT_DIFF_SH1)].resource = frame.diffuseShOutput;
            external[unsigned(nrd::ResourceType::OUT_SPEC_SH1)].resource = frame.specularShOutput;
        }
        ID3D12DescriptorHeap* heaps[] = {heap.Get()}; commands->SetDescriptorHeaps(1, heaps);
        for (uint32_t i = 0; i < dispatchCount; i++) {
            const uint32_t slot = frame.frameSlot * maxSets + i;
            const auto& dispatch = dispatches[i]; auto& pipeline = pipelines.at(dispatch.pipelineIndex);
            if (dispatch.resourcesNum > descriptorsPerSet || dispatch.constantBufferDataSize > constantStride)
                throw std::runtime_error("NRD dispatch exceeds declared limits");
            auto cpu = heap->GetCPUDescriptorHandleForHeapStart(); cpu.ptr += SIZE_T(slot) * descriptorsPerSet * stride;
            for (uint32_t j = 0; j < dispatch.resourcesNum; j++) {
                const auto& resource = dispatch.resources[j];
                auto& texture = resource.type == nrd::ResourceType::PERMANENT_POOL ? permanent.at(resource.indexInPool) :
                    resource.type == nrd::ResourceType::TRANSIENT_POOL ? transient.at(resource.indexInPool) : external[unsigned(resource.type)];
                if (!texture.resource) throw std::runtime_error("NRD requested an unbound input");
                bool storage = resource.descriptorType == nrd::DescriptorType::STORAGE_TEXTURE;
                texture.Transition(commands, storage ? D3D12_RESOURCE_STATE_UNORDERED_ACCESS : readState);
                if (storage) device->CreateUnorderedAccessView(texture.resource, nullptr, nullptr, cpu);
                else device->CreateShaderResourceView(texture.resource, nullptr, cpu);
                cpu.ptr += stride;
            }
            if (dispatch.constantBufferDataSize) memcpy(mapped + size_t(slot) * constantStride, dispatch.constantBufferData, dispatch.constantBufferDataSize);
            auto gpu = heap->GetGPUDescriptorHandleForHeapStart(); gpu.ptr += UINT64(slot) * descriptorsPerSet * stride;
            commands->SetComputeRootSignature(pipeline.root.Get()); commands->SetPipelineState(pipeline.state.Get());
            commands->SetComputeRootConstantBufferView(0, constants->GetGPUVirtualAddress() + UINT64(slot) * constantStride);
            commands->SetComputeRootDescriptorTable(1, gpu);
            commands->Dispatch(dispatch.gridWidth, dispatch.gridHeight, 1);
        }
    }
};

API int NrdAvailable() noexcept {
    try { NrdApi api; return 1; }
    catch (const std::exception& e) { strncpy_s(diagnostic, e.what(), _TRUNCATE); return 0; }
}
API const char* NrdDiagnostic() noexcept { return diagnostic; }
API int NrdCreate(ID3D12Device* device, uint32_t width, uint32_t height, NrdContext** output) noexcept {
    if (!output) return -1;
    *output = nullptr;
    if (!device || !width || !height || width > 65535 || height > 65535) return -1;
    try {
        auto context = std::make_unique<NrdContext>(); context->Initialize(device, width, height);
        *output = context.release(); return 0;
    } catch (const std::exception& e) { strncpy_s(diagnostic, e.what(), _TRUNCATE); return -2; }
    catch (...) { strcpy_s(diagnostic, "Unknown NRD initialization error"); return -3; }
}
API int NrdExecute(NrdContext* context, ID3D12GraphicsCommandList* commands, const NrdFrame* frame) noexcept {
    if (!context || !commands || !frame) return -1;
    try { context->Execute(commands, *frame); return 0; }
    catch (const std::exception& e) { strncpy_s(diagnostic, e.what(), _TRUNCATE); return -2; }
    catch (...) { strcpy_s(diagnostic, "Unknown NRD dispatch error"); return -3; }
}
API int NrdCreatePbr(ID3D12Device* device, uint32_t width, uint32_t height, NrdContext** output) noexcept {
    if (!output) return -1;
    *output = nullptr;
    if (!device || !width || !height || width > 65535 || height > 65535) return -1;
    try {
        auto context = std::make_unique<NrdContext>(); context->Initialize(device, width, height, true);
        *output = context.release(); return 0;
    } catch (const std::exception& e) { strncpy_s(diagnostic, e.what(), _TRUNCATE); return -2; }
    catch (...) { strcpy_s(diagnostic, "Unknown PBR NRD initialization error"); return -3; }
}

API int NrdCreateOpticalPair(ID3D12Device* device, uint32_t width, uint32_t height,
    NrdContext** reflection, NrdContext** transmission) noexcept {
    if (!reflection || !transmission) return -1;
    *reflection = *transmission = nullptr;
    if (!device || !width || !height || width > 65535 || height > 65535) return -1;
    try {
        auto first = std::make_unique<NrdContext>();
        first->Initialize(device, width, height);
        auto second = std::make_unique<NrdContext>();
        second->Initialize(device, width, height, false, false, first.get());
        *reflection = first.release();
        *transmission = second.release();
        return 0;
    } catch (const std::exception& e) { strncpy_s(diagnostic, e.what(), _TRUNCATE); return -2; }
    catch (...) { strcpy_s(diagnostic, "Unknown optical NRD initialization error"); return -3; }
}
// Raster radiance mode has an independent history and reuses the official dispatch executor.
API int NrdCreateRadiance(ID3D12Device* device, uint32_t width, uint32_t height, NrdContext** output) noexcept {
    if (!output) return -1;
    *output = nullptr;
    if (!device || !width || !height || width > 65535 || height > 65535) return -1;
    try {
        auto context = std::make_unique<NrdContext>(); context->Initialize(device, width, height, false, true);
        *output = context.release(); return 0;
    } catch (const std::exception& e) { strncpy_s(diagnostic, e.what(), _TRUNCATE); return -2; }
    catch (...) { strcpy_s(diagnostic, "Unknown radiance NRD initialization error"); return -3; }
}
API uint32_t NrdDispatchCount(const NrdContext* context) noexcept { return context ? context->dispatchCount : 0; }
API uint32_t NrdMaxAccumulatedFrames(const NrdContext* context) noexcept { return context && context->settingsInitialized ? context->configuredMaxFrames : 0; }
API void NrdDestroy(NrdContext* context) noexcept { delete context; }
