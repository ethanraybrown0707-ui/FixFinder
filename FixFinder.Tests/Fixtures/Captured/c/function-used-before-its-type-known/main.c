#include <stdio.h>

int main(void) {
    double result = half(3);
    printf("%f\n", result);
    return 0;
}

double half(int n) {
    return n / 2.0;
}
