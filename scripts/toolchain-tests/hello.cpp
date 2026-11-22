/* Toolchain probe: the C++ features and headers splat.cpp / itwom3.0.cpp rely on. */
#include <stdio.h>
#include <math.h>
#include <string.h>
#include <unistd.h>
#include <complex>

int main(void)
{
	std::complex<double> z(3.0, 4.0);
	char buf[64];
	snprintf(buf, sizeof buf, "%.0f", rint(2.6));
	printf("hello splat-toolchain abs=%.1f rint=%s ptr=%d\n", std::abs(z), buf, (int)sizeof(void *));
	return 0;
}
