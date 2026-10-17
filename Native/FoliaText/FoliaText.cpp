#include <windows.h>
#include <cstdint>
#include <dwrite.h>
#include <wrl.h>
#include <Microsoft.Graphics.Canvas.native.h>
#include "msdfgen.h"
#include <new>

using Microsoft::WRL::ComPtr;
using Point = D2D1_POINT_2F;

class OutlineReceiver final : public Microsoft::WRL::RuntimeClass<
    Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>, ID2D1SimplifiedGeometrySink> {
    msdfgen::Contour* contour = nullptr;
    msdfgen::Vector2 current, first;
    HRESULT status = S_OK;
public:
    msdfgen::Shape shape;
    bool evenOdd = false;
    static msdfgen::Vector2 vector(Point p) { return { p.x, p.y }; }
    void STDMETHODCALLTYPE SetFillMode(D2D1_FILL_MODE mode) override { evenOdd = mode == D2D1_FILL_MODE_ALTERNATE; }
    void STDMETHODCALLTYPE SetSegmentFlags(D2D1_PATH_SEGMENT) override { }
    void STDMETHODCALLTYPE BeginFigure(Point point, D2D1_FIGURE_BEGIN) override {
        if (FAILED(status)) return;
        try { contour = &shape.addContour(); current = first = vector(point); }
        catch (...) { status = E_OUTOFMEMORY; }
    }
    void STDMETHODCALLTYPE AddLines(const Point* points, UINT count) override {
        if (FAILED(status)) return;
        try { for (UINT i=0;i<count;i++) {
            const auto end = vector(points[i]);
            if (end != current) contour->addEdge(msdfgen::EdgeHolder(current, end));
            current = end;
        } } catch (...) { status = E_OUTOFMEMORY; }
    }
    void STDMETHODCALLTYPE AddBeziers(const D2D1_BEZIER_SEGMENT* segments, UINT count) override {
        if (FAILED(status)) return;
        try { for (UINT i=0;i<count;i++) {
            const auto& b=segments[i];
            contour->addEdge(msdfgen::EdgeHolder(current, vector(b.point1), vector(b.point2), vector(b.point3)));
            current = vector(b.point3);
        } } catch (...) { status = E_OUTOFMEMORY; }
    }
    void STDMETHODCALLTYPE EndFigure(D2D1_FIGURE_END) override {
        if (FAILED(status)) return;
        try { if (current != first) contour->addEdge(msdfgen::EdgeHolder(current, first)); contour = nullptr; }
        catch (...) { status = E_OUTOFMEMORY; }
    }
    HRESULT STDMETHODCALLTYPE Close() override { return status; }
};

extern "C" __declspec(dllexport) HRESULT __cdecl FoliaGenerateMtsdf(
    IUnknown* object, int width, int height, float translateX, float translateY, float range, float* pixels) noexcept {
    if (!object || !pixels || width <= 0 || height <= 0 || width > 8192 || height > 8192 || range <= 0) return E_INVALIDARG;
    try {
        // Official Win2D native resource interop; no managed COM callback or private vtable layout.
        ComPtr<ABI::Microsoft::Graphics::Canvas::ICanvasResourceWrapperNative> wrapper;
        HRESULT hr = object->QueryInterface(IID_PPV_ARGS(&wrapper));
        if (FAILED(hr)) return hr;
        ComPtr<ID2D1Geometry> outline;
        hr = wrapper->GetNativeResource(nullptr, 96, IID_PPV_ARGS(&outline));
        if (FAILED(hr)) return hr;
        auto receiver = Microsoft::WRL::Make<OutlineReceiver>();
        hr = outline->Simplify(D2D1_GEOMETRY_SIMPLIFICATION_OPTION_CUBICS_AND_LINES, nullptr, .001f, receiver.Get());
        if (SUCCEEDED(hr)) hr = receiver->Close();
        if (FAILED(hr)) return hr;
        auto& shape = receiver->shape;
        shape.setYAxisOrientation(msdfgen::Y_DOWNWARD);
        if (!shape.edgeCount()) {
            std::fill_n(pixels, width*height*4, 0.f);
            return S_OK;
        }
        shape.normalize();
        if (receiver->evenOdd) shape.orientContours();
        if (!shape.validate()) return E_INVALIDARG;
        msdfgen::edgeColoringInkTrap(shape, 3.0, 0);
        msdfgen::BitmapSection<float, 4> output(pixels, width, height, msdfgen::Y_DOWNWARD);
        msdfgen::SDFTransformation transform(msdfgen::Projection(msdfgen::Vector2(1), msdfgen::Vector2(translateX, translateY)), msdfgen::Range(range));
        msdfgen::generateMTSDF(output, shape, transform);
        // DirectWrite uses nonzero winding. Normalize polarity against its actual fill test,
        // including fonts whose contour orientation differs from the conventional TrueType one.
        bool polarityKnown = false;
        for (int y = 0; y < height && !polarityKnown; ++y) for (int x = 0; x < width && !polarityKnown; ++x) {
            const float value = output(x,y)[3];
            if (value < .53f) continue;
            BOOL inside = FALSE;
            Point p{ x+.5f-translateX, y+.5f-translateY };
            hr = outline->FillContainsPoint(p, nullptr, .001f, &inside);
            if (FAILED(hr)) return hr;
            if (!inside) for (int k=0;k<width*height*4;k++) pixels[k]=1-pixels[k];
            polarityKnown = true;
        }
        return S_OK;
    } catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
      catch (...) { return E_FAIL; }
}

