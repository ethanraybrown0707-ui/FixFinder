package main

import "fmt"

func main() {
	var value interface{} = "seventy"
	mark := value.(int)
	fmt.Println(mark)
}
