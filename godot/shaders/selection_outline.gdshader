shader_type canvas_item;

uniform sampler2D id_texture : filter_nearest;
uniform vec4 outline_color : source_color = vec4(1.0, 0.6, 0.0, 1.0);
uniform float thickness : hint_range(1.0, 5.0) = 2.0;

void fragment() {
	vec2 pixel_size = SCREEN_PIXEL_SIZE;
	float center_alpha = texture(id_texture, UV).a;

	if (center_alpha > 0.5) {
		// Inside the selected object — transparent (show 3D scene through)
		COLOR = vec4(0.0);
	} else {
		// Outside the selected object — check if any neighbor IS selected
		bool near_selected = false;
		for (float dy = -thickness; dy <= thickness; dy += 1.0) {
			for (float dx = -thickness; dx <= thickness; dx += 1.0) {
				if (dx == 0.0 && dy == 0.0) continue;
				float neighbor_alpha = texture(id_texture, UV + vec2(dx, dy) * pixel_size).a;
				if (neighbor_alpha > 0.5) {
					near_selected = true;
					break;
				}
			}
			if (near_selected) break;
		}

		if (near_selected) {
			COLOR = outline_color;
		} else {
			COLOR = vec4(0.0);
		}
	}
}
