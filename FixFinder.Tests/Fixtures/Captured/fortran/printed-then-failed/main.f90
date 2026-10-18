program marks_check
  implicit none
  integer :: marks(3)
  integer :: i
  marks = 70
  do i = 1, 4
    print *, marks(i)
  end do
end program marks_check
