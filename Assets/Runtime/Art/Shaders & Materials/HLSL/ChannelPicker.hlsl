void ChannelPicker_float(float channel_Number, float4 textureColor, out float outTexture)
{
    // Since channel_Number is exactly 0, 1, 2, or 3:
    // step(x, channel_Number) * step(channel_Number, x) returns 1.0 ONLY if channel_Number == x
    float4 mask;
    mask.r = step(0.0, channel_Number) * step(channel_Number, 0.0); // 1 if 0
    mask.g = step(1.0, channel_Number) * step(channel_Number, 1.0); // 1 if 1
    mask.b = step(2.0, channel_Number) * step(channel_Number, 2.0); // 1 if 2
    mask.a = step(3.0, channel_Number) * step(channel_Number, 3.0); // 1 if 3

    // The dot product multiplies your color by the mask and sums it up
    outTexture = dot(textureColor, mask);
}
void ChannelPicker_half(half channel_Number, half4 textureColor, out half outTexture)
{
    // Since channel_Number is exactly 0, 1, 2, or 3:
    // step(x, channel_Number) * step(channel_Number, x) returns 1.0 ONLY if channel_Number == x
    half4 mask;
    mask.r = step(0.0, channel_Number) * step(channel_Number, 0.0); // 1 if 0
    mask.g = step(1.0, channel_Number) * step(channel_Number, 1.0); // 1 if 1
    mask.b = step(2.0, channel_Number) * step(channel_Number, 2.0); // 1 if 2
    mask.a = step(3.0, channel_Number) * step(channel_Number, 3.0); // 1 if 3

    // The dot product multiplies your color by the mask and sums it up
    outTexture = dot(textureColor, mask);
}