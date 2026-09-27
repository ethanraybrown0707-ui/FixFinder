#include <iostream>

int main()
{
    int *stock = new int[3];

    stock[0] = 40;
    stock[1] = 12;
    stock[2] = 7;

    std::cout << "Lowest stock: " << stock[2] << std::endl;

    delete[] stock;

    if (stock[0] == 0) {
        delete[] stock;
    }

    return 0;
}
