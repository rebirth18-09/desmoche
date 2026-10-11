shader_type canvas_item;
uniform vec2 region_origin = vec2(0.0);
void fragment() {
    vec2 local_uv = (UV - region_origin) * 2.0;
    // COLOR ya incluye la textura en fragment(); sólo se recorta el alfa.
    COLOR.a *= 1.0 - smoothstep(0.475, 0.5, length(local_uv - vec2(0.5)));
}
