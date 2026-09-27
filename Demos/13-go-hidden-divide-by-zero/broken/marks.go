package main

import "fmt"

func average(marks []int) int {
	total := 0

	for _, mark := range marks {
		total += mark
	}

	return total / len(marks)
}

func main() {
	fmt.Println("Average mark:", average([]int{70, 45, 90}))
}
