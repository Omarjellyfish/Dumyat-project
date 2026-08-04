#ifndef WATER_WAVES_INCLUDED
#define WATER_WAVES_INCLUDED

struct WaveData
{
    float amplitude;
    float frequency;
    float phase;
    float pad; 
    float2 direction;
};

#ifndef SHADERGRAPH_PREVIEW
    StructuredBuffer<WaveData> _WavesA;
    int _WaveCountA;

    StructuredBuffer<WaveData> _WavesB;
    int _WaveCountB;
#endif

// Helper function to evaluate wave displacement for a single system
void EvaluateWaveSet(
    StructuredBuffer<WaveData> waveBuffer, 
    int waveCount, 
    float3 Position, 
    float Time, 
    out float3 outOffset, 
    out float3 outTangent, 
    out float3 outBinormal
)
{
    outOffset = float3(0, 0, 0);
    outTangent = float3(1, 0, 0);
    outBinormal = float3(0, 0, 1);

    int count = min(waveCount, 16);
    for (int i = 0; i < count; i++)
    {
        WaveData w = waveBuffer[i];

        float theta = (dot(w.direction, Position.xz) * w.frequency) + (Time * w.phase);
        float sinTheta = sin(theta);
        float cosTheta = cos(theta);

        outOffset.y += w.amplitude * sinTheta;

        float slope = w.frequency * w.amplitude * cosTheta;
        outTangent.y += w.direction.x * slope;
        outBinormal.y += w.direction.y * slope;
    }
}

// Shader Graph entry point for blending Nile (A) & Ocean (B)
void BarzakhMultiWaveDisplacement_float(
    float3 Position, 
    float Time, 
    float BlendFactor, 
    out float3 Offset, 
    out float3 Normal
)
{
    #ifndef SHADERGRAPH_PREVIEW
    float3 offsetA, tangentA, binormalA;
    float3 offsetB, tangentB, binormalB;

    EvaluateWaveSet(_WavesA, _WaveCountA, Position, Time, offsetA, tangentA, binormalA);
    EvaluateWaveSet(_WavesB, _WaveCountB, Position, Time, offsetB, tangentB, binormalB);

    float t = saturate(BlendFactor);

    // Smooth height & normal transitions at the meeting boundary
    Offset = lerp(offsetA, offsetB, t);

    float3 blendedTangent = lerp(tangentA, tangentB, t);
    float3 blendedBinormal = lerp(binormalA, binormalB, t);
    Normal = normalize(cross(blendedBinormal, blendedTangent));

    #else
    Offset = float3(0, 0, 0);
    Normal = float3(0, 1, 0);
    #endif
}

#endif