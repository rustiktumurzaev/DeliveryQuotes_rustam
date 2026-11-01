#include "dq_surcharge.h"

#define FREE_GRAMS 5000
#define UNIT_GRAMS 1000
#define UNITS_PER_STARTED_KG 50

int32_t dq_weight_surcharge(int32_t weight_grams)
{
    if (weight_grams < DQ_MIN_WEIGHT_GRAMS || weight_grams > DQ_MAX_WEIGHT_GRAMS) {
        return DQ_ERR_INVALID_WEIGHT;
    }

    if (weight_grams <= FREE_GRAMS) {
        return 0;
    }

    /* ceiling division: every started kilogram above the free part counts */
    int32_t started_kg = (weight_grams - FREE_GRAMS + UNIT_GRAMS - 1) / UNIT_GRAMS;
    return started_kg * UNITS_PER_STARTED_KG;
}
