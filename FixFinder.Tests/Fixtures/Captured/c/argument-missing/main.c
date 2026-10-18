#include <stdio.h>

int area(int width, int height) {
    return width * height;
}

int main(void) {
    printf("%d\n", area(3));
    return 0;
}
