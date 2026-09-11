/* A C program that compiles cleanly and then dies.
 *
 * There is no stack trace: a native crash on Windows prints nothing at all and simply exits
 * with 0xC0000005. FixFinder should report the crash honestly and say there is nothing to
 * search with, rather than inventing an error from an exit code.
 */
#include <stdio.h>
#include <string.h>

int main(void)
{
    char *name = NULL;

    printf("starting\n");

    /* The bug: nothing was ever allocated. */
    strcpy(name, "hello");

    printf("name is %s\n", name);

    return 0;
}
