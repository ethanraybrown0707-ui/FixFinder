#include <stdio.h>

struct point { int x; };

int main(void) {
    struct point p = { 1 };
    printf("%d\n", p.y);
    return 0;
}
