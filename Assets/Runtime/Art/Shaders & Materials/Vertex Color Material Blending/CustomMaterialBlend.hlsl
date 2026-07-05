// Float precision version
void MaterialBlend_float(
    float4 ColorA,
    float  AlphaA, 
    float3 NormalA, 
    float  AoA, 
    float  SmoothA, 
    float  MetalA, 

    float4 ColorB,
    float  AlphaB,
    float3 NormalB,
    float  AoB,
    float  SmoothB,
    float  MetalB,
    
    float T,
    out float4 OutColor,
    out float OutAlpha,
    out float3 OutNormal,
    out float OutAO,
    out float OutSmooth,
    out float OutMetal
) {
    OutColor  = lerp(ColorA, ColorB, T);
    OutAlpha  = lerp(AlphaA, AlphaB, T);
    OutNormal = lerp(NormalA, NormalB, T);
    OutAO     = lerp(AoA, AoB, T);
    OutSmooth = lerp(SmoothA, SmoothB, T);
    OutMetal  = lerp(MetalA, MetalB, T);
}

// Half precision version
void MaterialBlend_half(
    half4 ColorA,
    half  AlphaA, 
    half3 NormalA, 
    half  AoA, 
    half  SmoothA, 
    half  MetalA, 

    half4 ColorB,
    half  AlphaB,
    half3 NormalB,
    half  AoB,
    half  SmoothB,
    half  MetalB,
    
    half T,
    out half4 OutColor,
    out half OutAlpha,
    out half3 OutNormal,
    out half OutAO,
    out half OutSmooth,
    out half OutMetal
) {
    OutColor  = lerp(ColorA, ColorB, T);
    OutAlpha  = lerp(AlphaA, AlphaB, T);
    OutNormal = lerp(NormalA, NormalB, T);
    OutAO     = lerp(AoA, AoB, T);
    OutSmooth = lerp(SmoothA, SmoothB, T);
    OutMetal  = lerp(MetalA, MetalB, T);
}
