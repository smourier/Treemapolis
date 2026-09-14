#ifndef TREEMAPOLIS_SHADOW
#define TREEMAPOLIS_SHADOW

// a comparison with bilinear weights, what lies outside the map reads as the farthest depth there is, so it is lit.
#define SHADOW_SAMPLER "StaticSampler(s1, filter=FILTER_COMPARISON_MIN_MAG_LINEAR_MIP_POINT, addressU=TEXTURE_ADDRESS_BORDER, addressV=TEXTURE_ADDRESS_BORDER, borderColor=STATIC_BORDER_COLOR_OPAQUE_BLACK, comparisonFunc=COMPARISON_GREATER_EQUAL)"

#endif
