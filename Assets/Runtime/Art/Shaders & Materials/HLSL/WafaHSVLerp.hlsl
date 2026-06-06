// Internal Helper: Converts RGB to HSV space safely
half3 RGBToHSV_Internal(half3 In) {
    half4 K = half4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    half4 P = lerp(half4(In.bg, K.wz), half4(In.gb, K.xy), step(In.b, In.g));
    half4 Q = lerp(half4(P.xyw, In.r), half4(In.r, P.yzx), step(P.x, In.r));
    half D = Q.x - min(Q.w, Q.y);
    half E = 1e-5; // Safeguard against half-precision underflow on mobile GPUs
    return half3(abs(Q.z + (Q.w - Q.y) / (6.0 * D + E)), D / (Q.x + E), Q.x);
}

// Internal Helper: Converts HSV back to RGB space
half3 HSVToRGB_Internal(half3 In) {
    half4 K = half4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    half3 P = abs(frac(In.xxx + K.xyz) * 6.0 - K.www);
    return In.z * lerp(K.xxx, saturate(P - K.xxx), In.y);
}

// Main Function for Shader Graph Node
void HSVLerp_half(half4 A, half4 B, half T, out half4 Out) {
    // 1. Convert inputs from RGB to HSV
    half3 hsvA = RGBToHSV_Internal(A.xyz);
    half3 hsvB = RGBToHSV_Internal(B.xyz);

    // 2. Shortest-Path Hue Interpolation
    half d = hsvB.x - hsvA.x;
    half d_wrap = d - floor(d + 0.5); // Forces delta into the closest [-0.5, 0.5] arc
    half hue = frac(hsvA.x + T * d_wrap);
    
    // Safety check for HLSL frac behavior with negative bounds
    if (hue < 0.0) hue += 1.0; 

    // 3. Standard linear interpolation for Saturation, Value, and Alpha
    half sat   = lerp(hsvA.y, hsvB.y, T);
    half val   = lerp(hsvA.z, hsvB.z, T);
    half alpha = lerp(A.w,    B.w,    T);

    // 4. Output final RGB + Alpha result
    Out = half4(HSVToRGB_Internal(half3(hue, sat, val)), alpha);
}