// Canvas's middle baseline is half the font x-height above the alphabetic baseline.
// Obtain the actual fallback face used by DirectWrite; a family-name approximation drifts for mixed scripts.
class FontMetricReceiver final : public Microsoft::WRL::RuntimeClass<
    Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>, IDWriteTextRenderer> {
public:
    float baseline = 0, xHeight = 0;
    bool found = false;
    HRESULT STDMETHODCALLTYPE IsPixelSnappingDisabled(void*, BOOL* disabled) override { *disabled = TRUE; return S_OK; }
    HRESULT STDMETHODCALLTYPE GetCurrentTransform(void*, DWRITE_MATRIX* matrix) override { *matrix = {1,0,0,1,0,0}; return S_OK; }
    HRESULT STDMETHODCALLTYPE GetPixelsPerDip(void*, FLOAT* value) override { *value = 1; return S_OK; }
    HRESULT STDMETHODCALLTYPE DrawGlyphRun(void*, FLOAT, FLOAT y, DWRITE_MEASURING_MODE,
        const DWRITE_GLYPH_RUN* run, const DWRITE_GLYPH_RUN_DESCRIPTION*, IUnknown*) override {
        if (!found && run && run->fontFace) {
            DWRITE_FONT_METRICS metrics;
            run->fontFace->GetMetrics(&metrics);
            baseline = y;
            xHeight = float(metrics.xHeight) / metrics.designUnitsPerEm;
            found = true;
        }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE DrawUnderline(void*, FLOAT, FLOAT, const DWRITE_UNDERLINE*, IUnknown*) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE DrawStrikethrough(void*, FLOAT, FLOAT, const DWRITE_STRIKETHROUGH*, IUnknown*) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE DrawInlineObject(void*, FLOAT, FLOAT, IDWriteInlineObject*, BOOL, BOOL, IUnknown*) override { return S_OK; }
};
extern "C" __declspec(dllexport) HRESULT __cdecl FoliaGetTextMetrics(IUnknown* object, float* baseline, float* xHeight) noexcept {
    if (!object || !baseline || !xHeight) return E_INVALIDARG;
    ComPtr<ABI::Microsoft::Graphics::Canvas::ICanvasResourceWrapperNative> wrapper;
    HRESULT hr = object->QueryInterface(IID_PPV_ARGS(&wrapper));
    if (FAILED(hr)) return hr;
    ComPtr<IDWriteTextLayout> layout;
    hr = wrapper->GetNativeResource(nullptr, 96, IID_PPV_ARGS(&layout));
    if (FAILED(hr)) return hr;
    auto receiver = Microsoft::WRL::Make<FontMetricReceiver>();
    if (!receiver) return E_OUTOFMEMORY;
    hr = layout->Draw(nullptr, receiver.Get(), 0, 0);
    if (FAILED(hr)) return hr;
    *baseline = receiver->baseline;
    *xHeight = receiver->xHeight;
    return S_OK;
}
