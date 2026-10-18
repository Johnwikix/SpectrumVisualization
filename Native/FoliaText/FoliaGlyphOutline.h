// Immutable outline transfer shared by the D3D12 generator and the exact CPU fallback.
// ABI: 80-byte Bezier edges, 16-byte contours; coordinates remain at the 96-em source size.
struct FoliaGpuEdge { float points[4], control[4], domains[4], meta[4], tangents[4]; };
struct FoliaGpuContour { int first, count, winding, reserved; };
static_assert(sizeof(FoliaGpuEdge) == 80 && sizeof(FoliaGpuContour) == 16, "Glyph ABI changed");
struct FoliaGlyphOutline {
    ComPtr<ID2D1Geometry> geometry;
    msdfgen::Shape shape;
    std::vector<FoliaGpuEdge> edges;
    std::vector<FoliaGpuContour> contours;
    bool evenOdd = false, gpu = true, flip = false;
};

extern "C" __declspec(dllexport) HRESULT __cdecl FoliaCreateGlyphOutline(
    IUnknown* object, FoliaGlyphOutline** result, int* edgeCount, int* contourCount, int* fillRule, BYTE* digest) noexcept {
    if (!object || !result || !edgeCount || !contourCount || !fillRule || !digest) return E_INVALIDARG;
    *result=nullptr;
    try {
        HRESULT hr=FoliaOutlineSignature(object,digest);
        if (FAILED(hr)) return hr;
        auto outline=std::make_unique<FoliaGlyphOutline>();
        ComPtr<ABI::Microsoft::Graphics::Canvas::ICanvasResourceWrapperNative> wrapper;
        hr=object->QueryInterface(IID_PPV_ARGS(&wrapper));
        if (FAILED(hr)) return hr;
        hr=wrapper->GetNativeResource(nullptr,96,IID_PPV_ARGS(&outline->geometry));
        if (FAILED(hr)) return hr;
        auto receiver=Microsoft::WRL::Make<OutlineReceiver>();
        hr=outline->geometry->Simplify(D2D1_GEOMETRY_SIMPLIFICATION_OPTION_CUBICS_AND_LINES,nullptr,.001f,receiver.Get());
        if (SUCCEEDED(hr)) hr=receiver->Close();
        if (FAILED(hr)) return hr;
        outline->evenOdd=receiver->evenOdd;
        outline->shape=std::move(receiver->shape);
        outline->shape.setYAxisOrientation(msdfgen::Y_DOWNWARD);
        outline->shape.normalize();
        if (outline->evenOdd) outline->shape.orientContours();
        if (!outline->shape.validate()) return E_INVALIDARG;
        msdfgen::edgeColoringInkTrap(outline->shape,3.0,0);
        if (outline->shape.contours.size() > 32 || outline->shape.edgeCount()>2048) outline->gpu=false;
        for (const auto& contour:outline->shape.contours) {
            if (!outline->gpu) break;
            int first=int(outline->edges.size());
            for (size_t ei=0;ei<contour.edges.size();ei++) {
                const auto& edge=contour.edges[ei];
                int degree=edge->type();
                FoliaGpuEdge gpu{};const auto* points=edge->controlPoints();
                gpu.points[0]=float(points[0].x);gpu.points[1]=float(points[0].y);
                gpu.points[2]=float(points[1].x);gpu.points[3]=float(points[1].y);
                if(degree>1){gpu.control[0]=float(points[2].x);gpu.control[1]=float(points[2].y);}
                if(degree>2){gpu.control[2]=float(points[3].x);gpu.control[3]=float(points[3].y);}
                gpu.meta[0]=float(edge->color);gpu.meta[1]=float(degree);
                gpu.meta[2]=float(edge->direction(0).length());gpu.meta[3]=float(edge->direction(1).length());
                auto ad=edge->direction(0).normalize(true),bd=edge->direction(1).normalize(true);
                auto prev=contour.edges[(ei+contour.edges.size()-1)%contour.edges.size()]->direction(1).normalize(true);
                auto next=contour.edges[(ei+1)%contour.edges.size()]->direction(0).normalize(true);
                auto da=(prev+ad).normalize(true),db=(bd+next).normalize(true);
                gpu.domains[0]=float(da.x);gpu.domains[1]=float(da.y);gpu.domains[2]=float(db.x);gpu.domains[3]=float(db.y);
                gpu.tangents[0]=float(ad.x);gpu.tangents[1]=float(ad.y);gpu.tangents[2]=float(bd.x);gpu.tangents[3]=float(bd.y);
                outline->edges.push_back(gpu);
            }
            int count=int(outline->edges.size())-first;
            if (!count) continue;
            outline->contours.push_back({first,count,contour.winding(),0});
        }
        if (!outline->gpu) { outline->edges.clear(); outline->contours.clear(); }
        *edgeCount=int(outline->edges.size()); *contourCount=int(outline->contours.size());
        for(const auto& contour:outline->shape.contours) if(!contour.edges.empty()){
            const auto& edge=contour.edges.front();
            auto p=edge->point(.5)+edge->direction(.5).normalize().getOrthogonal()*.25;
            double d=msdfgen::SimpleTrueShapeDistanceFinder::oneShotDistance(outline->shape,p);
            if(fabs(d)<1e-5)continue;
            BOOL inside=FALSE;hr=outline->geometry->FillContainsPoint(Point{float(p.x),float(p.y)},nullptr,.001f,&inside);
            if(FAILED(hr))return hr;
            outline->flip=(d>0)!=(inside!=FALSE);break;
        }
        *fillRule=(outline->evenOdd?1:0)|(outline->gpu?0:2)|(outline->flip?4:0);
        *result=outline.release(); return S_OK;
    } catch (const std::bad_alloc&) { return E_OUTOFMEMORY; } catch (...) { return E_FAIL; }
}

