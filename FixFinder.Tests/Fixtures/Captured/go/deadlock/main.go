package main

func main() {
	results := make(chan int)
	results <- 1
}
