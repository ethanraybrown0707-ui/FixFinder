#include <stdio.h>
#include <stdlib.h>

/* Adds up the lines of an invoice and returns the total. */
static int total_of(const int *lines, int count)
{
    int total;

    for (int i = 0; i < count; i++) {
        total += lines[i];
    }

    return total;
}

int main(void)
{
    int *lines = malloc(3 * sizeof(int));

    lines[0] = 1200;
    lines[1] = 350;
    lines[2] = 75;

    printf("Invoice total: %d\n", total_of(lines, 3));

    return 0;
}
