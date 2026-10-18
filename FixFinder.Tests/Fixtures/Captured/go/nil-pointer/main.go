package main

import "fmt"

type Student struct{ name string }

func main() {
	var ada *Student
	fmt.Println(ada.name)
}
