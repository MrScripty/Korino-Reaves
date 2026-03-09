<!--
    Orientation Gizmo

    Displays an interactive 3D axis gizmo showing camera orientation.
    Clicking an axis endpoint snaps the camera to a canonical view
    (Front, Back, Left, Right, Top, Bottom).
-->
<script lang="ts">
    type Vec3 = readonly [number, number, number];

    interface Props {
        yaw: number;
        pitch: number;
        onSnapView: (yaw: number, pitch: number) => void;
        size?: number;
    }

    let { yaw, pitch, onSnapView, size = 100 }: Props = $props();

    const DEG2RAD = Math.PI / 180;

    // Axis definitions with colors and snap targets
    const axes = [
        { name: 'X', color: '#ef4444', dir: [1, 0, 0], posYaw: 90, posPitch: 0, negYaw: -90, negPitch: 0 },
        { name: 'Y', color: '#22c55e', dir: [0, 1, 0], posYaw: 0, posPitch: -89, negYaw: 0, negPitch: 89 },
        { name: 'Z', color: '#3b82f6', dir: [0, 0, 1], posYaw: 0, posPitch: 0, negYaw: 180, negPitch: 0 },
    ] as const;

    function cross(a: Vec3, b: Vec3): Vec3 {
        return [
            a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0],
        ];
    }

    function dot(a: Vec3, b: Vec3): number {
        return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    }

    function normalize(v: Vec3): Vec3 {
        const len = Math.sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
        if (len < 1e-10) return [0, 0, 1];
        return [v[0] / len, v[1] / len, v[2] / len];
    }

    // Project world axes into screen space based on camera yaw/pitch
    let projected = $derived.by(() => {
        const y = yaw * DEG2RAD;
        const p = pitch * DEG2RAD;

        // Camera forward (from camera toward target)
        const fwd: Vec3 = [
            -Math.cos(p) * Math.sin(y),
            Math.sin(p),
            -Math.cos(p) * Math.cos(y),
        ];

        // Camera right = normalize(forward x worldUp)
        const right = normalize(cross(fwd, [0, 1, 0]));
        // Camera up = right x forward
        const up = cross(right, fwd);

        return axes.map((axis) => {
            const sx = dot(right, axis.dir);
            const sy = -dot(up, axis.dir);
            const depth = dot(fwd, axis.dir);
            return { ...axis, sx, sy, depth };
        });
    });

    // Sort back-to-front for proper layering (negative ends drawn first, then positive)
    let sorted = $derived(
        [...projected].sort((a, b) => a.depth - b.depth)
    );
</script>

<svg
    width={size}
    height={size}
    viewBox="-1.4 -1.4 2.8 2.8"
    class="orientation-gizmo"
    role="group"
    aria-label="Camera orientation gizmo"
>
    <!-- Background -->
    <circle cx="0" cy="0" r="1.3" class="gizmo-bg" />

    <!-- Render axes sorted back-to-front -->
    {#each sorted as axis (axis.name)}
        <!-- Negative end line + dot -->
        <line
            x1="0" y1="0"
            x2={-axis.sx * 0.85} y2={-axis.sy * 0.85}
            stroke={axis.color}
            stroke-width="0.06"
            opacity={0.25 + 0.15 * (1 - axis.depth)}
        />
        <!-- svelte-ignore a11y_no_static_element_interactions -->
        <circle
            cx={-axis.sx * 0.85} cy={-axis.sy * 0.85}
            r="0.12"
            fill={axis.color}
            opacity={0.3 + 0.15 * (1 - axis.depth)}
            class="axis-btn"
            role="button"
            tabindex="-1"
            aria-label="{axis.name} negative"
            onclick={(e: MouseEvent) => { e.stopPropagation(); onSnapView(axis.negYaw, axis.negPitch); }}
        />

        <!-- Positive end line -->
        <line
            x1="0" y1="0"
            x2={axis.sx} y2={axis.sy}
            stroke={axis.color}
            stroke-width="0.08"
            opacity={0.5 + 0.5 * (1 + axis.depth) / 2}
        />
        <!-- Positive end circle + label -->
        <!-- svelte-ignore a11y_no_static_element_interactions -->
        <circle
            cx={axis.sx} cy={axis.sy}
            r="0.2"
            fill={axis.color}
            opacity={0.6 + 0.4 * (1 + axis.depth) / 2}
            class="axis-btn"
            role="button"
            tabindex="-1"
            aria-label="{axis.name} axis"
            onclick={(e: MouseEvent) => { e.stopPropagation(); onSnapView(axis.posYaw, axis.posPitch); }}
        />
        <text
            x={axis.sx} y={axis.sy}
            text-anchor="middle"
            dominant-baseline="central"
            class="axis-label"
            pointer-events="none"
        >{axis.name}</text>
    {/each}
</svg>

<style>
    .orientation-gizmo {
        pointer-events: auto;
        filter: drop-shadow(0 1px 4px rgba(0, 0, 0, 0.6));
    }

    .gizmo-bg {
        fill: rgba(0, 0, 0, 0.5);
        stroke: rgba(255, 255, 255, 0.08);
        stroke-width: 0.04;
    }

    .axis-btn {
        cursor: pointer;
        transition: opacity 0.12s, r 0.12s;
    }

    .axis-btn:hover {
        opacity: 1 !important;
    }

    .axis-label {
        fill: white;
        font-size: 0.22px;
        font-weight: 700;
        font-family: var(--font-sans);
    }
</style>
