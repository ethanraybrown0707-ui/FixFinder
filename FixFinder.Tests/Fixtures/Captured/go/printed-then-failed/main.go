package main

import "fmt"

func main() {
	marks := []int{70, 80, 0}
	for _, mark := range marks {
		fmt.Println("Mark:", mark)
		fmt.Println("Share:", 100/mark)
	}
}
