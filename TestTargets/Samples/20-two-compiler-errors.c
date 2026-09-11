/* Two compiler errors in one file, which is the ordinary case rather than the awkward one.
 *
 * A compiler is not like a runtime: it does not stop at the first problem, it reports everything
 * it can and then gives up. So both diagnostics below are printed by the same build - and that is
 * precisely why fixing one is not the end of it. FixFinder looks up the first, applies what it
 * finds, rebuilds, and the second is still there waiting.
 *
 * That rebuild is the part worth watching. Nothing about the source has to be re-read to know
 * whether the change helped: the build is run again and its diagnostics are fingerprinted the
 * same way a crash is, so "the same error came back" and "a different error now" are told apart
 * by comparison rather than by hope.
 *
 *   1.  error C2065: 'avarage' : undeclared identifier   - the name is misspelt here but not below
 *   2.  error C2143: syntax error : missing ';'          - the return statement has no semicolon
 */

#include <stdio.h>

static double mean(const double *values, int count)
{
    double sum = 0.0;

    for (int i = 0; i < count; i++)
    {
        sum += values[i];
    }

    /* 1. Declared as "average", used as "avarage". */
    double average = sum / count;
    return avarage;
}

int main(void)
{
    double readings[] = { 12.5, 9.0, 14.25, 11.75 };

    printf("mean: %.2f\n", mean(readings, 4));

    /* 2. No semicolon. */
    return 0
}
