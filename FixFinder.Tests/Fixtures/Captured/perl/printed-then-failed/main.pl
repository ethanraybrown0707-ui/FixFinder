use strict;
use warnings;
my @marks = (70, 80, 0);
for my $mark (@marks) {
    print "Mark: $mark\n";
    print "Share: ", 100 / $mark, "\n";
}
