#define MAX_AREA_LIGHT_COUNT 16

int      _AreaLightCount;
float    _AreaLightRenderShadow[MAX_AREA_LIGHT_COUNT];
float    _AreaLightTextureIndices[MAX_AREA_LIGHT_COUNT];
float4   _AreaLightColors[MAX_AREA_LIGHT_COUNT];
float4x4 _AreaLightVertices[MAX_AREA_LIGHT_COUNT];

struct AreaLight
{
    float4   color;
    float4x4 vertices;
};

int GetAreaLightCount()
{
    return _AreaLightCount;
}

AreaLight GetAreaLight(int index)
{
    AreaLight areaLight;
    areaLight.color        = _AreaLightColors[index];
    areaLight.vertices     = _AreaLightVertices[index];
    return areaLight;
}
