// BGRA → RGBA channel swizzle shader
//
// CEF outputs BGRA framebuffers. Godot's Image format is RGBA.
// Instead of CPU-side byte swapping, we pass BGRA bytes directly
// as Rgba8 and swizzle the channels on the GPU for free.
shader_type canvas_item;

void fragment() {
	vec4 tex = texture(TEXTURE, UV);
	COLOR = vec4(tex.b, tex.g, tex.r, tex.a);
}
