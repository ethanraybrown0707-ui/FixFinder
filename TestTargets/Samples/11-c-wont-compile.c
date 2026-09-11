/* A C file with a compile error, not a runtime one.
 *
 * The compiler error IS the thing to look up: "error C2065" and "undeclared identifier" are
 * globally unique strings that thousands of people have pasted into a search box, and
 * FixFinder lifts the code out as its own high-weight search term.
 */
#include <stdio.h>

int main(void)
{
    int total = 0;

    for (int i = 0; i < 10; i++)
    {
        total += i;
    }

    /* The bug: this was never declared. */
    printf("total is %d, average is %d\n", total, avarage);

    return 0;
}
