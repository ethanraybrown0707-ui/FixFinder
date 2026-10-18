package main

import "fmt"

func first(marks []int) int {
	return marks[0]
}

func report(marks []int) int {
	return first(marks)
}

func main() {
	fmt.Println(report([]int{}))
}
