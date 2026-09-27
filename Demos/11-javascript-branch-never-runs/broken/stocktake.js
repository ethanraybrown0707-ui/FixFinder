const shelves = ["apples", "bread", "milk"];

const countedToday = [];

for (const shelf of shelves) {
    console.log("Counting " + shelf);
}

if (countedToday.length > 0) {
    console.log("Stock take: " + countedToday.length + " lines counted");
} else {
    console.log("Nothing was counted today");
}
