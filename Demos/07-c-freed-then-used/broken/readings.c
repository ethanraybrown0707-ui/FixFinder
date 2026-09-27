#include <stdio.h>
#include <stdlib.h>

int main(void)
{
    int *readings = malloc(3 * sizeof(int));

    readings[0] = 12;
    readings[1] = 15;
    readings[2] = 9;

    int total = readings[0] + readings[1] + readings[2];

    free(readings);

    printf("First reading was %d\n", readings[0]);
    printf("Total is %d\n", total);

    return 0;
}
