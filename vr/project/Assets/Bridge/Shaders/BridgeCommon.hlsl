// SPDX-License-Identifier: LicenseRef-BRIDGE
// Shared bits of BRIDGE VR's shaders: the rounded-rectangle distance the glass UI is cut with, and
// a depth-only pass body so unlit opaque surfaces still reach URP's depth texture (SSAO, glass).

#ifndef BRIDGE_COMMON_INCLUDED
#define BRIDGE_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Signed distance from p to a rounded rectangle of half extents h and corner radius r, all in the
// same units (metres here): negative inside, zero on the edge.
float RoundedRectDistance(float2 p, float2 h, float r)
{
    r = min(r, min(h.x, h.y));
    float2 q = abs(p) - (h - r);
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

// Hash noise for dithering gradients (avoids banding on large soft fills).
float BridgeHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

#endif
