
#ifndef WATER_WAVES_INCLUDED
#define WATER_WAVES_INCLUDED


void simpleWave_float(float3 Position, float2 WaveDirection, float WaveLength, float amplitude, float speed, float Time, out float3 Offset, out float3 Normal)
{
    float frequency = 2.0 / WaveLength;
    float speedFactor = speed * frequency;
    float offset = amplitude * sin((dot(WaveDirection, Position.xz) * frequency) + (Time * speedFactor));

    Offset = float3(0, offset, 0);
    Normal = float3(
        -WaveDirection.x * frequency * amplitude * cos((dot(WaveDirection, Position.xz) * frequency) + (Time * speedFactor)),
        1,
        -WaveDirection.y * frequency * amplitude * cos((dot(WaveDirection, Position.xz) * frequency) + (Time * speedFactor))
        );
}

struct WaveData
{
    float amplitude;
    float frequency;
    float phase;
    float pad; 
    float2 direction;
};

#ifndef SHADERGRAPH_PREVIEW
    StructuredBuffer<WaveData> _Waves;
    int _WaveCount;
#endif


void EvaluateWaveSetComplex(
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

        // Sharpen crests / flatten troughs: e^(sin-1) ranges [e^-2, 1]
        // instead of sin's symmetric [-1, 1]
        float sharpened = exp(sinTheta - 1.0);

        outOffset.y += w.amplitude * sharpened;

        // The exp factor carries through into the derivative too (chain rule),
        // so it multiplies the slope the same way it multiplies the height
        float slope = w.frequency * w.amplitude * sharpened * cosTheta;
        outTangent.y += w.direction.x * slope;
        outBinormal.y += w.direction.y * slope;
    }
}

void MultiWaveDisplacementComplex_float(
    float3 Position, 
    float Time,  
    out float3 Offset, 
    out float3 Normal
)
{
    #ifndef SHADERGRAPH_PREVIEW
    float3 offset, tangent, binormal;

    EvaluateWaveSetComplex(_Waves, _WaveCount, Position, Time, offset, tangent, binormal);

    // Smooth height & normal transitions at the meeting boundary
    Offset = offset;

    float3 Tangent = tangent;
    float3 Binormal = binormal;
    Normal = normalize(cross(Binormal, Tangent));

    #else
    Offset = float3(0, 0, 0);
    Normal = float3(0, 1, 0);
    #endif
}


void MultiWaveDisplacement_float(
    float3 Position, 
    float Time, 
    out float3 Offset, 
    out float3 Normal
)
{
    float3 totalOffset = float3(0, 0, 0);
    float3 tangent = float3(1, 0, 0);
    float3 binormal = float3(0, 0, 1);

    #ifndef SHADERGRAPH_PREVIEW
    int count = min(_WaveCount, 16);
    
    for (int i = 0; i < count; i++)
    {
        // Here we read directly from the buffer we defined above!
        WaveData w = _Waves[i];

        float theta = (dot(w.direction, Position.xz) * w.frequency) + (Time * w.phase);
        float sinTheta = sin(theta);
        float cosTheta = cos(theta);

        totalOffset.y += w.amplitude * sinTheta;

        float slope = w.frequency * w.amplitude * cosTheta;
        tangent.y += w.direction.x * slope;
        binormal.y += w.direction.y * slope;
    }

    Normal = normalize(cross(binormal, tangent));

    #else
    // Fallback for Shader Graph Preview window
    totalOffset = float3(0, 0, 0);
    Normal = float3(0, 1, 0);
    #endif

    Offset = totalOffset;
}

#endif