#ifndef DQ_SURCHARGE_H
#define DQ_SURCHARGE_H

#include <stdint.h>

#ifdef _WIN32
#define DQ_API __declspec(dllexport)
#else
#define DQ_API __attribute__((visibility("default")))
#endif

#define DQ_MIN_WEIGHT_GRAMS 1
#define DQ_MAX_WEIGHT_GRAMS 30000
#define DQ_ERR_INVALID_WEIGHT (-1)

/*
 * Weight surcharge of one parcel, in whole currency units (no decimals, so no rounding of money).
 *
 *   weight_grams   1 .. 30000
 *   returns        0 for parcels up to 5000 g,
 *                  otherwise 50 units for every STARTED kilogram above 5000 g,
 *                  or DQ_ERR_INVALID_WEIGHT (-1) if weight_grams is outside 1 .. 30000.
 *
 * Pure function: no allocation, no global state, no pointers.
 */
DQ_API int32_t dq_weight_surcharge(int32_t weight_grams);

#endif
