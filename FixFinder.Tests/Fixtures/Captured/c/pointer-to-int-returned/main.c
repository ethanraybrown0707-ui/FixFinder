#include <stdio.h>

int main(void) {
    int mark = 70;
    int *pointer = &mark;
    return pointer.value;
}
