// ============================================================
// GerstnerWave.hlsl
// Single Gerstner wave — vertex offset + analytic normal.
// Matches the node graph: Speed / Sharpness / Normal subgraphs.
//
// USAGE IN SHADER GRAPH:
// Add a "Custom Function" node, set Type = File, point Source
// at this file, Function name = "GerstnerWave".
//
// Inputs  (must match the node's input pins, in this order):
//   float3 Position        -> World-space vertex position
//   float3 WaveDirection    -> Direction the wave travels (xz used)
//   float  WaveLength       -> Distance between crests
//   float  WaveHeight       -> Amplitude (A)
//   float  Gravity          -> Usually 9.8
//   float  PeakSharpness    -> Q, crest peaking factor
//   float  Time             -> _TimeParameters.x or similar
//
// Outputs (must match the node's output pins, in this order):
//   float3 Offset  -> add to Position in the vertex stage
//   float3 Normal  -> use to override the geometric normal
// ============================================================

#ifndef GERSTNER_WAVE_INCLUDED
#define GERSTNER_WAVE_INCLUDED

#define GERSTNER_TAU 6.28318530718

void GerstnerWave_float(
    float3 Position,
    float3 WaveDirection,
    float  WaveLength,
    float  WaveHeight,
    float  Gravity,
    float  PeakSharpness,
    float  Time,
    out float3 Offset,
    out float3 Normal)
{
    // ---- Direction (xz plane only) ------------------------------
    float2 d = normalize(WaveDirection.xz);

    // ---- Speed subgraph -------------------------------------------
    float k     = GERSTNER_TAU / max(WaveLength, 1e-4);
    float omega = sqrt(Gravity * k);

    // ---- Phase (Dot Product -> Subtract) ---------------------------
    float theta = k * dot(d, Position.xz) - omega * Time;

    float sinT = sin(theta);
    float cosT = cos(theta);

    // ---- Sharpness subgraph -----------------------------------------
    float Q = PeakSharpness / max(k * WaveHeight, 1e-4);

    // ---- Offset (position displacement) ------------------------------
    Offset = float3(
        Q * WaveHeight * d.x * cosT,
        WaveHeight * sinT,
        Q * WaveHeight * d.y * cosT
    );

    // ---- Normal subgraph -----------------------------------------------
    float WA = omega * WaveHeight;

    float3 tangent = float3(
        1 - Q * d.x * d.x * sinT,
        d.x * WA * cosT,
        -Q * d.x * d.y * sinT
    );

    float3 binormal = float3(
        -Q * d.x * d.y * sinT,
        d.y * WA * cosT,
        1 - Q * d.y * d.y * sinT
    );

    Normal = normalize(cross(binormal, tangent));
}

#endif // GERSTNER_WAVE_INCLUDED

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