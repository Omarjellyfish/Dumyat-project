#ifndef TRIG_MODIFIERS_INCLUDED
#define TRIG_MODIFIERS_INCLUDED

void CalculateSeamWobble_float(
    float HorizonCoord,
    float Time, 
    float3 Frequencies,   
    float3 Speeds,        
    float3 Amplitudes,    
    out float OutWobble       // Back to one clean, perfect wave output
)
{
    float w1 = sin(HorizonCoord * Frequencies.x + Time * Speeds.x) * Amplitudes.x;
    float w2 = sin(HorizonCoord * Frequencies.y - Time * Speeds.y) * Amplitudes.y;
    float w3 = cos(HorizonCoord * Frequencies.z + Time * Speeds.z) * Amplitudes.z;

    OutWobble = w1 + w2 + w3;
}

#endif 
