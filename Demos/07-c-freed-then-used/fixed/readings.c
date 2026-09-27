#include <stdio.h>
#include <stdlib.h>

int main(void)
{
    int *readings = malloc(3 * sizeof(int));

    if (readings == NULL) {
        return 1;
    }

    readings[0] = 12;
    readings[1] = 15;
    readings[2] = 9;

    int total = readings[0] + readings[1] + readings[2];
    int first = readings[0];

    free(readings);

    printf("First reading was %d\n", first);
    printf("Total is %d\n", total);

    return 0;
}
