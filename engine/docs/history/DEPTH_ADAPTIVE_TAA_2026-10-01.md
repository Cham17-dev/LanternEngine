# Depth-Adaptive TAA Improvements

**Date:** 2026-10-01  
**Status:** A historical experiment, replaced by a fix of the temporal reprojection.

The thresholds and weights below are no longer used. Lowering the history weight in the distance made
the flicker worse; comparing device depth between different cameras and keeping depth in FP16 could
not tell distant surfaces apart correctly. The current analysis, changes and check results:
[TEMPORAL_REVIEW_2026-10-01.md](TEMPORAL_REVIEW_2026-10-01.md).

## The problem

The TAA of the time used fixed thresholds for:
1. Detecting disocclusion (newly revealed surfaces): `0.0005`
2. The temporal accumulation weight: `0.9` → `0.65` depending on motion

These parameters ignore the pixel's depth, which leads to problems:
- **Distant objects (depth → 1.0)**: temporal instability, flicker
- **Near objects (depth → 0.0)**: too much blur in fast motion

## The approach: depth-adaptive thresholds

### 1. Depth-adaptive disocclusion threshold

**File:** `gpu/shadps4/video_core/host_shaders/taa.comp`

```glsl
float depthThreshold(float depth) {
    // Near: 0.001, Far (>0.95): 0.0002
    return mix(0.001, 0.0002, smoothstep(0.7, 0.95, depth));
}
```

**Logic:**
- Near surfaces (depth < 0.7): threshold `0.001`, which allows small differences
- Far surfaces (depth > 0.95): threshold `0.0002`, stricter, fewer false positives
- A smooth transition through `smoothstep(0.7, 0.95, depth)`

**Use:**
```glsl
float depthDiff = abs(history.a - depth);
float threshold = depthThreshold(depth);
if (depthDiff < threshold && ...) {
    // Accept history
}
```

### 2. Depth-adaptive temporal weight

```glsl
float temporalWeight(float depth, float motionLength) {
    // Base weight decreases for distant objects: 0.9 near, 0.8 far
    float baseWeight = mix(0.9, 0.8, smoothstep(0.7, 0.95, depth));
    // Motion reduces weight more aggressively for distant pixels
    float motionFactor = mix(0.65, 0.5, smoothstep(0.7, 0.95, depth));
    return mix(baseWeight, motionFactor, clamp(motionLength / 16.0, 0.0, 1.0));
}
```

**Logic:**
- **Base weight without motion:**
  - Near: `0.9` (aggressive history accumulation)
  - Far: `0.8` (more conservative, less flicker)

- **In fast motion (motionLength > 16px):**
  - Near: lowered to `0.65`
  - Far: lowered to `0.5` (more conservative still)

**Use:**
```glsl
float motionLength = length(motion);
float weight = temporalWeight(depth, motionLength);
result = mix(current, clipped, weight);
```

### 3. Motion vector validation (camera_motion.comp)

**File:** `gpu/shadps4/video_core/host_shaders/camera_motion.comp`

```glsl
// Depth-adaptive validation: distant surfaces need tighter depth matching.
float depth_threshold = mix(0.001, 0.0003, smoothstep(0.7, 0.95, depth));
if (object_motion.b > 0.99 && abs(object_motion.a - depth) < depth_threshold &&
    !any(isnan(object_motion.rg)) && !any(isinf(object_motion.rg))) {
    result = object_motion.rg;
}
```

**What changed:**
- Old threshold: a fixed `0.001`
- New threshold: `0.001` → `0.0003` for distant surfaces
- Keeps object motion vectors from the wrong pixels from being used

### 4. Clearer debug views

**File:** `gpu/shadps4/video_core/host_shaders/camera_motion.comp`

The `BB_DEBUG_MOTION=1` modes are now structured:
```glsl
if (mode == 1) {
    // Raw depth visualization
    imageStore(color_img, pixel, vec4(fract(depth * 4.0), depth, depth < 0.0 ? 1.0 : 0.0, 1.0));
} else if (mode == 2) {
    // View-space depth visualization (z / 50)
    const float v = z / 50.0;
    imageStore(color_img, pixel, vec4(fract(v * 4.0), v, v < 0.0 ? 1.0 : 0.0, 1.0));
}
```

## Expected improvements

### Distant objects
- Less flicker on distant walls and the sky
- A steadier history with small camera movements
- Fewer false disocclusions from numeric depth errors

### Near objects
- Less blur in fast motion (the character, weapons)
- More accurate object motion vectors
- A better balance between accumulation and responsiveness

### The adaptive zone (depth 0.7-0.95)
- A smooth transition between the modes
- Prevents abrupt artifacts at the boundary

## Testing

### Visual check
```bash
# TAA at native resolution
BB_UPSCALER=taa bash run.sh

# Check:
# 1. Flicker of distant walls (Hunter's Dream, upper floors)
# 2. Weapon blur in a fast turn
# 3. Stability in slow camera movement
# 4. Disocclusion when walking around corners
```

### Debug views
```bash
# Depth visualization
BB_DEBUG_MOTION=1 bash run.sh
# Toggle: (1<<21) = raw depth, (1<<22) = view-space z

# Checking motion vectors
# Toggle: (1<<20) = blended motion, (1<<23) = reprojection, (1<<24) = difference
```

### A/B comparison
```bash
# Create the file for switching at run time
echo "0" > /tmp/bb_toggle

# Switch the bits in the game to compare
# A temporary mechanism for the old/new weights would be needed
```

## Possible risks

1. **Too conservative weights on distant objects**
   - May add smearing in fast turns
   - Fix: tune `baseWeight` (0.8 → 0.85?)

2. **Too strict a depth threshold for object motion**
   - May reject valid vectors
   - Fix: `0.0003` → `0.0005` for depth > 0.95

3. **The smoothstep range 0.7-0.95 may not be the best**
   - Bloodborne: near=0.05, far=3000
   - Most geometry is in the 0.1-0.9 range
   - Fix: profile the depth distribution in typical scenes

## Next steps (as planned then)

1. Implement depth-adaptive thresholds (done)
2. Build and test in the game
3. Collect statistics of the depth distribution
4. Tune the thresholds from real data
5. Add run-time switching for A/B tests
6. Document the final parameters

## Links

- [upscaler.md](../upscaler.md): the upscaler's overall design
- [motion_vectors.md](../motion_vectors.md): motion vectors
- TAA shader: `gpu/shadps4/video_core/host_shaders/taa.comp`
- Camera motion: `gpu/shadps4/video_core/host_shaders/camera_motion.comp`
