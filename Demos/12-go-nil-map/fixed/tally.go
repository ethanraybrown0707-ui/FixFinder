package main

import "fmt"

func main() {
	words := []string{"apple", "pear", "apple"}

	counts := make(map[string]int)

	for _, word := range words {
		counts[word]++
	}

	fmt.Println("Apples counted:", counts["apple"])
}
