#ifndef TRIG_MODIFIERS_INCLUDED
#define TRIG_MODIFIERS_INCLUDED

// Optimized specifically for boundary seams and lines
void CalculateSeamWobble_float(
    float HorizonCoord, // Pass just ONE horizontal coordinate channel here
    float Time, 
    float3 Frequencies,   // X = Wave1, Y = Wave2, Z = Wave3
    float3 Speeds,        // X = Wave1, Y = Wave2, Z = Wave3
    float3 Amplitudes,    // X = Wave1, Y = Wave2, Z = Wave3
    out float OutWobble)
{
    // Wave 1: Large sweeping curve
    float w1 = sin(HorizonCoord * Frequencies.x + Time * Speeds.x) * Amplitudes.x;

    // Wave 2: Medium break up
    float w2 = sin(HorizonCoord * Frequencies.y - Time * Speeds.y) * Amplitudes.y;


    float w3 = cos(HorizonCoord * Frequencies.z + Time * Speeds.z) * Amplitudes.z;

    // Combine them into a single offset scalar
    OutWobble = w1 + w2 + w3;
}

#endif // TRIG_MODIFIERS_INCLUDED
