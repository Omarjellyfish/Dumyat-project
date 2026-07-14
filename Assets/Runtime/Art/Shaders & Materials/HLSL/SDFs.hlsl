// Inputs:
// UV             - The incoming texture coordinates (0.0 to 1.0)
// Size           - Width and height of the rectangle (e.g., 0.5, 0.5)
// Radius         - Corner roundness (0.0 = sharp, higher = more rounded)
// Offset         - Shifts mask position (Positive X = Right, Positive Y = Up/Down depending on API)
// EdgeSmoothness - Blur amount per side (X=Left, Y=Top, Z=Right, W=Bottom)
// Output:
// Out            - The final grayscale mask (1.0 inside, 0.0 outside)

void DrawRoundedRectangleMask_float(
    float2 UV, 
    float2 Size, 
    float Radius, 
    float2 Offset, 
    float4 EdgeSmoothness, 
    out float Out
) {
    // 1. Center the UVs around (0, 0) and apply the corrected position offset
    float2 centeredUV = UV - 0.5 + Offset;

    // 2. Calculate standard Box/Rounded Rectangle distance field
    float2 halfSize = Size * 0.5;
    float2 d = abs(centeredUV) - halfSize + Radius;
    float dist = length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - Radius;

    // 3. Determine which edge/side the current pixel is closest to
    float isRight  = smoothstep(0.0, 1.0, centeredUV.x / max(halfSize.x, 0.001));
    float isLeft   = smoothstep(0.0, 1.0, -centeredUV.x / max(halfSize.x, 0.001));
    float isTop    = smoothstep(0.0, 1.0, centeredUV.y / max(halfSize.y, 0.001));
    float isBottom = smoothstep(0.0, 1.0, -centeredUV.y / max(halfSize.y, 0.001));

    // Normalize weights so they equal 1.0 overall across the canvas
    float weightSum = isRight + isLeft + isTop + isBottom;
    isRight  /= weightSum;
    isLeft   /= weightSum;
    isTop    /= weightSum;
    isBottom /= weightSum;

    // 4. Extract and blend edge smoothness from Vector 4 (X=Left, Y=Top, Z=Right, W=Bottom)
    float localSmoothness = (EdgeSmoothness.x * isLeft) + 
                            (EdgeSmoothness.y * isTop) + 
                            (EdgeSmoothness.z * isRight) + 
                            (EdgeSmoothness.w * isBottom);

    // Guard rail: Ensure smoothness doesn't hit 0 to avoid smoothstep artifacts
    localSmoothness = max(localSmoothness, 0.0001);

    // 5. Output the final anti-aliased/blurred edge mask with corrected step order
    Out = smoothstep(localSmoothness, -localSmoothness, dist);
}

void DrawRoundedRectangleMask_half(
    half2 UV, 
    half2 Size, 
    half Radius, 
    half2 Offset, 
    half4 EdgeSmoothness, 
    out half Out
) {
    // 1. Center the UVs around (0, 0) and apply the corrected position offset
    half2 centeredUV = UV - 0.5 + Offset;

    // 2. Calculate standard Box/Rounded Rectangle distance field
    half2 halfSize = Size * 0.5;
    half2 d = abs(centeredUV) - halfSize + Radius;
    half dist = length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - Radius;

    // 3. Determine which edge/side the current pixel is closest to
    half isRight  = smoothstep(0.0, 1.0, centeredUV.x / max(halfSize.x, 0.001));
    half isLeft   = smoothstep(0.0, 1.0, -centeredUV.x / max(halfSize.x, 0.001));
    half isTop    = smoothstep(0.0, 1.0, centeredUV.y / max(halfSize.y, 0.001));
    half isBottom = smoothstep(0.0, 1.0, -centeredUV.y / max(halfSize.y, 0.001));

    // Normalize weights so they equal 1.0 overall across the canvas
    half weightSum = isRight + isLeft + isTop + isBottom;
    isRight  /= weightSum;
    isLeft   /= weightSum;
    isTop    /= weightSum;
    isBottom /= weightSum;

    // 4. Extract and blend edge smoothness from Vector 4 (X=Left, Y=Top, Z=Right, W=Bottom)
    half localSmoothness = (EdgeSmoothness.x * isLeft) + 
                            (EdgeSmoothness.y * isTop) + 
                            (EdgeSmoothness.z * isRight) + 
                            (EdgeSmoothness.w * isBottom);

    // Guard rail: Ensure smoothness doesn't hit 0 to avoid smoothstep artifacts
    localSmoothness = max(localSmoothness, 0.0001);

    // 5. Output the final anti-aliased/blurred edge mask with corrected step order
    Out = smoothstep(localSmoothness, -localSmoothness, dist);
}