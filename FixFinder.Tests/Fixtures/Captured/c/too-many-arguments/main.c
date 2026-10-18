#include <stdio.h>

int area(int width, int height) {
    return width * height;
}

int main(void) {
    printf("%d\n", area(3, 4, 5));
    return 0;
}
