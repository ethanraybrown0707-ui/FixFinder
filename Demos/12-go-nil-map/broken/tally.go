package main

import "fmt"

func main() {
	words := []string{"apple", "pear", "apple"}

	var counts map[string]int

	for _, word := range words {
		counts[word]++
	}

	fmt.Println("Apples counted:", counts["apple"])
}