extern "C" __declspec(dllexport) HRESULT __cdecl FoliaCopyGlyphOutline(
    const FoliaGlyphOutline* outline, FoliaGpuEdge* edges, int edgeCount, FoliaGpuContour* contours, int contourCount) noexcept {
    if (!outline || edgeCount!=int(outline->edges.size()) || contourCount!=int(outline->contours.size())) return E_INVALIDARG;
    if (edgeCount) { if (!edges) return E_INVALIDARG; std::copy(outline->edges.begin(),outline->edges.end(),edges); }
    if (contourCount) { if (!contours) return E_INVALIDARG; std::copy(outline->contours.begin(),outline->contours.end(),contours); }
    return S_OK;
}
extern "C" __declspec(dllexport) void __cdecl FoliaReleaseGlyphOutline(FoliaGlyphOutline* outline) noexcept { delete outline; }

extern "C" __declspec(dllexport) HRESULT __cdecl FoliaRenderGlyphOutline(
    const FoliaGlyphOutline* outline, int width, int height, float tx, float ty, float range, float* pixels) noexcept {
    if (!outline || !pixels || width<=0 || height<=0 || width>8192 || height>8192 || range<=0) return E_INVALIDARG;
    try {
        if (!outline->shape.edgeCount()) { std::fill_n(pixels,width*height*4,0.f); return S_OK; }
        msdfgen::BitmapSection<float,4> output(pixels,width,height,msdfgen::Y_DOWNWARD);
        msdfgen::SDFTransformation transform(msdfgen::Projection(msdfgen::Vector2(1),msdfgen::Vector2(tx,ty)),msdfgen::Range(range));
        msdfgen::generateMTSDF(output,outline->shape,transform);
        bool known=false;
        for (int y=0;y<height&&!known;y++) for (int x=0;x<width&&!known;x++) {
            if (output(x,y)[3]<.53f) continue;
            BOOL inside=FALSE;
            HRESULT hr=outline->geometry->FillContainsPoint(Point{x+.5f-tx,y+.5f-ty},nullptr,.001f,&inside);
            if (FAILED(hr)) return hr;
            if (!inside) for (int k=0;k<width*height*4;k++) pixels[k]=1-pixels[k];
            known=true;
        }
        return S_OK;
    } catch (const std::bad_alloc&) { return E_OUTOFMEMORY; } catch (...) { return E_FAIL; }
}
